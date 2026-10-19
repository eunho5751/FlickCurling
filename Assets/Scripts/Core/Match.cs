using System;
using System.Collections.Generic;
using System.Linq;

namespace FlickCurling.Core
{
    /// <summary>Things the presentation reacts to while a shot is moving (sounds, floating labels).</summary>
    public interface IMatchEvents
    {
        void OnStoneHit(Stone a, Stone b, double j);
        void OnPostHit(Stone s, double vn);
        void OnWallHit(Stone s, double vn, Seg g);
        /// <summary>The shot stone cushioned directly: used = a ticket was spent, otherwise it had none (foul).</summary>
        void OnTicket(Stone s, bool used);
        void OnCushion(Stone s, int n);
        void OnDeadRail(Stone s);
    }

    public sealed class ShotOutcome
    {
        public bool Foul;                                // cushion without a ticket: everything goes back, the shot stone is removed
        public List<Removal> Removed = new List<Removal>();
        public OutWhy ShotWhy = OutWhy.None;             // why the delivered stone itself was removed
        public int[] Knocked = new int[2];               // other stones knocked out, per team
    }

    public sealed class EndOutcome
    {
        public ScoreResult Score;
        public Team PrevHammer;
    }

    public enum AfterEnd { NextEnd, ExtraEnd, GameOver }

    public sealed class LsdState
    {
        public readonly Team[] Order = { Team.B, Team.W };
        public int Idx;
        public double TargetX = Sheet.TeeX, TargetY = Sheet.TeeY;   // the centre of the house
        public double?[] Dist = new double?[2];   // null = not thrown yet, +inf = no record
        public Post[] Bumpers = new Post[0];        // only on the sheet for the draw
        public Field Field;                          // the map plus the bumpers
    }

    /// <summary>
    /// Match rules: ends, shot order, bonus guard, launch lanes, cushion tickets and the no-ticket foul,
    /// judging, scoring, hammer (blank ends pass it), extra end and the last stone draw.
    /// Timing and animation are left to the caller.
    /// </summary>
    public sealed partial class Match : IPhysicsEvents
    {
        public readonly Field Field;
        public readonly PhysicsParams P;
        public readonly int TicketCount;
        Rng rng;

        public List<Stone> Stones = new List<Stone>();
        int nextId = 1;
        public int End = 1, Shot;
        public Team Hammer = Team.W, Cur = Team.B;
        public readonly List<int>[] Scores = { new List<int>(), new List<int>() };
        public readonly int[] Tickets = new int[2];
        public readonly double[] Lane = { 50, 50 };
        public double LaunchX = 50;

        // the shot in flight
        public int ShotId;
        public bool ShotHitStone, WallTouched, Foul;
        public int Cushions;
        public List<Stone> Snapshot = new List<Stone>();

        public bool InLsd;
        public LsdState Lsd;
        public IMatchEvents Listener;

        /// <param name="ticketCount">cushion tickets per team in an end; -1 = Sheet.Tickets</param>
        public Match(Field field, PhysicsParams p = null, int seed = 0, int ticketCount = -1)
        {
            Field = field;
            P = p ?? PhysicsParams.Default;
            TicketCount = ticketCount < 0 ? Sheet.Tickets : ticketCount;
            rng = new Rng((ulong)(uint)seed);
            Tickets[0] = Tickets[1] = TicketCount;
        }

        public int Total(Team t) => Scores[(int)t].Sum();
        public Team ShooterAt(int k) => k % 2 == 0 ? Sheet.Other(Hammer) : Hammer;
        public Team Shooter => ShooterAt(Shot);
        public bool ShotsDone => Shot >= Sheet.Shots;
        public bool Moving => Sim.AnyMoving(Stones);
        public Stone ShotStone => Stones.FirstOrDefault(s => s.Id == ShotId);

        public int ThrownBy(Team t)
        {
            int n = 0;
            for (int k = 0; k < Shot; k++) if (ShooterAt(k) == t) n++;
            return n;
        }

        // ---------------- match start / last stone draw ----------------

        /// <summary>Starts with the last stone draw for the first-end hammer.</summary>
        public void StartMatch()
        {
            Scores[0].Clear(); Scores[1].Clear();
            End = 1; Shot = 0; Stones.Clear();
            Tickets[0] = Tickets[1] = TicketCount;
            InLsd = true;
            // the draw is to the centre of the house, around a fresh set of bumpers for every match
            var bumpers = LsdBumpers(rng, Field.Posts);
            Lsd = new LsdState { Bumpers = bumpers, Field = Field.WithPosts(bumpers) };
            Cur = Team.B;
        }

        public void BeginLsdShot()
        {
            Stones.Clear();
            Shot = Sheet.Shots;
            LaunchX = 50;   // the draw is always thrown from the centre lane
            Cur = Lsd.Order[Lsd.Idx];
            Snapshot.Clear();
        }

