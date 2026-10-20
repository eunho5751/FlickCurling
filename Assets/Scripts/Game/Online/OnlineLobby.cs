using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlickCurling.Core;
using FlickCurling.Game.Account;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Matchmaker;
using MatchIdAssignment = Unity.Services.Matchmaker.Models.MatchIdAssignment;
using NoneAssignment = Unity.Services.Matchmaker.Models.NoneAssignment;
using TicketStatusResponse = Unity.Services.Matchmaker.Models.TicketStatusResponse;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace FlickCurling.Game.Online
{
    /// <summary>
    /// The online match outside the game scene: finds an opponent (the "quick-match" queue), joins the two in a
    /// Distributed Authority session (NGO; the network manager lives across scenes), tells the server (OnlineApi) and
    /// keeps who is who: the match id, both players, which side (team) this device plays, the seed, both records.
    /// Messages for the game (<see cref="OnlineMatchSync"/>) wait in <see cref="Inbox"/> until it reads them.
    /// A player who drops out gets back in with <see cref="ResumeAsync"/> (the session id is the match id).
    /// </summary>
    public static class OnlineLobby
    {
        public const string Queue = "quick-match";
        const string NickKey = "nick", MatchKey = "match";

        public enum Resume { Ok, Gone, Failed }

        public static ISession Session { get; private set; }
        public static string MatchId { get; private set; } = "";
        public static string MyId { get; private set; } = "";
        public static string OpponentId { get; private set; } = "";
        public static string MyName { get; private set; } = "";
        public static string OpponentName { get; private set; } = "";
        public static string Map { get; private set; } = "base";
        public static int Seed { get; private set; }
        /// <summary>The side this device plays inside the match (black throws the turn order shot first); on screen this
        /// device is always black.</summary>
        public static Team MyTeam { get; private set; } = Team.B;
        public static OnlineApi.PlayerStats MyStats { get; private set; }
        public static OnlineApi.PlayerStats OpponentStats { get; private set; }
        public static bool InMatch => Session != null && !string.IsNullOrEmpty(MatchId);
        /// <summary>In a match but out of its session (the connection dropped): get back in with <see cref="ResumeAsync"/>.</summary>
        public static bool Lost => Session == null && !string.IsNullOrEmpty(MatchId) && connectingNow == 0;
        /// <summary>
        /// The match found being connected (<see cref="FindAsync"/> returns as soon as the opponent is known, and the
        /// game scene opens on it while this goes on): true once the two are connected, false if that failed (then the
        /// match is left and forgotten). Done already (true) for a match come back to.
        /// </summary>
        public static Task<bool> Connecting { get; private set; } = Task.FromResult(true);
        /// <summary>Whether the opponent is connected now: in the session, and heard from lately (their device sends a
        /// heartbeat every second; one that has crashed, lost its network or sits paused in the background goes quiet
        /// long before the session notices).</summary>
        public static bool OpponentHere
        {
            get => inSession && Time.realtimeSinceStartup - lastHeard < HeardWithin;
            private set { inSession = value; if (value) lastHeard = Time.realtimeSinceStartup; }
        }
        /// <summary>
        /// The session's clock in seconds (NGO's server time, kept by the Distributed Authority service and the same on
        /// both devices, whatever the phones' clocks say). Off the network it runs on from the last reading.
        /// </summary>
        public static double NetworkTime
        {
            get
            {
                double real = Time.realtimeSinceStartupAsDouble;
                var nm = NetworkManager.Singleton;
                if (nm && nm.IsConnectedClient)
                {
                    netTime = nm.ServerTime.Time;
                    netAt = real;
                    return netTime;
                }
                return netTime + (real - netAt);
            }
        }
        static double netTime, netAt;

        static bool inSession;
        /// <summary>Whether the opponent is in the session (as the session tells it; slower to notice than
        /// <see cref="OpponentHere"/>, but not fooled by a device that is busy for a moment).</summary>
        public static bool OpponentInSession => inSession;
        static float lastHeard = -100;
        const float HeardWithin = 5;

        /// <summary>This device is back from the background (it heard nothing meanwhile, which says nothing of the
        /// opponent): give them a fresh second or two to be heard.</summary>
        public static void Resumed() => lastHeard = Mathf.Max(lastHeard, Time.realtimeSinceStartup - HeardWithin + 2);

        public static readonly Queue<OnlineMessage> Inbox = new Queue<OnlineMessage>();
        /// <summary>Raised when the opponent leaves or comes back, or this device drops out.</summary>
        public static event Action Changed;

        static bool resuming;

        /// <summary>This run of the app, telling it from the same player on another device.</summary>
        public static readonly string DeviceId = Guid.NewGuid().ToString("N");
        /// <summary>The same player joined the match from another device, which now plays it (Account.DeviceGuard steps
        /// this device aside).</summary>
        public static event Action TakenOver;

        static OnlineMessage HelloMessage() => new OnlineMessage { Kind = OnlineMsg.Hello, Text = MyId + "|" + DeviceId };

        /// <summary>Out of the match without leaving the session (that would take the player out of it on their other
        /// device too, which plays it on).</summary>
        public static void StepOut()
        {
            if (Session == null && string.IsNullOrEmpty(MatchId)) return;
            Session = null;
            MatchId = OpponentId = OpponentName = "";
            OpponentHere = false;
            Inbox.Clear();
            if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Session = null; MatchId = MyId = OpponentId = MyName = OpponentName = ""; Map = "base"; Seed = 0;
            MyTeam = Team.B; MyStats = OpponentStats = null; OpponentHere = false; Inbox.Clear(); Changed = null;
            hooked = false; resuming = false; TakenOver = null; joinsInFlight = 0; Joining = false; Connecting = Task.FromResult(true); connectingNow = 0; BestRegion = null; regionAt = -1; measuring = null; finding = false; settling = Task.CompletedTask; leaving = Task.CompletedTask;
        }

        /// <summary>Whether the match the game scene opens is one this device came back to (taken once).</summary>
        public static bool TakeResuming()
        {
            bool r = resuming;
            resuming = false;
            return r;
        }

        static bool hooked;

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            // joins the SDK has under way (its own, after a match is found, go on even when the search is given up)
            var ms = MultiplayerService.Instance;
            ms.AddingSessionStarted += _ => joinsInFlight++;
            ms.AddingSessionFailed += (_, __) => joinsInFlight = Mathf.Max(0, joinsInFlight - 1);
            ms.AddingSessionCancelled += _ => joinsInFlight = Mathf.Max(0, joinsInFlight - 1);
            ms.SessionAdded += added =>
            {
                joinsInFlight = Mathf.Max(0, joinsInFlight - 1);
                if (!finding && connectingNow == 0 && added != Session) _ = Quit(added);   // one for a search given up: left at once
            };
            OnlineMatchSync.Received += m =>
            {
                if (m.Kind == OnlineMsg.Ping)
                {
                    if (m.Text == OpponentId) lastHeard = Time.realtimeSinceStartup;
                    return;
                }
                if (m.Kind == OnlineMsg.Hello)
                {
                    // "player|device": this player on another device has taken the seat; this one steps out
                    var parts = (m.Text ?? "").Split('|');
                    if (parts[0] == MyId && parts.Length > 1 && parts[1] != DeviceId) { StepOut(); TakenOver?.Invoke(); return; }
                    if (parts[0] == OpponentId) SetOpponentHere(true);
                }
                if (Session != null) Inbox.Enqueue(m);
            };
        }

        /// <summary>Sends <paramref name="m"/> to the other device (dropped while there is no match object).</summary>
        public static void Send(OnlineMessage m)
        {
            if (Session != null && OnlineMatchSync.Instance) OnlineMatchSync.Instance.Send(m);
        }

        // ---------------- network manager ----------------

        static NetworkManager EnsureNetworkManager()
        {
            if (NetworkManager.Singleton) return NetworkManager.Singleton;
            var go = new GameObject("Network");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<UnityTransport>();
            var nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = go.GetComponent<UnityTransport>(),
                EnableSceneManagement = false,   // each device loads its scenes itself
            };
            var prefab = Resources.Load<GameObject>(OnlineMatchSync.ResourcePath);
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = prefab });
            return nm;
        }

        static Dictionary<string, PlayerProperty> MyProperties() =>
            new Dictionary<string, PlayerProperty> { [NickKey] = new PlayerProperty(MyName, VisibilityPropertyOptions.Member) };

        static void Me()
        {
            MyId = AuthenticationService.Instance.PlayerId ?? "";
            MyName = Nickname.Display(AccountService.PlayerName);
        }

        // ---------------- finding a match ----------------

        /// <summary>
        /// Finds an opponent: true as soon as the matchmaker has paired this player and the opponent is known (who, the
        /// match's settings; the records on their way), the two being connected meanwhile (<see cref="Connecting"/>), so
        /// the game scene opens at once. False if cancelled or it failed (then nothing is left over). With
        /// <paramref name="retry"/> it keeps looking (the queue gives up after a while) until it finds one or is cancelled.
        /// </summary>
        public static async Task<bool> FindAsync(CancellationToken ct, bool retry = false)
        {
            TraceSearchStart();
            MeasureRegion();   // if not measured lately: ready by the time a match is found, usually
            while (true)
            {
                bool found = await FindOnceAsync(ct);
                if (found || !retry || ct.IsCancellationRequested) return found;
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return false; }
            }
        }

        // A matched search is never left half way: once the matchmaker has paired the two, the session is joined to the
        // end (no cancelling from here; the game scene is opening). Leaving a session whose network is still coming up
        // is what breaks the SDK (it may start it again later, alone, with errors). The limits only cover an opponent
        // gone meanwhile: the join given up after JoinLimit (its session left when it lands), the set-up after SetupLimit.
        const float JoinLimit = 30, SetupLimit = 20;
        static int joinsInFlight;
        static bool finding;
        static int connectingNow;   // connections under way (their session is this match's, not a stray to leave)

        /// <summary>Whether a match has been found and the opponent is being looked up (cancelling is not possible).</summary>
        public static bool Joining { get; private set; }

        static async Task<bool> FindOnceAsync(CancellationToken ct)
        {
            await settling;
            await LeaveAsync();
            await NetworkDown();
            if (AccountService.Status != AccountService.State.Ready) await AccountService.StartAsync();
            if (AccountService.Status != AccountService.State.Ready) return false;
            Hook();
            EnsureNetworkManager();
            finding = true;
            try { return await FindCoreAsync(ct); }
            finally
            {
                finding = false;
                Joining = false;
                if (connectingNow == 0) LeaveStray();
            }
        }

        static async Task<bool> FindCoreAsync(CancellationToken ct)
        {
            Me();
            var props = MyProperties();
            // the region measured beforehand (MeasureRegion): the SDK would otherwise measure every region when the
            // session's network is made, seconds while both players wait
            var sessionOptions = new SessionOptions { MaxPlayers = 2, PlayerProperties = props }.WithDistributedAuthorityNetwork(BestRegion);
            Trace("network region " + (BestRegion ?? "(chosen by the SDK)"));
#if UNITY_EDITOR
            // editor test mode (FlickCurling > Online Test): the editor's players meet in one fixed session, no matchmaker;
            // the session id stays the same from test to test, and whoever opens it names the match. The opponent is
            // only known from the session, so the scene waits for the connection
            if (OnlineTestMode.Enabled)
            {
                sessionOptions.SessionProperties[MatchKey] = new SessionProperty(OnlineTestMode.SessionId + "." + Guid.NewGuid().ToString("N").Substring(0, 8),
                    VisibilityPropertyOptions.Member);
                Joining = true;
                Connecting = ConnectAsync(OnlineTestMode.SessionId, sessionOptions, false);
                return await Connecting;
            }
#endif
            string matchId = await TicketAsync(ct);
            // cancelled while the ticket's answer was on its way: the match is not joined (no session exists yet)
            if (matchId == null || ct.IsCancellationRequested) return false;

            // paired: from here on the match is joined, whatever is pressed
            Joining = true;
            // who the opponent is, from the matchmaker's record of the match (before anyone is in its session)
            var opponent = await OpponentAsync(matchId);
            if (opponent == null) return false;   // (the opponent, alone in the session, looks again shortly)
            OpponentId = opponent.Value.id;
            OpponentName = opponent.Value.name;
            MatchId = matchId;   // (the session id is the match id)
            // the match's settings straight from its id, as the server works them out too: the game starts without
            // waiting for it; its record is made, and the two records fetched, meanwhile
            Settle(OnlineApi.LocalInfo(MatchId, MyId, OpponentId));
            OnlineApi.RecordReady = OnlineApi.StartMatch(MatchId, OpponentId, "base");
            FetchStats();
            resuming = false;
            Trace("opponent " + OpponentId + " (" + OpponentName + "), connecting");
            Connecting = ConnectAsync(matchId, sessionOptions, true);
            return true;
        }

        // the matchmaker's players in match matchId (their tickets carry their nicknames): the opponent, or null if that
        // could not be read
        static async Task<(string id, string name)?> OpponentAsync(string matchId)
        {
            for (int tries = 0; tries < 3; tries++)
            {
                if (tries > 0) await Task.Delay(500);
                try
                {
                    var results = await MatchmakerService.Instance.GetMatchmakingResultsAsync(matchId);
                    var p = results?.MatchProperties?.Players?.FirstOrDefault(x => x.Id != MyId);
                    if (p == null) continue;
                    string nick = null;
                    try { nick = p.CustomData?.GetAs<TicketData>()?.nick; } catch (Exception) { }
                    Trace("matchmaking results read");
                    return (p.Id, string.IsNullOrEmpty(nick) ? "?" : nick);
                }
                catch (Exception e) { Debug.LogWarning("FlickCurling: reading the matchmaking results failed: " + e.Message); }
            }
            return null;
        }

        // what a ticket's player carries (custom data, which the matchmaking results hand the opponent)
        [Serializable]
        sealed class TicketData { public string nick; }

        // into the matched session, both players there and the match object up (the game scene is open meanwhile).
        // known: the opponent and the match are known already (from the matchmaker); otherwise taken from the session.
        // False (the match left and forgotten) if it could not be done.
        static async Task<bool> ConnectAsync(string sessionId, SessionOptions sessionOptions, bool known)
        {
            connectingNow++;
            try { return await ConnectCoreAsync(sessionId, sessionOptions, known); }
            finally { connectingNow--; }
        }

        static async Task<bool> ConnectCoreAsync(string sessionId, SessionOptions sessionOptions, bool known)
        {
            Trace("joining session " + sessionId);
            var joining = MultiplayerService.Instance.CreateOrJoinSessionAsync(sessionId, sessionOptions);
            if (await Task.WhenAny(joining, Task.Delay(TimeSpan.FromSeconds(JoinLimit))) != joining)
            {
                Debug.LogWarning("FlickCurling: joining the matched session took too long (the opponent gone?)");
                await LeaveAsync();
                return false;
            }
            ISession s;
            try { s = await joining; }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: joining the matched session failed: " + e.Message);
                await LeaveAsync();
                return false;
            }
            Session = s;
            Trace("joined session " + s.Id + " (" + s.Players.Count + " in, " + (s.IsHost ? "host" : "member") + ")");
            using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(SetupLimit));
            var ct = setup.Token;
            // the opponent gone while setting up (their app closed): given up at once
            bool gone = false;
            void Left(string id) { if (id != MyId) { gone = true; setup.Cancel(); } }
            void Removed() { gone = true; setup.Cancel(); }
            s.PlayerHasLeft += Left; s.RemovedFromSession += Removed; s.Deleted += Removed;
            try
            {
                // the network and the match object come up meanwhile (the session owner does not wait for the opponent
                // to make it)
                var sync = SpawnSync(ct);
                // both players in (the opponent normally shows up within a few seconds of the match; not at all when they
                // cancelled at the very moment it was made, so not waited for long), and the match object up
                float joinBy = Time.realtimeSinceStartup + GameConfig.Load().opponentJoinSeconds;
                while (s.Players.Count < 2)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Time.realtimeSinceStartup > joinBy) { gone = true; Trace("the opponent did not show up in the session"); throw new OperationCanceledException(); }
                    await Task.Delay(200, ct);
                }
                Trace("opponent in the session");
                await sync;
                var opp = s.Players.First(p => p.Id != MyId);
                if (!known)
                {
                    OpponentId = opp.Id;
                    OpponentName = NameOf(opp);
                    MatchId = s.Properties.TryGetValue(MatchKey, out var mk) && !string.IsNullOrEmpty(mk.Value) ? mk.Value : s.Id;
                    Settle(OnlineApi.LocalInfo(MatchId, MyId, OpponentId));
                    OnlineApi.RecordReady = OnlineApi.StartMatch(MatchId, OpponentId, "base");
                    FetchStats();
                    resuming = false;
                }
                Watch(s);
                SetOpponentHere(true);
                Send(HelloMessage());
                Trace("match " + MatchId + " connected against " + OpponentId);
                return true;
            }
            catch (Exception e)
            {
                setup.Cancel();   // the network wait, if still going, stops too
                if (gone) Debug.Log("FlickCurling: the opponent did not come or left while the match was connected");
                else if (!(e is OperationCanceledException)) Debug.LogWarning("FlickCurling: connecting the match failed: " + e.Message);
                await LeaveAsync();
                return false;
            }
            finally { s.PlayerHasLeft -= Left; s.RemovedFromSession -= Removed; s.Deleted -= Removed; }
        }

        // The matchmaker ticket ("quick-match"), checked every second: the match id once paired; null if the queue gave
        // up or the search was cancelled. Cancelling deletes the ticket; nothing else exists yet to clean up.
        static async Task<string> TicketAsync(CancellationToken ct)
        {
            var mm = MatchmakerService.Instance;
            string ticket;
            try
            {
                ticket = (await mm.CreateTicketAsync(new List<Unity.Services.Matchmaker.Models.Player> { new Unity.Services.Matchmaker.Models.Player(MyId, new TicketData { nick = MyName }) },
                    new CreateTicketOptions(Queue, new Dictionary<string, object>()))).Id;
                Trace("ticket " + ticket + " created");
            }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: matchmaking failed: " + e.Message);
                return null;
            }
            while (true)
            {
                try { await Task.Delay(TicketPoll, ct); } catch (OperationCanceledException) { }
                // cancelled: the ticket goes, even if a match was just made with it (that opponent finds nobody in the
                // match and looks again after GameConfig.opponentJoinSeconds); cancelling always cancels
                if (ct.IsCancellationRequested)
                {
                    Trace("ticket " + ticket + " cancelled");
                    try { await mm.DeleteTicketAsync(ticket); }
                    catch (Exception e) { Debug.Log("FlickCurling: deleting the matchmaking ticket: " + e.Message); }
                    return null;
                }
                TicketStatusResponse status = null;
                try { status = await mm.GetTicketAsync(ticket); }
                catch (Exception e) { if (!ct.IsCancellationRequested) Debug.LogWarning("FlickCurling: checking the matchmaking ticket failed: " + e.Message); }
                switch (status?.Value)
                {
                    case MatchIdAssignment a when a.Status == MatchIdAssignment.StatusOptions.Found && !string.IsNullOrEmpty(a.MatchId):
                        TraceFound();
                        Trace("ticket " + ticket + " matched: " + a.MatchId);
                        return a.MatchId;
                    case MatchIdAssignment a when a.Status == MatchIdAssignment.StatusOptions.Failed || a.Status == MatchIdAssignment.StatusOptions.Timeout:
                        return null;   // the queue gave up (its time out): looked for again
                    case NoneAssignment n when n.Status == NoneAssignment.StatusOptions.Failed || n.Status == NoneAssignment.StatusOptions.Timeout:
                        return null;
                }
            }
        }
        const int TicketPoll = 1000;
        /// <summary>
        /// The matchmaking steps, in the log (to follow a match from both devices' logs), each with the time since the
        /// search started and since the match was found, and the time the step took (since the step before).
        /// </summary>
        public static void Trace(string what)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            string Since(double t) => t < 0 ? "-" : (now - t).ToString("0.00") + "s";
            Debug.Log($"FlickCurling [match] {what}  (step {Since(traceLast)}, search {Since(traceSearch)}, match {Since(traceFound)})");
            traceLast = now;
        }

        /// <summary>The clock for <see cref="Trace"/>: a search starts / a match is found.</summary>
        public static void TraceSearchStart() { traceSearch = traceLast = Time.realtimeSinceStartupAsDouble; traceFound = -1; }
        static void TraceFound() => traceFound = Time.realtimeSinceStartupAsDouble;
        static double traceSearch = -1, traceFound = -1, traceLast = -1;

        static string NameOf(IReadOnlyPlayer p) =>
            p.Properties.TryGetValue(NickKey, out var nick) && !string.IsNullOrEmpty(nick.Value) ? nick.Value : "?";

        // the match's settings (who throws the turn order shot first, the seed, the map)
        static void Settle(OnlineApi.MatchInfo info)
        {
            Map = string.IsNullOrEmpty(info.Map) ? "base" : info.Map;
            Seed = (int)info.Seed;
            MyTeam = info.FirstThrower == MyId ? Team.B : Team.W;
        }

        // both records, filled in when they come (the records panel and the sheet show them then)
        static async void FetchStats()
        {
            MyStats = OpponentStats = null;
            string me = MyId, opponent = OpponentId;
            var stats = await OnlineApi.GetStats(me, opponent);
            if (MyId != me || OpponentId != opponent) return;   // another match by now
            MyStats = stats?.FirstOrDefault(x => x.PlayerId == me);
            OpponentStats = stats?.FirstOrDefault(x => x.PlayerId == opponent);
        }

        // connected to the session's network, and the match object there: the session owner makes it (checked again
        // while waiting, as who owns the session can be settled a moment after connecting), the other gets it
        static async Task SpawnSync(CancellationToken ct)
        {
            var nm = NetworkManager.Singleton;
            while (!nm.IsConnectedClient) { ct.ThrowIfCancellationRequested(); await Task.Delay(50, ct); }
            Trace("network connected (" + (nm.LocalClient.IsSessionOwner ? "session owner" : "not the owner") + ")");
            bool spawned = false;
            while (OnlineMatchSync.Instance == null)
            {
                ct.ThrowIfCancellationRequested();
                if (!spawned && nm.LocalClient.IsSessionOwner)
                {
                    spawned = true;
                    var prefab = Resources.Load<GameObject>(OnlineMatchSync.ResourcePath);
                    UnityEngine.Object.Instantiate(prefab).GetComponent<NetworkObject>().Spawn();
                    Trace("match object made");
                }
                await Task.Delay(50, ct);
            }
            Trace("match object up");
        }

        static void Watch(ISession s)
        {
            s.PlayerHasLeft += id => { if (id == OpponentId) SetOpponentHere(false); };
            s.PlayerJoined += id => { if (id == OpponentId) SetOpponentHere(true); };
            s.RemovedFromSession += () => Dropped(s);
            s.Deleted += () => Dropped(s);
        }

        // this device is out of the session (kept as the match, so it can get back in)
        static void Dropped(ISession s)
        {
            if (Session != s) return;
            Session = null;
            OpponentHere = false;
            Changed?.Invoke();
        }

        static void SetOpponentHere(bool here)
        {
            bool was = inSession;
            OpponentHere = here;
            if (was != here) Changed?.Invoke();
        }

        // ---------------- coming back ----------------

        /// <summary>
        /// Gets back into match <paramref name="matchId"/>'s session (after a dropped connection, or on starting the game
        /// with a match still going) and sets everything up again. Gone: the session is no more (both players left).
        /// <paramref name="forGame"/>: the game scene is to open on it (it then asks the opponent where the match is).
        /// </summary>
        public static async Task<Resume> ResumeAsync(string matchId, CancellationToken ct, bool forGame = false)
        {
            // the session it joins is this one's, not a stray to leave
            finding = true;
            try { return await ResumeCoreAsync(matchId, ct, forGame); }
            finally { finding = false; }
        }

        static async Task<Resume> ResumeCoreAsync(string matchId, CancellationToken ct, bool forGame)
        {
            if (string.IsNullOrEmpty(matchId)) return Resume.Gone;
            if (AccountService.Status != AccountService.State.Ready) await AccountService.StartAsync();
            if (AccountService.Status != AccountService.State.Ready) return Resume.Failed;
            Hook();
            EnsureNetworkManager();
            Me();
            ISession s = null;
            // the session id is the match id (in the editor test session, the part before the dot)
            string sessionId = matchId.Split('.')[0];
            try { s = await MultiplayerService.Instance.ReconnectToSessionAsync(sessionId, new ReconnectSessionOptions(), ct); }
            catch (SessionException e) when (e.Error == SessionError.SessionNotFound) { return Resume.Gone; }
            catch (OperationCanceledException) { return Resume.Failed; }
            catch (Exception e) { Debug.Log("FlickCurling: reconnecting failed, joining again: " + e.Message); }
            if (s == null)
                try { s = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, new JoinSessionOptions { PlayerProperties = MyProperties() }); }
                catch (SessionException e) when (e.Error == SessionError.SessionNotFound) { return Resume.Gone; }
                catch (Exception e) { Debug.LogWarning("FlickCurling: joining the match again failed: " + e.Message); return Resume.Failed; }
            Session = s;
            Connecting = Task.FromResult(true);
            try
            {
                await SpawnSync(ct);
                MatchId = matchId;
                var info = await OnlineApi.GetMatch(matchId);
                string[] players = info != null && info.Exists ? info.Players : s.Players.Select(p => p.Id).ToArray();
                OpponentId = players.FirstOrDefault(p => p != MyId) ?? "";
                var opp = s.Players.FirstOrDefault(p => p.Id == OpponentId);
                if (opp != null) OpponentName = NameOf(opp);
                Settle(info != null && info.Exists ? info : OnlineApi.LocalInfo(matchId, MyId, OpponentId));
                OnlineApi.RecordReady = Task.FromResult(info);   // the record is there already (or gone)
                FetchStats();
                Watch(s);
                OpponentHere = opp != null;
                Send(HelloMessage());
                if (forGame) resuming = true;
                Changed?.Invoke();
                return Resume.Ok;
            }
            catch (Exception e)
            {
                if (!(e is OperationCanceledException)) Debug.LogWarning("FlickCurling: getting back into the match failed: " + e.Message);
                Session = null;
                await Quit(s);
                return Resume.Failed;
            }
        }

        /// <summary>
        /// On starting the game (Docs/OnlineMatch.md 5.4): the match the server has this player in, if any. Still going
        /// with its session there: back into it (true; the game scene is to open on it). Over, or gone: just seen. Going
        /// but its session is gone (both players left): both lose.
        /// </summary>
        public static async Task<bool> ResumeActiveAsync(CancellationToken ct)
        {
            string id = await OnlineApi.ActiveMatchId();
            if (string.IsNullOrEmpty(id) || ct.IsCancellationRequested) return false;
            var info = await OnlineApi.GetMatch(id);
            if (info == null || ct.IsCancellationRequested) return false;   // the server cannot be reached: leave it for now
            if (info.Exists && info.Status == "ongoing")
            {
                var r = await ResumeAsync(id, ct, forGame: true);
                if (r == Resume.Ok) return true;
                if (r == Resume.Failed) return false;
                await OnlineApi.ResolveAbandoned(id);
            }
            await OnlineApi.Acknowledge(id);
            return false;
        }

        // the network fully down before the next session starts it again: the session being left stops it a moment
        // later, and a join the SDK still has under way (from a search given up) lands and is left first. The network is
        // never stopped from here while such a join is starting it (that only breaks the join, with an error); a join
        // stuck waiting for a code that never comes does not hold the network, so after a while the search goes on.
        static async Task NetworkDown()
        {
            await leaving;
            float until = Time.realtimeSinceStartup + JoinLimit;
            while (joinsInFlight > 0 && Time.realtimeSinceStartup < until) await Task.Delay(200);
            LeaveStray();
            await leaving;
            var nm = NetworkManager.Singleton;
            if (!nm) return;
            until = Time.realtimeSinceStartup + 5;
            while ((nm.ShutdownInProgress || nm.IsListening) && Time.realtimeSinceStartup < until) await Task.Delay(100);
        }

        // a session the SDK joined for a search that was given up or cancelled: left as soon as it shows up
        static void LeaveStray()
        {
            foreach (var other in MultiplayerService.Instance.Sessions.Values.ToList())
                if (other != Session) leaving = Quit(other);
        }

        static Task leaving = Task.CompletedTask;

        /// <summary>
        /// Leaves the session once the opponent is out of it and the SDK has taken that in (the one left becomes the
        /// session's owner and the SDK restarts its network: leaving under that only breaks it, with errors). Used when
        /// the opponent is the one going (a forfeit, a no-show). A next search waits for it.
        /// </summary>
        public static Task LeaveSettledAsync()
        {
            settling = LeaveSettledCore();
            return settling;
        }

        static async Task LeaveSettledCore()
        {
            var s = Session;
            if (s == null) return;
            float until = Time.realtimeSinceStartup + 5;
            while (inSession && Session == s && Time.realtimeSinceStartup < until) await Task.Delay(100);
            await Task.Delay(TimeSpan.FromSeconds(OwnerSettle));
            if (Session == s) await LeaveAsync();
        }

        const float OwnerSettle = 2;

        /// <summary>Leaves the session, after a settling leave under way (if any) has had its moment.</summary>
        public static async Task LeaveWhenSettledAsync()
        {
            await settling;
            await LeaveAsync();
        }
        static Task settling = Task.CompletedTask;

        // ---------------- network region ----------------

        /// <summary>The Relay region nearest this device (lowest latency and loss), measured once in a while ahead of a
        /// match; null until measured (then the SDK measures when the session's network is made).</summary>
        public static string BestRegion { get; private set; }
        static float regionAt = -1;
        static Task measuring;
        const float RegionKeep = 10 * 60;   // measured again after this long (the device may have moved, the network changed)

        /// <summary>Measures the nearest region in the background, unless a recent measurement is there or one is under
        /// way.</summary>
        public static void MeasureRegion()
        {
            if (measuring != null && !measuring.IsCompleted) return;
            if (BestRegion != null && Time.realtimeSinceStartup - regionAt < RegionKeep) return;
            if (AccountService.Status != AccountService.State.Ready) return;
            measuring = MeasureCore();
        }

        static async Task MeasureCore()
        {
            try
            {
                var results = await Unity.Services.Qos.QosService.Instance.GetSortedQosResultsAsync("relay", null);
                if (results != null && results.Count > 0)
                {
                    BestRegion = results[0].Region;
                    regionAt = Time.realtimeSinceStartup;
                    Debug.Log("FlickCurling: nearest network region " + BestRegion);
                }
            }
            catch (Exception e) { Debug.LogWarning("FlickCurling: measuring the network regions failed: " + e.Message); }
        }

        /// <summary>Leaves the session after <paramref name="seconds"/> (for a last message to go out first).</summary>
        public static async Task LeaveAfterAsync(float seconds)
        {
            var s = Session;
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            if (Session == s) await LeaveAsync();
        }

        /// <summary>Leaves the session (if any) and forgets the match.</summary>
        public static async Task LeaveAsync()
        {
            var s = Session;
            Session = null;
            MatchId = OpponentId = OpponentName = "";
            OpponentHere = false;
            Inbox.Clear();
            if (s != null) leaving = Quit(s);
            await leaving;
        }

        static async Task Quit(ISession s)
        {
            if (s == null) return;
            try { await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: leaving the session failed: " + e.Message); }
        }
    }
}
