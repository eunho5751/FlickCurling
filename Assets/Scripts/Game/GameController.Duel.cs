using System.Collections.Generic;
using FlickCurling.Core;
using UnityEngine;

namespace FlickCurling.Game
{
    // The turn order shot thrown together (against the AI, online, the tutorial; two players on one device still take turns):
    // both aim at the target at once. This player's stone goes the moment it is thrown; the other side's, a see-through
    // ghost that never meets it, goes once both have thrown (with this one's if they threw first, else as they throw).
    // Each draw is worked out on its own where it is thrown (Match.SimulateLsd) and its record played, online sent to the
    // other device (and kept on this device's card). With both stopped, the distances show and the closer side has the
    // hammer.
    public sealed partial class GameController
    {
        bool LsdDuel => mode != Mode.Pvp;

        Team duelMine => mode == Mode.Online ? Online.OnlineLobby.MyTeam : Team.B;   // this player's side in the draw
        LsdThrow duelMyThrow, duelOppThrow;   // each side's draw once thrown (null: not yet)
        AiDecision duelAi;             // the AI's draw, aimed ahead, thrown with this player's
        readonly List<DuelRunner> runners = new List<DuelRunner>();
        float duelRevealT = -1;         // both stopped: the distances showing for a moment (-1: not yet)
        const float DuelRevealTime = 1.6f;

        // a draw played from its record: where its stone is, step by step
        sealed class DuelRunner
        {
            public LsdThrow Throw; public bool Ghost; public double Step; public int NextEvent;
            public bool Done => Step >= Throw.Record.StepCount;
        }

        void DuelStart()
        {
            m.BeginLsdShot();
            m.Cur = duelMine;
            duelMyThrow = null; duelAi = null;   // (the other side's may be in already: kept)
            runners.Clear(); duelRevealT = -1;
            fades.Clear();
            laneTo = null;
            state = St.Aim;
            aimAng = 0; ResetShotInput();
            userMoved = false;
            CamTo(AimView(1), 3.5f);
            RenderHud();
            hud.TurnToast(Loc.T("lsd.throwNow"));
            sfx.TurnStart();
            StartClock();
            // the AI aims now, and throws with this player
            var other = Sheet.Other(duelMine);
            if (IsAI(other)) duelAi = brain.DecideLsd(ai[(int)other].Value, m.Lsd.TargetX, m.Lsd.TargetY, m.P, m.ActiveField);
        }

        // this player's draw (or none, out of time): it goes; the other side's with it if it is in already
        void DuelThrow(double ang, double pw, bool timedOut)
        {
            clock = -1; ResetShotInput();
            duelMyThrow = timedOut ? NoRecord(duelMine) : m.SimulateLsd(duelMine, ang, pw, m.LaunchX);
            if (timedOut) { sfx.Void(); hud.Toast(Loc.T("timeout.title"), Loc.T("timeout.lsd"), 1800); }
            else { Run(duelMyThrow, false); sfx.Blip(0.4f + (float)pw * 0.4f, 1800, 3, 0.05f); Haptics.Pulse(0.25f); }
            state = St.LsdDuel;   // (the draw: stones on their way, then the distances)
            OnlineDuelSend(duelMyThrow);
            if (duelAi != null)
            {
                var d = duelAi;
                duelOppThrow = m.SimulateLsd(Sheet.Other(duelMine), d.Ang, d.Pw, d.Lx);
            }
            if (duelOppThrow != null) Run(duelOppThrow, true);
            RenderHud();
        }

        // the other side's draw has come (online): it goes now if this player has thrown, else it waits unseen
        void DuelTheirs(LsdThrow t)
        {
            if (duelOppThrow != null || !m.InLsd) return;
            duelOppThrow = t;
            if (duelMyThrow != null) Run(t, true);
        }

        // a new match: no draws yet
        void DuelReset()
        {
            duelMyThrow = duelOppThrow = null; duelAi = null;
            runners.Clear(); duelRevealT = -1;
        }

        LsdThrow NoRecord(Team team) => new LsdThrow { Team = team, Record = null };

        void Run(LsdThrow t, bool ghost)
        {
            if (t.Record != null) runners.Add(new DuelRunner { Throw = t, Ghost = ghost });
        }

