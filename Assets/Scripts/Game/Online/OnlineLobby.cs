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
        public static bool Lost => Session == null && !string.IsNullOrEmpty(MatchId);
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
            hooked = false; resuming = false; TakenOver = null; joinStartedAt = -1; finding = false; leaving = Task.CompletedTask;
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
            MultiplayerService.Instance.AddingSessionStarted += _ => joinStartedAt = Time.realtimeSinceStartup;
            MultiplayerService.Instance.SessionAdded += added => { if (!finding && added != Session) _ = Quit(added); };
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
        /// Finds an opponent and sets the match up (session, match object, server record, records). Returns false if
        /// cancelled or it failed (then nothing is left over). With <paramref name="retry"/> it keeps looking (the queue
        /// gives up after a while) until it finds one or is cancelled.
        /// </summary>
        public static async Task<bool> FindAsync(CancellationToken ct, bool retry = false)
        {
            while (true)
            {
                bool found = await FindOnceAsync(ct);
                if (found || !retry || ct.IsCancellationRequested) return found;
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return false; }
            }
        }

        // once the matchmaker has paired the two, the SDK joins the match's session on its own (our token no longer stops
        // it) and the joining player waits for the other to publish the network's join code, up to minutes if that never
        // comes. These bound it: a join that takes longer is given up (and its session left once it lands), and so is a
        // set-up after it.
        const float JoinLimit = 15, SetupLimit = 20;
        static float joinStartedAt = -1;
        static bool finding;

        static async Task<bool> FindOnceAsync(CancellationToken ct)
        {
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
                LeaveStray();
            }
        }

        static async Task<bool> FindCoreAsync(CancellationToken outer)
        {
            using var join = CancellationTokenSource.CreateLinkedTokenSource(outer);
            var ct = join.Token;
            float findStart = Time.realtimeSinceStartup;
            _ = Watchdog();
            async Task Watchdog()
            {
                while (!join.IsCancellationRequested)
                {
                    try { await Task.Delay(500, join.Token); } catch (OperationCanceledException) { return; }
                    if (joinStartedAt > findStart && Time.realtimeSinceStartup - joinStartedAt > JoinLimit)
                    {
                        Debug.LogWarning("FlickCurling: joining the matched session took too long; looking again");
                        join.Cancel();
                    }
                }
            }
            Me();
            var props = MyProperties();
            var sessionOptions = new SessionOptions { MaxPlayers = 2, PlayerProperties = props }.WithDistributedAuthorityNetwork();
            ISession s;
            try
            {
#if UNITY_EDITOR
                // editor test mode (FlickCurling > Online Test): the editor's players meet in one fixed session, no matchmaker
                if (OnlineTestMode.Enabled)
                {
                    // the session id stays the same from test to test; whoever opens it names the match
                    sessionOptions.SessionProperties[MatchKey] = new SessionProperty(OnlineTestMode.SessionId + "." + Guid.NewGuid().ToString("N").Substring(0, 8),
                        VisibilityPropertyOptions.Member);
                    s = await MultiplayerService.Instance.CreateOrJoinSessionAsync(OnlineTestMode.SessionId, sessionOptions);
                }
                else
#endif
                s = await MultiplayerService.Instance.MatchmakeSessionAsync(
                    new MatchmakerOptions { QueueName = Queue, PlayerProperties = props }, sessionOptions, ct);
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception e)
            {
                if (!outer.IsCancellationRequested) Debug.LogWarning("FlickCurling: matchmaking failed: " + e.Message);
                return false;
            }
            join.Cancel();   // the watchdog's part is done
            if (outer.IsCancellationRequested) { await Quit(s); return false; }
            Session = s;
            using var setup = CancellationTokenSource.CreateLinkedTokenSource(outer);
            setup.CancelAfter(TimeSpan.FromSeconds(SetupLimit));
            ct = setup.Token;
            // the opponent gone while setting up (they cancelled, or their app closed): look again at once
            bool gone = false;
            void Left(string id) { if (id != MyId) { gone = true; setup.Cancel(); } }
            void Removed() { gone = true; setup.Cancel(); }
            s.PlayerHasLeft += Left; s.RemovedFromSession += Removed; s.Deleted += Removed;
            try
            {
                // both players in, and the match object up
                while (s.Players.Count < 2) { ct.ThrowIfCancellationRequested(); await Task.Delay(200, ct); }
                await SpawnSync(ct);
                var opp = s.Players.First(p => p.Id != MyId);
                OpponentId = opp.Id;
                OpponentName = NameOf(opp);
                MatchId = s.Properties.TryGetValue(MatchKey, out var mk) && !string.IsNullOrEmpty(mk.Value) ? mk.Value : s.Id;
                // the two server calls at once (neither needs the other's answer)
                var starting = OnlineApi.StartMatch(MatchId, OpponentId, "base");
                var stats = OnlineApi.GetStats(MyId, OpponentId);
                var info = await starting ?? OnlineApi.LocalInfo(MatchId, MyId, OpponentId);
                await Settle(info, stats, ct);
                Watch(s);
                SetOpponentHere(true);
                Send(HelloMessage());
                resuming = false;
                return true;
            }
            catch (Exception e)
            {
                if (gone) Debug.Log("FlickCurling: the opponent left while the match was set up; looking again");
                else if (!(e is OperationCanceledException)) Debug.LogWarning("FlickCurling: setting the match up failed: " + e.Message);
                await LeaveAsync();
                return false;
            }
            finally { s.PlayerHasLeft -= Left; s.RemovedFromSession -= Removed; s.Deleted -= Removed; }
        }

        static string NameOf(IReadOnlyPlayer p) =>
            p.Properties.TryGetValue(NickKey, out var nick) && !string.IsNullOrEmpty(nick.Value) ? nick.Value : "?";

        // the match's settings and both records
        static async Task Settle(OnlineApi.MatchInfo info, Task<List<OnlineApi.PlayerStats>> statsCall, CancellationToken ct)
        {
            Map = string.IsNullOrEmpty(info.Map) ? "base" : info.Map;
            Seed = (int)info.Seed;
            MyTeam = info.FirstThrower == MyId ? Team.B : Team.W;
            var stats = await statsCall;
            ct.ThrowIfCancellationRequested();
            MyStats = stats?.FirstOrDefault(x => x.PlayerId == MyId);
            OpponentStats = stats?.FirstOrDefault(x => x.PlayerId == OpponentId);
        }

        static async Task SpawnSync(CancellationToken ct)
        {
            var nm = NetworkManager.Singleton;
            while (!nm.IsConnectedClient) { ct.ThrowIfCancellationRequested(); await Task.Delay(100, ct); }
            if (OnlineMatchSync.Instance == null && nm.LocalClient.IsSessionOwner)
            {
                var prefab = Resources.Load<GameObject>(OnlineMatchSync.ResourcePath);
                UnityEngine.Object.Instantiate(prefab).GetComponent<NetworkObject>().Spawn();
            }
            while (OnlineMatchSync.Instance == null) { ct.ThrowIfCancellationRequested(); await Task.Delay(100, ct); }
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
            try
            {
                await SpawnSync(ct);
                MatchId = matchId;
                var info = await OnlineApi.GetMatch(matchId);
                string[] players = info != null && info.Exists ? info.Players : s.Players.Select(p => p.Id).ToArray();
                OpponentId = players.FirstOrDefault(p => p != MyId) ?? "";
                var opp = s.Players.FirstOrDefault(p => p.Id == OpponentId);
                if (opp != null) OpponentName = NameOf(opp);
                await Settle(info != null && info.Exists ? info : OnlineApi.LocalInfo(matchId, MyId, OpponentId), OnlineApi.GetStats(MyId, OpponentId), ct);
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

        // the network fully down before the next session starts it again (a session being left stops it a moment later)
        static async Task NetworkDown()
        {
            await leaving;
            var nm = NetworkManager.Singleton;
            if (!nm) return;
            float until = Time.realtimeSinceStartup + 5;
            while ((nm.ShutdownInProgress || nm.IsListening) && Time.realtimeSinceStartup < until) await Task.Delay(100);
            if (nm.IsListening && !nm.ShutdownInProgress) nm.Shutdown();
        }

        // a session the SDK joined for a search that was given up or cancelled: left as soon as it shows up
        static void LeaveStray()
        {
            foreach (var other in MultiplayerService.Instance.Sessions.Values.ToList())
                if (other != Session) _ = Quit(other);
        }

        static Task leaving = Task.CompletedTask;

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