        /// <summary>Records the draw just thrown. Returns the distance (+inf = no record).</summary>
        public double RecordLsd(out bool shortOfHog)
        {
            var s = ShotStone;
            bool ok = s != null && s.Live && !s.Flew && s.Y - Sheet.SR >= Sheet.Hog && s.Y - Sheet.SR <= Sheet.Back;
            double d = ok ? Sheet.Dist(s.X, s.Y, Lsd.TargetX, Lsd.TargetY) : double.PositiveInfinity;
            Lsd.Dist[(int)Cur] = d;
            shortOfHog = s != null && s.Y - Sheet.SR < Sheet.Hog;
            return d;
        }

        /// <summary>Moves to the next drawer; false when both have thrown.</summary>
        public bool NextLsd() => ++Lsd.Idx < Lsd.Order.Length;

        /// <summary>Closer draw takes the first-end hammer; equal records go to a coin toss.</summary>
        public Team ResolveLsd(out bool coin)
        {
            double dB = Lsd.Dist[0] ?? double.PositiveInfinity, dW = Lsd.Dist[1] ?? double.PositiveInfinity;
            Team win;
            coin = dB == dW;
            if (coin) win = rng.NextDouble() < 0.5 ? Team.B : Team.W;
            else win = dB < dW ? Team.B : Team.W;
            Hammer = win; Cur = Sheet.Other(win);
            Stones.Clear();
            return win;
        }

        public void FinishLsd() { InLsd = false; Lsd = null; }

        public const double LsdBumperGap = 20;

        /// <summary>
        /// The last stone draw's bumpers (all Sheet.LsdBumperR): one on the centre line just in front of the house, so the
        /// tee cannot be reached in a straight line, and Sheet.LsdBumpersPerHalf in each half (left and right) between the
        /// house's centre line and the min line, placed at random but spread out (one in each band from low to high, apart
        /// from every other post), and never inside the house.
        /// </summary>
        public static Post[] LsdBumpers(Rng rng, Post[] avoid)
        {
            double rr = Sheet.LsdBumperR;
            var list = new List<Post> { new Post(Sheet.TeeX, Sheet.TeeY - Sheet.R - rr - 6, rr) };
            double y0 = Sheet.Hog + 10, y1 = Sheet.TeeY;
            int bands = Sheet.LsdBumpersPerHalf;
            bool Clear(double x, double y, double gap)
            {
                if (Sheet.Dist(x, y, Sheet.TeeX, Sheet.TeeY) < Sheet.R + rr + 1.5) return false;   // wholly outside the house
                foreach (var p in list) if (Sheet.Dist(x, y, p.X, p.Y) < gap) return false;
                foreach (var p in avoid) if (Sheet.Dist(x, y, p.X, p.Y) < gap) return false;
                return true;
            }
            foreach (bool left in new[] { true, false })
                for (int band = 0; band < bands; band++)
                {
                    double xa = left ? 8 : Sheet.W / 2 + 8, xb = left ? Sheet.W / 2 - 8 : Sheet.W - 8;
                    double ya = y0 + (y1 - y0) * band / bands, yb = y0 + (y1 - y0) * (band + 1) / bands;
                    double x = 0, y = 0;
                    bool placed = false;
                    // the gap relaxes a little if a crowded spot leaves no room
                    for (double gap = LsdBumperGap; !placed && gap >= 8; gap -= 4)
                        for (int t = 0; t < 200 && !placed; t++)
                        {
                            x = xa + rng.NextDouble() * (xb - xa); y = ya + rng.NextDouble() * (yb - ya);
                            placed = Clear(x, y, gap);
                        }
                    if (placed) list.Add(new Post(x, y, rr));
                }
            return list.ToArray();
        }

        // ---------------- ends and shots ----------------

        public void StartEnd()
        {
            // bonus guard: the side without hammer starts with one guard in front of the house
            Stones = new List<Stone>
            {
                new Stone { Id = nextId++, Team = Sheet.Other(Hammer), X = Sheet.PlaceGuardX, Y = Sheet.PlaceGuardY, Placed = true }
            };
            Shot = 0;
            Tickets[0] = Tickets[1] = TicketCount;
            Lane[0] = Lane[1] = 50;   // every end starts on the centre lane; each side keeps its last lane within the end
        }

        public void BeginShot()
        {
            Cur = Shooter;
            LaunchX = Lane[(int)Cur];
            Snapshot = Stones.Where(s => s.Live).Select(s => s.Clone()).ToList();
        }

        public void SetLane(Team t, double lx) => Lane[(int)t] = lx;

