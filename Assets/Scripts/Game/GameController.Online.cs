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
            StartMatch(Mode.Online, OnlineLobby.Map);
        }

        void OnlineReset()
        {
            playback = null;
            myReady.Clear(); oppReady.Clear(); oppDone.Clear(); held.Clear();
            waitingCard = null; commit = null; doneAt = -100;
            stateWanted = false; reportTask = null; reportedMatch = "";
            wasHere = true; arriving = false;
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
            // here: the opponent waits for this before the match starts (and this device for theirs)
            myReady.Add(CardArrive);
            OnlineSend(OnlineMsg.Ready, CardArrive);
            arriving = true; arriveStart = Time.unscaledTime;
            ScreenFader.WhenClear(() =>
            {
                if (!this) return;
                VsPanel.Show(transform, hud.Canvas.sortingOrder + 20, VsSeconds, () => { if (this) show(); });
            });
        }
        const float VsSeconds = 2.5f;

        // ---------------- an opponent who never arrives ----------------

        const int CardArrive = 0;          // Ready key: this device has opened the match
        const float ArriveWait = 20;       // the opponent has this long to open it too
        const float ArriveGone = 3;        // or this long once they have left the session
        bool arriving;
        float arriveStart;

        void TickArrival()
        {
            if (!arriving) return;
            if (oppReady.Contains(CardArrive))
            {
                // both here: the match has started (from now on leaving it counts)
                arriving = false;
                if (!string.IsNullOrEmpty(OnlineLobby.MatchId)) _ = OnlineApi.MarkStarted(OnlineLobby.MatchId);
                return;
            }
            float t = Time.unscaledTime - arriveStart;
            if (t > ArriveWait || (!OnlineLobby.OpponentHere && t > ArriveGone)) NoShow();
        }

        // the opponent never came: the match is off (no record for anyone) and this player looks for another one
        void NoShow()
        {
            arriving = false;
            string id = OnlineLobby.MatchId;
            reportedMatch = id;   // nothing to report
            OffTheSheet();
            _ = OnlineLobby.LeaveAsync();
            if (!string.IsNullOrEmpty(id)) _ = OnlineApi.CancelMatch(id);
            StartRematch();
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
            if (IsOnline) _ = OnlineLobby.LeaveAsync();
        }

        void OnlineSend(OnlineMsg kind, int? seq = null, double a = 0, double b = 0, double c = 0, byte[] data = null) =>
            OnlineLobby.Send(new OnlineMessage { Kind = kind, Seq = seq ?? TurnSeq, A = a, B = b, C = c, Data = data });

        // ---------------- names and records ----------------

        string myName = "", oppName = "";
        OnlineApi.PlayerStats myStats, oppStats;

        string OnlineSideName(Team t)
        {
            if (t == OnlineLobby.MyTeam) return myName;
            // the opponent's connection, small beside their name, while the match is on
            return OnlineLobby.OpponentHere || state == St.Over ? oppName : $"{oppName} <size=60%><color=#a3341f>{Loc.T("online.disconnected")}</color></size>";
        }

        string[] OnlineRecords()
        {
            string Rec(OnlineApi.PlayerStats s) => s == null ? "" : Loc.T("hud.record", ("w", s.W), ("l", s.L), ("d", s.D));
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
            CheckCard();
            if (stateWanted) SendStateIfSteady();
            TickReconnect();
            TickRematch();
        }

        /// <summary>Acts on a message; false keeps it for later (it is for a turn still to come).</summary>
        bool Handle(OnlineMessage msg)
        {
            switch (msg.Kind)
            {
                case OnlineMsg.Hello: return true;
                case OnlineMsg.Ready: oppReady.Add(msg.Seq); return true;
                case OnlineMsg.PlaybackDone: oppDone.Add(msg.Seq); return true;
                case OnlineMsg.StateRequest: stateWanted = true; return true;
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

        // where the match is, sent at a steady point (a turn not yet thrown, or a card)
        void SendStateIfSteady()
        {
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
            awaitingState = false;
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
            // the scores in the order the server keeps the players (by id)
            var players = new[] { OnlineLobby.MyId, OnlineLobby.OpponentId }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            int ScoreOf(string pid) => m.Total(pid == OnlineLobby.MyId ? OnlineLobby.MyTeam : Sheet.Other(OnlineLobby.MyTeam));
            reportTask = OnlineApi.ReportResult(id, winner, new[] { ScoreOf(players[0]), ScoreOf(players[1]) });
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
            if (rematch != null)
            {
                rematch.Cancel(); rematch = null;
                hud.SetCardButton(0, Loc.T("over.again"), true);
                return;
            }
            StartRematch();
            hud.SetCardButton(0, Elapsed(), true);
        }

        void StartRematch()
        {
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
