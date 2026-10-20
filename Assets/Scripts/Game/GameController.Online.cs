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
    /// as input first and then as a record both devices play (only the shooter's device runs the physics), the cards that
    /// wait for both players, the shot clock judged by the waiting side when the shooter is gone, the result reported the
    /// moment the match ends, play again with matchmaking, and a player coming back getting the match where it is.
    /// The two devices talk through <see cref="OnlineLobby"/> (messages in its inbox).
    /// </summary>
    public sealed partial class GameController
    {
        // the cards both players press before going on (their keys in Ready messages); the end cards add the end number
        const int CardLsdStart = 1, CardLsdResult = 2, CardEnd = 10;
        // past the shared deadline: the shooter is gone / still heard but silent (their void or shot is on its way for a
        // moment at most, both devices going by the same deadline)
        const float OverdueGone = 1, OverdueStuck = 3;
        const float CommitStuck = 8;                       // input but no record this long: run the shot here
        const float NextTurnWait = 3;                      // the next turn waits this long at most for the other's playback

        /// <summary>Where a match is, as a device coming back is told (<see cref="OnlineMsg.State"/>).</summary>
        enum Phase { LsdStartCard = 1, Turn, LsdResultCard, EndCard, Over }

        ShotPlayback playback;
        readonly HashSet<int> myReady = new HashSet<int>(), oppReady = new HashSet<int>(), oppDone = new HashSet<int>();
        readonly List<OnlineMessage> held = new List<OnlineMessage>();   // for a later turn than this one
        (int key, Action go)? waitingCard;
        OnlineMessage? commit;       // the opponent's input for this turn, until its record comes
        float commitAt, doneAt = -100;
        bool stateWanted;            // the opponent came back and wants the match: send at the next steady point
        bool awaitingState;          // this device came back: waiting for where the match is
        Action firstCard;
        Task reportTask;
        string reportedMatch = "";
        bool wasHere = true, reconnecting;
        // play again: matchmaking from the result card
        CancellationTokenSource rematch;
        float rematchStart;

        bool IsOnline => mode == Mode.Online;
        bool IsRemote(Team t) => IsOnline && t != OnlineLobby.MyTeam;
        int TurnSeq => m.InLsd && m.Lsd != null ? m.Lsd.Idx : 1000 + m.End * 100 + m.Shot;

        // ---------------- start ----------------

        void StartOnline()
        {
            awaitingState = OnlineLobby.TakeResuming();
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
            stateWanted = false; reportTask = null; reportedMatch = "";
            wasHere = true; arriving = false; oppArrived = false;
            myVsAt = oppVsAt = null;   // (the opponent's may come in before this device's panel is up)
            // who is playing, kept for the whole match (the lobby forgets the opponent once the session is left)
            myName = OnlineLobby.MyName; oppName = OnlineLobby.OpponentName;
            myStats = OnlineLobby.MyStats; oppStats = OnlineLobby.OpponentStats;
        }

        void OnlineFirstCard(Action show)
        {
            if (awaitingState)
            {
                // the match goes on from where the opponent's device has it
                firstCard = show;
                OnlineSend(OnlineMsg.StateRequest);
                hud.Toast(Loc.T("online.resuming"), null, 2400);
                return;
            }
            if (!connecting) Arrived();
            ScreenFader.WhenClear(() =>
            {
                if (!this) return;
                // both panels end together: each device tells the other when its panel appeared (network time, so once
                // connected), and both end it the panel's time after the later of the two
                int tok = token;
                vsPanel = VsPanel.Show(transform, hud.Canvas.sortingOrder + 20, () => Now, VsEnd, () => connecting,
                    () => { vsUp = true; OnlineLobby.Trace("records panel shown"); if (!connecting) VsShown(); },
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

        void VsShown()
        {
            myVsAt = Now; vsShownReal = Time.unscaledTime;
            OnlineSend(OnlineMsg.Ready, CardVs, a: myVsAt.Value);
        }
        bool vsUp;

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
            if (awaitingState) return;
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
        const int CardVs = 3;   // Ready key: my records panel appeared (A: when, in network time)
        GameObject vsPanel;
        double? myVsAt, oppVsAt;
        float vsShownReal;

        // when the panel ends: after both appeared; if the opponent's never comes (gone, or something lost), from this
        // device's own after a while
        double? VsEnd()
        {
            if (!myVsAt.HasValue) return null;
            if (oppVsAt.HasValue) return Math.Max(myVsAt.Value, oppVsAt.Value) + VsSeconds;
            if (!OnlineLobby.OpponentInSession || Time.unscaledTime - vsShownReal > VsWaitMax) return myVsAt.Value + VsSeconds;
            return null;
        }

        // ---------------- an opponent who never arrives ----------------

        const int CardArrive = 0;          // Ready key: this device has opened the match
        // the opponent has this long to open the match too; still longer while their device is heard (a slow phone
        // loading the scene goes quiet for a moment, and is not gone), and not as long once they have left the session
        const float ArriveWait = 20, ArriveWaitHeard = 60, ArriveGone = 3;
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
            float t = Time.unscaledTime - arriveStart;
            if ((!OnlineLobby.OpponentInSession && t > ArriveGone) || (t > ArriveWait && !OnlineLobby.OpponentHere) || t > ArriveWaitHeard) NoShow();
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
            hud.ClearToasts(); hud.HideCard();
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

        void OnlineSend(OnlineMsg kind, int? seq = null, double a = 0, double b = 0, double c = 0, byte[] data = null, string text = null) =>
            OnlineLobby.Send(new OnlineMessage { Kind = kind, Seq = seq ?? TurnSeq, A = a, B = b, C = c, Data = data, Text = text });

        // ---------------- names and records ----------------

        string myName = "", oppName = "";
        OnlineApi.PlayerStats myStats, oppStats;

        string OnlineSideName(Team t)
        {
            return t == OnlineLobby.MyTeam ? myName : oppName;
        }

        /// <summary>Whether the side is the opponent and their device is not heard now (shown under their name while the
        /// match is on).</summary>
        bool SideDisconnected(Team t) =>
            mode == Mode.Online && t != OnlineLobby.MyTeam && state != St.Over && !connecting && !OnlineLobby.OpponentHere;

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
            if (!oppReady.Contains(key) && OnlineLobby.OpponentHere) return;
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

        // the released shot run to rest here and recorded; then both devices play the record
        void OnlineRecordShot()
        {
            var before = m.Capture();
            var rec = ShotRecord.Record(m);
            m.Restore(before);
            playback = new ShotPlayback(m, rec);
            OnlineSend(OnlineMsg.Shot, a: lastAng, b: lastPw, c: m.LaunchX, data: rec.ToBytes());
        }

        // the opponent's shot: their stone goes out as they threw it and plays along their record
        void PlayRemoteShot(OnlineMessage msg, ShotRecord rec)
        {
            m.LaunchX = msg.C;
            Release(msg.A, msg.B);
            playback = rec != null ? new ShotPlayback(m, rec) : null;
            if (rec == null)
            {
                // no record (its device went before sending it): this device runs the shot from the input instead
                var before = m.Capture();
                var own = ShotRecord.Record(m);
                m.Restore(before);
                playback = new ShotPlayback(m, own);
            }
            commit = null;
        }

        // a shot (or a lost turn) has finished here
        void OnlineShotDone()
        {
            if (!IsOnline) return;
            OnlineSend(OnlineMsg.PlaybackDone);
            doneAt = Time.unscaledTime;
        }

        // the next turn waits for the other device to finish the shot too, but not for long, nor for one that is gone
        bool OnlineNextTurnReady() =>
            !IsOnline || oppDone.Contains(TurnSeq) || !OnlineLobby.OpponentHere || Time.unscaledTime - doneAt > NextTurnWait;

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

        // the opponent's turn: their clock burns here too; if it runs out and they are gone (or silent), this device calls
        // the stone lost. With their input in but no record, it runs the shot itself after a while.
        void TickRemoteClock()
        {
            if (commit.HasValue)
            {
                clock = -1;
                if (!OnlineLobby.OpponentHere || Time.unscaledTime - commitAt > CommitStuck) PlayRemoteShot(commit.Value, null);
                return;
            }
            double left = ClockFromDeadline();
            if (-left > (OnlineLobby.OpponentHere ? OverdueStuck : OverdueGone))
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
            if (OnlineLobby.OpponentHere != wasHere)
            {
                wasHere = OnlineLobby.OpponentHere;
                RenderHud();
            }
            TickArrival();
            TickForfeit();
            CheckCard();
            if (stateWanted) SendStateIfSteady();
            // nothing came back (the opponent is away too): go on from here, with what the opponent sent meanwhile
            if (awaitingState && resyncing && Time.unscaledTime - awaitStart > ResyncWait) { awaitingState = resyncing = false; hud.ClearToasts(); }
            TickReconnect();
            TickRematch();
        }

        /// <summary>Acts on a message; false keeps it for later (it is for a turn still to come).</summary>
        bool Handle(OnlineMessage msg)
        {
            // anything the opponent's game sends shows they have opened the match (should the arrival itself be missed)
            if (msg.Kind != OnlineMsg.Hello) oppArrived = true;
            switch (msg.Kind)
            {
                case OnlineMsg.Hello: return true;   // sent on joining, before the scene: not "arrived"
                case OnlineMsg.Forfeit:
                    // the opponent gave up (they name this player the winner), or claimed the win while this one was away
                    if (state != St.Over)
                    {
                        bool won = msg.Text == OnlineLobby.MyId;
                        EndByForfeit(won ? "online.forfeitWin" : "online.forfeitLost", won ? OnlineLobby.MyId : null);
                    }
                    return true;
                case OnlineMsg.Ready:
                    oppReady.Add(msg.Seq);
                    if (msg.Seq == CardVs) oppVsAt = msg.A;
                    return true;
                case OnlineMsg.PlaybackDone: oppDone.Add(msg.Seq); return true;
                case OnlineMsg.StateRequest:
                    // both back at once: one answers (the lower player id), the other waits for it
                    if (awaitingState)
                    {
                        if (string.CompareOrdinal(OnlineLobby.MyId, OnlineLobby.OpponentId) >= 0) return true;
                        awaitingState = resyncing = false;
                    }
                    stateWanted = true;
                    return true;
                case OnlineMsg.State: if (awaitingState) ApplyState(msg); return true;
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

        // ---------------- coming back ----------------

        // back from the background in a match: what happened meanwhile is not played through here, the match is taken
        // as the opponent's device has it (their answer comes at their next steady point: a turn or a card)
        void OnApplicationPause(bool paused)
        {
            if (paused || mode != Mode.Online || state == St.Over || awaitingState || connecting || !OnlineLobby.InMatch) return;
            awaitingState = resyncing = true;
            awaitStart = Time.unscaledTime;
            CheckSettled().Forget();
            ResetShotInput();
            OnlineSend(OnlineMsg.StateRequest);
            hud.ClearToasts();
            hud.Toast(Loc.T("online.resuming"), null, 2400);
        }
        bool resyncing;
        float awaitStart;
        const float ResyncWait = 8;

        // where the match is, sent at a steady point (a turn not yet thrown, or a card)
        void SendStateIfSteady()
        {
            if (awaitingState) return;
            Phase? phase = null;
            if ((state == St.Aim || state == St.Remote) && !commit.HasValue) phase = Phase.Turn;
            else if (state == St.Coin && hud.CardVisible) phase = m.InLsd && m.Lsd != null && m.Lsd.Dist[0] == null ? Phase.LsdStartCard : Phase.LsdResultCard;
            else if (state == St.EndCard && hud.CardVisible && m.NextStep() != AfterEnd.GameOver) phase = Phase.EndCard;
            else if (state == St.EndCard || state == St.Over) phase = Phase.Over;
            if (!phase.HasValue) return;
            stateWanted = false;
            OnlineSend(OnlineMsg.State, (int)phase.Value, a: deadline, data: m.Capture().ToBytes());
        }

        void ApplyState(OnlineMessage msg)
        {
            MatchState st;
            try { st = MatchState.FromBytes(msg.Data); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: bad match state: " + e.Message); return; }
            awaitingState = resyncing = false;
            hud.ClearToasts(); hud.HideCard();
            playback = null; commit = null; waitingCard = null;
            m.Restore(st);
            fades.Clear(); measure = null; lsdShow = null;
            switch ((Phase)msg.Seq)
            {
                case Phase.LsdStartCard:
                    state = St.Coin;
                    RenderHud();
                    (firstCard ?? LsdStartCard)();
                    break;
                case Phase.Turn:
                    laneTo = null;
                    state = IsRemote(m.Cur) ? St.Remote : St.Aim;
                    aimAng = 0; ResetShotInput();
                    CamTo(AimView(zoom), 3.5f);
                    RenderHud();
                    hud.TurnToast(Loc.T("hud.turn", ("name", SideOf(m.Cur, 130))));
                    deadline = msg.A;   // the turn's own deadline
                    ClockFromDeadline();
                    OnlineTurnStarted();
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

        // this device lost the session (its connection dropped): keep trying to get back in, then ask where the match is
        void TickReconnect()
        {
            if (state != St.Over && !reconnecting && OnlineLobby.Lost) ReconnectAsync().Forget();
        }

        async UniTaskVoid ReconnectAsync()
        {
            reconnecting = true;
            hud.Toast(Loc.T("online.reconnecting"), null, 2400);
            var r = await OnlineLobby.ResumeAsync(OnlineLobby.MatchId, destroyCancellationToken);
            if (!this) return;
            if (r == OnlineLobby.Resume.Ok)
            {
                reconnecting = false;
                awaitingState = true;
                OnlineSend(OnlineMsg.StateRequest);
                CheckSettled().Forget();
                return;
            }
            // the session is gone (both out, so both lost): back to the menu; otherwise try again in a moment
            if (r == OnlineLobby.Resume.Gone) { reconnecting = false; ToMenu(); return; }
            await UniTask.Delay(3000, DelayType.Realtime, cancellationToken: destroyCancellationToken).SuppressCancellationThrow();
            reconnecting = false;
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

        // the opponent unheard for forfeitSeconds at a stretch: this player claims the win (the server's first report
        // decides it, so two claims cannot both win)
        float forfeitAfter = 30, goneSince = -1;
        bool claiming;

        void TickForfeit()
        {
            if (!MatchUnderWay || awaitingState || claiming || OnlineLobby.OpponentHere) { goneSince = -1; return; }
            if (goneSince < 0) goneSince = Time.unscaledTime;
            if (Time.unscaledTime - goneSince >= forfeitAfter) ClaimForfeit().Forget();
        }

        async UniTaskVoid ClaimForfeit()
        {
            claiming = true;
            string id = playingMatch;
            var info = await OnlineApi.ReportResult(id, OnlineLobby.MyId, ReportScores());
            claiming = false;
            if (!this || !MatchUnderWay || playingMatch != id) return;
            // the server not reached (this device's own connection may be the one down): try again shortly
            if (info == null || !info.Exists) { goneSince = Time.unscaledTime - forfeitAfter + 5; return; }
            bool won = info.Winner == OnlineLobby.MyId;
            if (won) OnlineSend(OnlineMsg.Forfeit, text: OnlineLobby.MyId);   // should they come back, they see it
            EndByForfeit(won ? "online.forfeitGone" : "online.forfeitLost", null);
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

        // back after being away: if the match was settled meanwhile (the opponent claimed the win), show that
        async UniTaskVoid CheckSettled()
        {
            string id = playingMatch;
            var info = await OnlineApi.GetMatch(id);
            if (!this || info == null || !info.Exists || info.Status != "finished" || playingMatch != id || state == St.Over) return;
            EndByForfeit(info.Winner == OnlineLobby.MyId ? "online.forfeitWin" : "online.forfeitLost", null);
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
