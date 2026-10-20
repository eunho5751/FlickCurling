using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FlickCurling.Core;
using FlickCurling.Game.Online;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// Online play (Docs/OnlineMatch.md) on top of the offline flow: whose turn is played on this device, the shot sent
    /// as input first and then as a record both devices play in step (only the shooter's device runs the physics; the
    /// other catches up), the cards that wait for both players, the result reported the moment the match ends, play
    /// again with matchmaking. A device dropped from the match's network has lost it (the other wins); one back from a
    /// short spell in the background takes the match from the opponent's card at once.
    /// The two devices talk through <see cref="OnlineLobby"/> (messages in its inbox).
    /// </summary>
    public sealed partial class GameController
    {
        // the cards both players press before going on (their keys in Ready messages); the end cards add the end number
        const int CardLsdStart = 1, CardLsdResult = 2, CardEnd = 10;
        // past the shared deadline, the shooter's void or shot is on its way for a moment at most (both devices go by the
        // same deadline): after this long the waiting side calls the stone lost itself
        const float Overdue = 3;
        const float CommitStuck = 8;                       // input but no record this long: run the shot here
        const float NextTurnWait = 3;                      // the next turn waits this long at most for the other's playback

        /// <summary>Where a match is, as each device's card keeps it (<see cref="OnlinePlayerState.State"/>).</summary>
        enum Phase { LsdStartCard = 1, Turn, LsdResultCard, EndCard, Over }

        ShotPlayback playback;
        readonly HashSet<int> myReady = new HashSet<int>(), oppReady = new HashSet<int>(), oppDone = new HashSet<int>();
        readonly List<OnlineMessage> held = new List<OnlineMessage>();   // for a later turn than this one
        (int key, Action go)? waitingCard;
        OnlineMessage? commit;       // the opponent's input for this turn, until its record comes
        float commitAt, doneAt = -100;
        bool awaitingState;          // back from the background: the match is about to be taken from the opponent's card
        Task reportTask;
        string reportedMatch = "";
        // play again: matchmaking from the result card
        CancellationTokenSource rematch;
        float rematchStart;

        bool IsOnline => mode == Mode.Online;
        bool IsRemote(Team t) => IsOnline && t != OnlineLobby.MyTeam;
        int TurnSeq => m.InLsd && m.Lsd != null ? m.Lsd.Idx : 1000 + m.End * 100 + m.Shot;

        // ---------------- start ----------------

        void StartOnline()
        {
            awaitingState = false;
            playingMatch = OnlineLobby.MatchId;
            connect = OnlineLobby.Connecting;
            connecting = !connect.IsCompleted;
            OnlineLobby.Trace("game scene opened");
            StartMatch(Mode.Online, OnlineLobby.Map);
        }

        void OnlineReset()
        {
            vsUp = false;
            playback = null;
            myReady.Clear(); oppReady.Clear(); oppDone.Clear(); held.Clear();
            waitingCard = null; commit = null; doneAt = -100;
            reportTask = null; reportedMatch = "";
            published = (-1, -1, -1); myStamp = appliedStamp = 0; myData = null; resyncUntil = -1;
            arriving = false; oppArrived = false; oppSeen = false; playStart = null;
            myVsAt = oppVsAt = null;   // (the opponent's may come in before this device's panel is up)
            // who is playing, kept for the whole match (the lobby forgets the opponent once the session is left)
            myName = OnlineLobby.MyName; oppName = OnlineLobby.OpponentName;
            myStats = OnlineLobby.MyStats; oppStats = OnlineLobby.OpponentStats;
        }

        void OnlineFirstCard(Action show)
        {
            if (!connecting) Arrived();
            ScreenFader.WhenClear(() =>
            {
                if (!this) return;
                // both panels end together: each device tells the other when its panel appeared (network time, so once
                // connected), and both end it the panel's time after the later of the two
                int tok = token;
                vsPanel = VsPanel.Show(transform, hud.Canvas.sortingOrder + 20, () => Now, VsEnd, () => connecting,
                    () => { vsUp = true; vsUpReal = Time.unscaledTime; OnlineLobby.Trace("records panel shown"); if (!connecting) VsShown(); },
                    () => { OnlineLobby.Trace("records panel closed (both shown " + (oppVsAt.HasValue ? "" : "not ") + "known), first card"); if (this && tok == token) show(); });
            });
        }

        // here: the opponent waits for this before the match starts (and this device for theirs)
        void Arrived()
        {
            myReady.Add(CardArrive);
            OnlineSend(OnlineMsg.Ready, CardArrive);
            arriving = true; arriveStart = Time.unscaledTime;
        }

        // on the network and the panel up: when it appeared (in network time, dated back to before the connection, if it
        // came up first) and when this device connected, to the other
        void VsShown()
        {
            myVsConnected = Now; vsShownReal = Time.unscaledTime;
            myVsAt = myVsConnected - (Time.unscaledTime - vsUpReal);
            OnlineSend(OnlineMsg.Ready, CardVs, a: myVsAt.Value, b: myVsConnected);
        }
        bool vsUp;
        float vsUpReal;
        double myVsConnected, oppVsConnected;

        // ---------------- connecting ----------------

        // the scene opens as soon as the opponent is known, the two devices connecting behind the records panel (which
        // says so): then the match goes on as usual; if that fails, back to the menu, looking for another opponent
        Task<bool> connect;
        bool connecting;

        void TickConnect()
        {
            if (!connecting || !connect.IsCompleted) return;
            connecting = false;
            if (!connect.Result) { ConnectFailed(); return; }
            OnlineLobby.Trace("connected in the game");
            Arrived();
            if (vsUp) VsShown();
            RenderHud();
        }

        void ConnectFailed()
        {
            OnlineLobby.Trace("could not connect the match, looking again");
            if (vsPanel) Destroy(vsPanel);
            string id = playingMatch;
            reportedMatch = id;   // nothing to report
            OffTheSheet();
            if (!string.IsNullOrEmpty(id)) _ = OnlineApi.CancelMatch(id);
            MatchLaunch.SetSearchAgain();
            ToMenu();
        }
        const float VsSeconds = 2.5f, VsWaitMax = 10;
        const int CardVs = 3;   // Ready key: my records panel appeared (A: when, B: when this device connected; network time)
        GameObject vsPanel;
        double? myVsAt, oppVsAt;
        float vsShownReal;

        // when the panel ends, the same on both devices: VsSeconds after the later of the two appeared (the wait for the
        // connection counted in), or as soon as both are connected if that took longer; if the opponent's word never comes
        // (gone, or something lost), from this device's own after a while
        double? VsEnd()
        {
            if (!myVsAt.HasValue) return null;
            if (oppVsAt.HasValue) return Math.Max(Math.Max(myVsAt.Value, oppVsAt.Value) + VsSeconds, Math.Max(myVsConnected, oppVsConnected));
            if ((oppSeen && !OnlineLobby.OpponentPresent) || Time.unscaledTime - vsShownReal > VsWaitMax) return Math.Max(myVsAt.Value + VsSeconds, myVsConnected);
            return null;
        }

        // ---------------- an opponent who never arrives ----------------

        const int CardArrive = 0;          // Ready key: this device has opened the match
        // the opponent has this long to open the match too (it is off at once if either device drops from it meanwhile:
        // TickPresence)
        const float ArriveWait = 20;
        bool arriving, oppArrived;
        string playingMatch = "";   // the match this scene plays (a new one found from its result card is the next scene's)
        float arriveStart;

        void TickArrival()
        {
            if (!arriving) return;
            if (oppReady.Contains(CardArrive) || oppArrived)
            {
                OnlineLobby.Trace("opponent arrived in the game");
                // both here: the match has started (from now on leaving it counts)
                arriving = false;
                if (!string.IsNullOrEmpty(OnlineLobby.MatchId)) _ = OnlineApi.MarkStarted(OnlineLobby.MatchId);
                return;
            }
            if (Time.unscaledTime - arriveStart > ArriveWait) NoShow();
        }

        // the opponent never came: the match is off (no record for anyone) and this player looks for another one
        const float NoShowSettle = 2;

        void NoShow()
        {
            arriving = false;
            if (vsPanel) Destroy(vsPanel);
            string id = OnlineLobby.MatchId;
            reportedMatch = id;   // nothing to report
            OffTheSheet();
            if (!string.IsNullOrEmpty(id)) _ = OnlineApi.CancelMatch(id);
            // the session is left (and the next search started) a moment later: the opponent having just left, the SDK is
            // still taking that in (it restarts the session's network as the new owner), and leaving under it only breaks
            // that with errors
            _ = OnlineLobby.LeaveSettledAsync();
            LookAgainSoon().Forget();
            async UniTaskVoid LookAgainSoon()
            {
                if (await UniTask.Delay(TimeSpan.FromSeconds(NoShowSettle), DelayType.Realtime, cancellationToken: destroyCancellationToken).SuppressCancellationThrow()) return;
                if (rematch == null && state == St.Over && hud.CardVisible) { StartRematch(); hud.SetCardButton(0, Elapsed(), true); }
            }
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("mainmenu.online"),
                Title = Loc.T("online.noShow"),
                Buttons =
                {
                    new CardButton(rematch != null ? Elapsed() : Loc.T("over.again"), ToggleRematch) { KeepOpen = true },
                    new CardButton(Loc.T("over.menu"), ToMenu)
                }
            });
        }

        // whatever was under way stops: no turn, no shot, no card
        void OffTheSheet()
        {
            token++;
            state = St.Over;
            playback = null; commit = null; waitingCard = null; clock = -1;
            ResetShotInput(); aiPull = null;
            awaitingState = false; resyncUntil = -1;
            hud.ClearToasts(); hud.HideCard(); hud.HideWaiting();
            RenderHud();
        }

        void OnlineDestroy()
        {
            rematch?.Cancel();
            rematch = null;
        }

        void OnlineLeave()
        {
            rematch?.Cancel();
            rematch = null;
            if (IsOnline) _ = leaveSoon ? OnlineLobby.LeaveAfterAsync(0.5f) : OnlineLobby.LeaveWhenSettledAsync();
        }
        bool leaveSoon;

        void OnlineSend(OnlineMsg kind, int? seq = null, double a = 0, double b = 0, double c = 0, double t = 0, byte[] data = null, string text = null) =>
            OnlineLobby.Send(new OnlineMessage { Kind = kind, Seq = seq ?? TurnSeq, A = a, B = b, C = c, T = t, Data = data, Text = text });

        // ---------------- names and records ----------------

        string myName = "", oppName = "";
        OnlineApi.PlayerStats myStats, oppStats;

        string OnlineSideName(Team t)
        {
            return t == OnlineLobby.MyTeam ? myName : oppName;
        }

        string[] OnlineRecords()
        {
            // filled in when the server's answer comes (the match does not wait for it)
            myStats ??= OnlineLobby.MyStats; oppStats ??= OnlineLobby.OpponentStats;
            string Rec(OnlineApi.PlayerStats s) => Loc.T("hud.record", ("w", s?.W ?? 0), ("l", s?.L ?? 0), ("d", s?.D ?? 0));   // none: all zero
            var mine = Rec(myStats); var theirs = Rec(oppStats);
            return Left == OnlineLobby.MyTeam ? new[] { mine, theirs } : new[] { theirs, mine };
        }

        // ---------------- cards both players press ----------------

        /// <summary>A card's go-on button: online it waits for both players ("waiting for the opponent" once pressed).</summary>
        CardButton SyncButton(int key, string label, Action go)
        {
            if (!IsOnline) return new CardButton(label, go);
            bool pressed = myReady.Contains(key);
            return new CardButton(pressed ? Loc.T("online.waiting") : label, () => PressSync(key, go))
                { KeepOpen = true, Interactable = !pressed };
        }

        void PressSync(int key, Action go)
        {
            if (!myReady.Add(key)) return;
            OnlineSend(OnlineMsg.Ready, key);
            waitingCard = (key, go);
            hud.SetCardButton(0, Loc.T("online.waiting"), false);
            CheckCard();
        }

        // both pressed (or the opponent is not here to wait for): on
        void CheckCard()
        {
            if (!waitingCard.HasValue) return;
            var (key, go) = waitingCard.Value;
            if (!oppReady.Contains(key) && OnlineLobby.OpponentPresent) return;
            waitingCard = null;
            hud.HideCard();
            go();
        }

        // ---------------- turns ----------------

        void OnlineTurnStarted()
        {
            if (!IsOnline) return;
            commit = null;
            // this device throws: its deadline is the turn's, for both
            if (state == St.Aim) OnlineSend(OnlineMsg.Deadline, a: deadline);
        }

        // this device's shot: the input, before anything is worked out
        void OnlineCommit(double ang, double pw)
        {
            lastAng = ang; lastPw = pw;
            OnlineSend(OnlineMsg.Commit, a: ang, b: pw, c: m.LaunchX);
        }
        double lastAng, lastPw;

        // the released shot run to rest here and recorded; then both devices play the record, from the same moment (network
        // time), and it goes on this device's card meanwhile
        void OnlineRecordShot()
        {
            var before = m.Capture();
            var rec = ShotRecord.Record(m);
            m.Restore(before);
            playback = new ShotPlayback(m, rec);
            playStart = Now;
            var bytes = rec.ToBytes();
            OnlineSend(OnlineMsg.Shot, a: lastAng, b: lastPw, c: m.LaunchX, t: playStart.Value, data: bytes);
            OnlinePlayerState.PublishShot(TurnSeq, lastAng, lastPw, m.LaunchX, playStart.Value, bytes);
        }

        // the opponent's shot: their stone goes out as they threw it and plays along their record, catching up with where
        // their device has it (it started there when they threw)
        void PlayRemoteShot(OnlineMessage msg, ShotRecord rec)
        {
            m.LaunchX = msg.C;
            Release(msg.A, msg.B);
            playback = rec != null ? new ShotPlayback(m, rec) : null;
            playStart = rec != null && msg.T > 0 ? msg.T : Now;
            if (rec == null)
            {
                // no record (its device went before sending it): this device runs the shot from the input instead
                var before = m.Capture();
                rec = ShotRecord.Record(m);
                m.Restore(before);
                playback = new ShotPlayback(m, rec);
            }
            commit = null;
            OnlinePlayerState.PublishShot(TurnSeq, msg.A, msg.B, msg.C, playStart.Value, msg.Data ?? rec.ToBytes());
        }

        // when the shot playing here started on the shooter's device (network time; null offline)
        double? playStart;
        bool jumpPlayback;   // straight to where the shooter's device has it (back from the background, under the dim)

        /// <summary>Plays the shot on by a frame: online, behind the shooter's device (the record came late), it runs
        /// faster (GameConfig.catchUpSpeed) until level, skipping at once only what it could not make up by the end.</summary>
        void AdvancePlayback(float dt)
        {
            if (!IsOnline || !playStart.HasValue) { playback.Advance(dt); return; }
            double behind = Now - playStart.Value - playback.Elapsed;
            if (jumpPlayback) { jumpPlayback = false; playback.Advance(Math.Max(0, behind) + dt); return; }
            if (behind <= 0) { playback.Advance(dt); return; }
            double speed = Math.Max(1.01, catchUpSpeed);
            double canGain = (playback.Duration - playback.Elapsed) * (speed - 1) / speed;
            if (behind > canGain) { playback.Advance(behind - canGain); behind = canGain; }
            playback.Advance(Math.Min(dt * speed, dt + behind));
        }
        float catchUpSpeed = 1.15f;

        // a shot (or a lost turn) has finished here
        void OnlineShotDone()
        {
            if (!IsOnline) return;
            OnlineSend(OnlineMsg.PlaybackDone);
            doneAt = Time.unscaledTime;
        }

        // the next turn waits for the other device to finish the shot too, but not for long, nor for one that is gone
        bool OnlineNextTurnReady() =>
            !IsOnline || oppDone.Contains(TurnSeq) || !OnlineLobby.OpponentPresent || Time.unscaledTime - doneAt > NextTurnWait;

        // the turn's deadline in network time (the session's clock, the same on both devices and not the phone's): the
        // shooter's device sets it and tells the other, so both fuses end together; time in the background counts
        double deadline;
        static double Now => OnlineLobby.NetworkTime;

        // the fuse from the deadline: the toast's wait first, then the shot time
        double ClockFromDeadline()
        {
            double left = deadline - Now;
            clockWait = (float)Math.Max(0, left - ShotTime);
            clock = (float)Math.Max(0, Math.Min(ShotTime, left));
            return left;
        }

        void TickOnlineClock()
        {
            if (awaitingState) return;   // the match is where the opponent's device has it: no judging here meanwhile
            if (state == St.Remote) { TickRemoteClock(); return; }
            if (clock < 0 || !HumanAim) return;
            float before = clock;
            double left = ClockFromDeadline();
            // a third of the time left: the fuse flashes red and a warning beeps
            if (before > ShotTime / 3 && clock <= ShotTime / 3 && clock > 0) sfx.Warn();
            if (left <= 0) TimeOut();
        }

        // the opponent's turn: their clock burns here too; if it runs out and nothing came, this device calls the stone
        // lost. With their input in but no record, it runs the shot itself after a while.
        void TickRemoteClock()
        {
            if (commit.HasValue)
            {
                clock = -1;
                if (Time.unscaledTime - commitAt > CommitStuck) PlayRemoteShot(commit.Value, null);
                return;
            }
            double left = ClockFromDeadline();
            if (-left > Overdue)
            {
                OnlineSend(OnlineMsg.Void);
                ApplyTimeOut();
            }
        }

        // ---------------- messages ----------------

        void OnlineUpdate()
        {
            if (!IsOnline) return;
            TickConnect();
            // the match is over here, or the next one has been found: its messages wait for the next scene
            if (string.IsNullOrEmpty(playingMatch) || OnlineLobby.MatchId != playingMatch) { TickRematch(); return; }
            while (OnlineLobby.Inbox.Count > 0) held.Add(OnlineLobby.Inbox.Dequeue());
            for (int i = 0; i < held.Count; i++)
            {
                var msg = held[i];
                bool done = Handle(msg);
                if (done) { held.RemoveAt(i); i--; }
            }
            TickPresence();
            TickArrival();
            CheckCard();
            PublishState();
            TickResync();
            TickRematch();
        }

        /// <summary>Acts on a message; false keeps it for later (it is for a turn still to come).</summary>
        bool Handle(OnlineMessage msg)
        {
            // anything the opponent's game sends shows they have opened the match (should the arrival itself be missed)
            oppArrived = true;
            switch (msg.Kind)
            {
                case OnlineMsg.Forfeit:
                    // the opponent gave up (they name this player the winner)
                    if (state != St.Over)
                    {
                        bool won = msg.Text == OnlineLobby.MyId;
                        EndByForfeit(won ? "online.forfeitWin" : "online.forfeitLost", won ? OnlineLobby.MyId : null);
                    }
                    return true;
                case OnlineMsg.Ready:
                    oppReady.Add(msg.Seq);
                    if (msg.Seq == CardVs) { oppVsAt = msg.A; oppVsConnected = msg.B; }
                    return true;
                case OnlineMsg.PlaybackDone: oppDone.Add(msg.Seq); return true;
            }
            if (awaitingState) return false;
            // turn messages: past turns are dropped, later ones kept, and so is this turn's until it has started here
            int seq = TurnSeq;
            if (msg.Seq < seq) return true;
            if (msg.Seq > seq) return false;
            bool turnOver = state == St.Moving || state == St.Restoring || state == St.Settle || state == St.LsdShow || state == St.Over;
            switch (msg.Kind)
            {
                case OnlineMsg.Commit:
                    if (state != St.Remote) return turnOver;
                    if (!commit.HasValue) { commit = msg; commitAt = Time.unscaledTime; clock = -1; }
                    return true;
                case OnlineMsg.Shot:
                    if (state != St.Remote) return turnOver;
                    ShotRecord rec = null;
                    try { rec = ShotRecord.FromBytes(msg.Data); }
                    catch (Exception e) { Debug.LogWarning("FlickCurling: bad shot record: " + e.Message); }
                    PlayRemoteShot(msg, rec);
                    return true;
                case OnlineMsg.Deadline:
                    if (state != St.Remote) return turnOver;
                    deadline = msg.A;
                    return true;
                case OnlineMsg.Void:
                    // their time ran out (or, from the waiting side, it called this turn lost)
                    if (state == St.Remote || state == St.Aim) { ApplyTimeOut(); return true; }
                    return turnOver;
            }
            return true;
        }

        // ---------------- back from the background ----------------

        // a short spell in the background (still on the match's network: longer, and the match is lost, TickPresence):
        // what happened meanwhile is not played through, the match is taken at once as the opponent's device has it
        // (their card), under a dim, with any shot of theirs under way shown where it is on their screen
        void OnApplicationPause(bool paused)
        {
            if (paused || mode != Mode.Online || state == St.Over || awaitingState || connecting || !OnlineLobby.InMatch) return;
            ResetShotInput();
            awaitingState = true;
            resyncUntil = Time.unscaledTime + ResyncWait;
            hud.ClearToasts();
            hud.ShowWaiting(Loc.T("online.resuming"));
        }
        float resyncUntil = -1;
        const float ResyncWait = 2;   // for what was held up meanwhile (the opponent's card among it) to come in

        // the opponent's card ahead of this device (written later, and not what this device has): the match is taken from
        // it, again if a later one comes in before the dim goes
        double appliedStamp;

        void TickResync()
        {
            if (resyncUntil < 0) return;
            var card = OnlinePlayerState.Of(OnlineLobby.OpponentId);
            var snap = card ? card.State.Value : null;
            if (snap != null && snap.Data != null && snap.Data.Length > 0 && snap.Stamp > Math.Max(myStamp, appliedStamp) && !SameAsMine(snap))
            {
                appliedStamp = snap.Stamp;
                OnlineLobby.Trace("match state taken from the opponent's card");
                ApplyState(snap);
            }
            if (Time.unscaledTime <= resyncUntil) return;
            resyncUntil = -1;
            awaitingState = false;
            hud.HideWaiting();
        }

        bool SameAsMine(MatchSnapshot snap) =>
            myData != null && snap.Phase == published.phase && snap.ShotSeq == myShotSeq && snap.Data.AsSpan().SequenceEqual(myData);
        int myShotSeq => playback != null && playStart.HasValue ? TurnSeq : -1;

        // ---------------- this device's card ----------------

        // where the match is, written on this device's card at every steady point (a turn not yet thrown, or a card)
        (int phase, int seq, int end) published = (-1, -1, -1);
        double myStamp;
        byte[] myData;

        void PublishState()
        {
            if (awaitingState) return;
            var phase = SteadyPhase();
            if (!phase.HasValue) return;
            var key = ((int)phase.Value, TurnSeq, m.End);
            if (key == published) return;
            var data = m.Capture().ToBytes();
            double stamp = OnlinePlayerState.Publish((int)phase.Value, deadline, data);
            if (stamp < 0) return;   // no card yet: written once it is there
            published = key; myStamp = stamp; myData = data;
        }

        Phase? SteadyPhase()
        {
            if ((state == St.Aim || state == St.Remote) && !commit.HasValue) return Phase.Turn;
            if (state == St.Coin && hud.CardVisible) return m.InLsd && m.Lsd != null && m.Lsd.Dist[0] == null ? Phase.LsdStartCard : Phase.LsdResultCard;
            if (state == St.EndCard && hud.CardVisible && m.NextStep() != AfterEnd.GameOver) return Phase.EndCard;
            if (state == St.EndCard || state == St.Over) return Phase.Over;
            return null;
        }

        void ApplyState(MatchSnapshot snap)
        {
            MatchState st;
            try { st = MatchState.FromBytes(snap.Data); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: bad match state: " + e.Message); return; }
            hud.ClearToasts(); hud.HideCard();
            playback = null; playStart = null; commit = null; waitingCard = null;
            m.Restore(st);
            fades.Clear(); measure = null; lsdShow = null;
            published = (-1, -1, -1);   // (written again from here)
            switch ((Phase)snap.Phase)
            {
                case Phase.LsdStartCard:
                    state = St.Coin;
                    RenderHud();
                    LsdStartCard();
                    break;
                case Phase.Turn:
                    laneTo = null;
                    state = IsRemote(m.Cur) ? St.Remote : St.Aim;
                    aimAng = 0; ResetShotInput();
                    CamTo(AimView(zoom), 3.5f);
                    RenderHud();
                    hud.TurnToast(Loc.T("hud.turn", ("name", SideOf(m.Cur, 130))));
                    deadline = snap.Deadline;   // the turn's own deadline
                    ClockFromDeadline();
                    OnlineTurnStarted();
                    if (snap.ShotSeq == TurnSeq && snap.Shot != null && snap.Shot.Length > 0) ShotUnderWay(snap);
                    break;
                case Phase.LsdResultCard:
                    LsdResultCard(m.Hammer);
                    break;
                case Phase.EndCard:
                {
                    // the end just scored, from the score table
                    int e = m.End - 1;
                    int b = e >= 0 && e < m.Scores[0].Count ? m.Scores[0][e] : 0, w = e >= 0 && e < m.Scores[1].Count ? m.Scores[1][e] : 0;
                    var sc = new ScoreResult { Team = b > 0 ? Team.B : w > 0 ? Team.W : (Team?)null, Points = Math.Max(b, w), List = new List<Ranked>(), Ids = new List<int>() };
                    measure = new Measure { Sc = sc, PrevHammer = m.Hammer, Dur = 0 };
                    ShowEndCard();
                    break;
                }
                default:
                    // over while this device was away: the result is in already (no second report), just seen
                    reportedMatch = OnlineLobby.MatchId;
                    GameOver();
                    break;
            }
        }

        // the turn's shot is under way on the opponent's device: it goes out here too, at the point it has reached there
        void ShotUnderWay(MatchSnapshot snap)
        {
            ShotRecord rec;
            try { rec = ShotRecord.FromBytes(snap.Shot); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: bad shot record: " + e.Message); return; }
            state = St.Remote;   // (not this device's own release: nothing is sent)
            m.LaunchX = snap.ShotLane;
            Release(snap.ShotAng, snap.ShotPw);
            playback = new ShotPlayback(m, rec);
            playStart = snap.ShotStart;
            jumpPlayback = true;
        }

        // ---------------- result ----------------

        // the moment the last end is scored, before the result shows: the server settles the match on the first report
        void OnlineReportResult()
        {
            if (!IsOnline || reportedMatch == OnlineLobby.MatchId || string.IsNullOrEmpty(OnlineLobby.MatchId)) return;
            string id = OnlineLobby.MatchId;
            reportedMatch = id;
            var win = m.Winner;
            string Who(Team t) => t == OnlineLobby.MyTeam ? OnlineLobby.MyId : OnlineLobby.OpponentId;
            string winner = win.HasValue ? Who(win.Value) : "draw";
            reportTask = OnlineApi.ReportResult(id, winner, ReportScores());
        }

        // the two scores in the order the server keeps the players (by id)
        int[] ReportScores()
        {
            var players = new[] { OnlineLobby.MyId, OnlineLobby.OpponentId }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            int ScoreOf(string pid) => m.Total(pid == OnlineLobby.MyId ? OnlineLobby.MyTeam : Sheet.Other(OnlineLobby.MyTeam));
            return new[] { ScoreOf(players[0]), ScoreOf(players[1]) };
        }

        // ---------------- forfeits ----------------

        // the match has started (both opened it) and is not over: leaving it, or being away too long, forfeits it
        bool MatchUnderWay => IsOnline && state != St.Over && !connecting && !arriving && !string.IsNullOrEmpty(playingMatch) && OnlineLobby.MatchId == playingMatch;

        /// <summary>"Back to the main menu" (the settings popup): online, during a match, that gives the match up, so it
        /// asks first; otherwise straight to the menu.</summary>
        void RequestMenu()
        {
            if (connecting) return;   // the match found is being connected: it cannot be left half way
            if (!MatchUnderWay) { ToMenu(); return; }
            var under = hud.CardVisible ? cardFactory : null;   // a card that was up comes back if the player stays
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("mainmenu.online"),
                Title = Loc.T("online.forfeitTitle"),
                Body = Loc.T("online.forfeitBody"),
                Buttons =
                {
                    new CardButton(Loc.T("online.forfeitStay"), () => { if (under != null) ShowCard(under); }),
                    new CardButton(Loc.T("online.forfeitLeave"), ForfeitAndLeave)
                }
            });
        }

        // the player gives up: the opponent wins (told at once, and settled on the server), and this one goes to the menu
        void ForfeitAndLeave()
        {
            if (!MatchUnderWay) { ToMenu(); return; }
            string id = playingMatch, winner = OnlineLobby.OpponentId;
            var score = ReportScores();
            OnlineSend(OnlineMsg.Forfeit, text: winner);
            leaveSoon = true;   // a moment for that message to go out before the session is left
            reportedMatch = id;
            Settle().Forget();
            async UniTaskVoid Settle()
            {
                await OnlineApi.ReportResult(id, winner, score);
                await OnlineApi.Acknowledge(id);
            }
            ToMenu();
        }

        // ---------------- dropped from the match ----------------

        // either device dropped from the match's network has lost it: this one (the service dropped it, about 10 s into the
        // background or out of signal) loses, the other (its card gone with it) wins. Before the match has started, it is
        // simply off.
        bool oppSeen;   // the opponent's card has been here (until then its absence says nothing)

        void TickPresence()
        {
            if (connecting || state == St.Over) return;
            bool gone;
            if (!OnlineLobby.NetworkUp) gone = true;
            else if (OnlineLobby.OpponentPresent) { oppSeen = true; return; }
            else gone = oppSeen;
            if (!gone) return;
            if (arriving) { NoShow(); return; }
            if (!MatchUnderWay) return;
            bool mine = !OnlineLobby.NetworkUp;
            OnlineLobby.Trace(mine ? "this device dropped from the match: lost" : "the opponent dropped from the match: won");
            EndByForfeit(mine ? "online.forfeitLost" : "online.forfeitGone", mine ? OnlineLobby.OpponentId : OnlineLobby.MyId);
        }

        // the match ended by a forfeit: the result card (play again or the menu), told to the server (a report when
        // <paramref name="winner"/> is given, then seen) and the session left
        void EndByForfeit(string titleKey, string winner)
        {
            string id = playingMatch;
            var score = ReportScores();
            OffTheSheet();
            if (vsPanel) Destroy(vsPanel);
            reportedMatch = id;
            Settle().Forget();
            async UniTaskVoid Settle()
            {
                if (winner != null) await OnlineApi.ReportResult(id, winner, score);
                await OnlineApi.Acknowledge(id);
            }
            _ = OnlineLobby.LeaveSettledAsync();
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("over.eyebrow"),
                Title = Loc.T(titleKey),
                Buttons =
                {
                    new CardButton(rematch != null ? Elapsed() : Loc.T("over.again"), ToggleRematch) { KeepOpen = true },
                    new CardButton(Loc.T("over.menu"), ToMenu)
                }
            });
        }

        void OnlineGameOver()
        {
            OnlineReportResult();
            string id = reportedMatch;
            var report = reportTask;
            // out of the session now (play again finds a new one), and seen: told to the server after the report
            _ = OnlineLobby.LeaveAsync();
            Ack().Forget();
            async UniTaskVoid Ack()
            {
                if (report != null) await report;
                if (!string.IsNullOrEmpty(id)) await OnlineApi.Acknowledge(id);
            }
            var win = m.Winner;
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("over.eyebrow"),
                Title = win.HasValue
                    ? Loc.T("over.win", ("name", SideOf(win.Value, 110)), ("score", Versus(110)))
                    : Loc.T("over.draw", ("score", Versus(110))),
                Buttons =
                {
                    new CardButton(rematch != null ? Elapsed() : Loc.T("over.again"), ToggleRematch) { KeepOpen = true },
                    new CardButton(Loc.T("over.menu"), ToMenu)
                }
            });
        }

        // play again: look for a new opponent (the button shows the time spent); pressed again, stop looking
        void ToggleRematch()
        {
            if (OnlineLobby.Joining) return;   // a match found is no longer cancelled
            if (rematch != null)
            {
                OnlineLobby.Trace("search cancelled (result card)");
                rematch.Cancel(); rematch = null;
                hud.SetCardButton(0, Loc.T("over.again"), true);
                return;
            }
            StartRematch();
            hud.SetCardButton(0, Elapsed(), true);
        }

        void StartRematch()
        {
            OnlineLobby.Trace("search started (result card)");
            rematch = new CancellationTokenSource();
            rematchStart = Time.unscaledTime;
            FindAgain(rematch).Forget();
        }

        async UniTaskVoid FindAgain(CancellationTokenSource cts)
        {
            bool found = await OnlineLobby.FindAsync(cts.Token, retry: true);
            if (!this || rematch != cts) return;
            rematch = null;
            if (!found) { hud.SetCardButton(0, Loc.T("over.again"), true); return; }
            hud.HideCard();
            MatchLaunch.SetOnline();
            ScreenFader.LoadScene(MatchLaunch.GameScene);
        }

        void TickRematch()
        {
            if (rematch != null) hud.SetCardButton(0, Elapsed(), true);
        }

        string Elapsed()
        {
            int t = Mathf.FloorToInt(Time.unscaledTime - rematchStart);
            return $"{t / 60:00}:{t % 60:00}";
        }
    }
}