        void TickDuel(float dt)
        {
            if (!m.InLsd || duelMyThrow == null || state != St.LsdDuel) return;
            foreach (var r in runners)
            {
                if (r.Done) continue;
                r.Step = System.Math.Min(r.Throw.Record.StepCount, r.Step + dt / Sheet.DT);
                var ev = r.Throw.Record.Events;
                while (r.NextEvent < ev.Count && ev[r.NextEvent].Step <= r.Step)
                {
                    var e = ev[r.NextEvent++];
                    float k = r.Ghost ? 0.6f : 1;
                    if (e.Kind == ShotEventKind.WallHit) sfx.Blip(k * (float)e.Value / 90, 420, 2, 0.09f);
                    else if (e.Kind == ShotEventKind.PostHit) sfx.Blip(k * (float)e.Value / 70, 1500, 5, 0.07f);
                }
            }
            if (duelOppThrow == null || runners.Exists(r => !r.Done)) return;
            // both have thrown and stopped: the distances, a moment, then who has the hammer
            if (duelRevealT < 0)
            {
                duelRevealT = 0;
                m.SetLsd(duelMyThrow.Team, duelMyThrow.Dist, duelMyThrow.X, duelMyThrow.Y);
                m.SetLsd(duelOppThrow.Team, duelOppThrow.Dist, duelOppThrow.X, duelOppThrow.Y);
                foreach (var t in new[] { duelMyThrow, duelOppThrow })
                    if (!t.HasRecord && t.Record != null)
                        hud.Toast(Loc.T("hud.noRecord"), Loc.T(t.ShortOfHog ? "lsd.shortOfHog" : "lsd.offSheet"), 1800);
                RenderHud();
            }
            duelRevealT += dt;
            if (duelRevealT < DuelRevealTime) return;
            runners.Clear();
            var win = m.ResolveLsd(out _);
            LsdResultCard(win);
        }

        // where a runner's stone is now
        static Vector2 RunnerAt(DuelRunner r)
        {
            var rec = r.Throw.Record;
            if (rec.Tracks.Count == 0) return new Vector2((float)r.Throw.Lane, (float)Sheet.LaunchY);
            var t = rec.Tracks[0];
            double k = r.Step / rec.SampleEvery - t.FirstSample;
            int i = Mathf.Clamp((int)System.Math.Floor(k), 0, t.X.Count - 1), j = Mathf.Min(i + 1, t.X.Count - 1);
            float f = Mathf.Clamp01((float)(k - i));
            if (k < 0) { i = j = 0; f = 0; }
            float x = Mathf.Lerp((float)StoneTrack.Unpack(t.X[i]), (float)StoneTrack.Unpack(t.X[j]), f);
            float y = Mathf.Lerp((float)StoneTrack.Unpack(t.Y[i]), (float)StoneTrack.Unpack(t.Y[j]), f);
            return new Vector2(x, y);
        }

        // the draw's stones, this player's solid, the other's a ghost; once both stopped, a line from the target to each
        // and its distance
        void DrawDuel()
        {
            if (!m.InLsd || state != St.LsdDuel || duelMyThrow == null) return;
            var target = new Vector2((float)m.Lsd.TargetX, (float)m.Lsd.TargetY);
            foreach (var r in runners)
            {
                var p = RunnerAt(r);
                bool gone = r.Done && !r.Throw.HasRecord;
                if (!gone) DrawStone(p.x, p.y, r.Throw.Team, r.Ghost ? 0.5f : 1);
                if (duelRevealT >= 0 && r.Throw.HasRecord)
                {
                    float q = Mathf.Clamp01(duelRevealT / 0.5f);
                    over.Line(target, target + (p - target) * q, U(2.5f), Gold95);
                    // its distance on the stone, as the turn order shot writes it
                    if (q >= 1) hud.Label(LsdMarkText(r.Throw.Team, r.Throw.Dist), X(p.x), Y(p.y), 13,
                        Ui.Shown(r.Throw.Team) == Team.B ? Palette.Rgba(255, 255, 255, .95f) : Palette.Rgba(40, 24, 12, .95f), 0, true);
                }
            }
            // thrown, stopped, and the other side not yet: said so
            if (duelOppThrow == null && !runners.Exists(r => !r.Done))
            {
                float a = 0.65f + 0.35f * Mathf.Sin(Time.time * 4);
                hud.Label(Loc.T("lsd.waiting"), cw / 2, ch * 0.42f, 18, Palette.WithA(Palette.Text, a), 0, true, true);
            }
        }
    }
}