        public Stone Release(double ang, double pw)
        {
            double v = pw * P.VMax;
            var s = new Stone { Id = nextId++, Team = Cur, X = LaunchX, Y = Sheet.LaunchY, Vx = Math.Sin(ang) * v, Vy = Math.Cos(ang) * v };
            Stones.Add(s);
            ShotId = s.Id; Cushions = 0;
            WallTouched = false; ShotHitStone = false; Foul = false;
            return s;
        }

        /// <summary>The field stones move on: during the last stone draw, the map plus the draw's bumpers.</summary>
        public Field ActiveField => InLsd && Lsd != null ? Lsd.Field : Field;

        public void Step() => Sim.Step(Stones, ActiveField, P, this);

        /// <summary>Simulates the shot to rest (headless use: tests, replays).</summary>
        public void RunToRest()
        {
            for (int i = 0; i < Sim.MaxSteps && Moving; i++) Step();
        }

        /// <summary>Judges the stopped shot. On a foul nothing is removed yet: call <see cref="ApplyRestore"/>.</summary>
        public ShotOutcome ResolveShot()
        {
            var o = new ShotOutcome();
            if (Foul) { o.Foul = true; return o; }
            o.Removed = Rules.Judge(Stones, ShotId);
            foreach (var r in o.Removed)
            {
                r.Stone.Live = false;
                if (r.Stone.Id == ShotId) o.ShotWhy = r.Why;
                else o.Knocked[(int)r.Stone.Team]++;
            }
            Stones = Stones.Where(s => s.Live).ToList();
            return o;
        }

        /// <summary>Foul: every stone goes back to where it was before the shot, and the shot stone is gone.</summary>
        public void ApplyRestore()
        {
            Stones = Snapshot.Select(s => { var c = s.Clone(); c.Vx = 0; c.Vy = 0; c.Live = true; c.Flew = false; return c; }).ToList();
        }

        public void AdvanceShot() => Shot++;

        /// <summary>
        /// The shot clock ran out: nothing is thrown. In the last stone draw that is no record; in an end the stone is
        /// lost (the caller still advances the shot, so it counts as thrown).
        /// </summary>
        public void Forfeit()
        {
            ShotId = -1;
            if (InLsd) Lsd.Dist[(int)Cur] = double.PositiveInfinity;
        }

        public EndOutcome FinishEnd()
        {
            var sc = Rules.Scoring(Stones);
            Scores[0].Add(sc.Team == Team.B ? sc.Points : 0);
            Scores[1].Add(sc.Team == Team.W ? sc.Points : 0);
            var prev = Hammer;
            // whoever did not score takes the hammer; a blank end passes it too (mixed doubles rule)
            if (sc.Team.HasValue) Hammer = Sheet.Other(sc.Team.Value);
            else Hammer = Sheet.Other(prev);
            return new EndOutcome { Score = sc, PrevHammer = prev };
        }

        public AfterEnd NextStep()
        {
            if (End < Sheet.Ends) return AfterEnd.NextEnd;
            if (End == Sheet.Ends && Total(Team.B) == Total(Team.W)) return AfterEnd.ExtraEnd;
            return AfterEnd.GameOver;
        }

        public Team? Winner
        {
            get
            {
                int b = Total(Team.B), w = Total(Team.W);
                return b == w ? (Team?)null : (b > w ? Team.B : Team.W);
            }
        }

        // ---------------- physics events: cushion tickets ----------------

        void IPhysicsEvents.Hit(Stone a, Stone b, double j)
        {
            Listener?.OnStoneHit(a, b, j);
            if (a.Id == ShotId || b.Id == ShotId) ShotHitStone = true;   // later wall touches are not cushions
        }

        void IPhysicsEvents.Post(Stone s, double vn) => Listener?.OnPostHit(s, vn);

        void IPhysicsEvents.Wall(Stone s, double vn, Seg g)
        {
            Listener?.OnWallHit(s, vn, g);
            if (s.Id != ShotId || g.IsBottom) return;
            // the dead rail beside the out area soaks the stone up: never a cushion, never a ticket
            if (g.Dead) { if (vn > 6) Listener?.OnDeadRail(s); return; }
            // only a direct cushion counts: the shot stone reaching a side wall before it has touched any stone.
            // The first one uses a ticket, or fouls without one; the turn order shot never spends tickets.
            // with unlimited tickets cushions are simply free
            if (!InLsd && !Sheet.UnlimitedTickets && !ShotHitStone && !WallTouched && !Foul)
            {
                WallTouched = true;
                if (Tickets[(int)s.Team] > 0) { Tickets[(int)s.Team]--; Listener?.OnTicket(s, true); }
                else { Foul = true; Listener?.OnTicket(s, false); }
            }
            if (vn > 6)
            {
                Cushions++;
                Listener?.OnCushion(s, Cushions);
            }
        }
    }
}
