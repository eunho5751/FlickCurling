using System;
using System.Collections.Generic;
using System.Linq;

namespace FlickCurling.Core
{
    public enum AiLevel { Easy, Normal, Hard }

    /// <summary>Everything the AI reads, copied out of the match so the search can run off the main thread.</summary>
    public sealed class AiInput
    {
        public Field Field;
        public PhysicsParams P;
        public List<Stone> Stones;
        public Team Team;
        public AiLevel Level;
        public int[] Tickets;
        public Team Hammer;
        public int Shot, End, Diff;

        public static AiInput From(Match m, Team team, AiLevel lvl) => new AiInput
        {
            Field = m.Field, P = m.P, Team = team, Level = lvl,
            Stones = m.Stones.Where(s => s.Live).Select(s => s.Clone()).ToList(),
            Tickets = (int[])m.Tickets.Clone(), Hammer = m.Hammer, Shot = m.Shot, End = m.End,
            Diff = m.Total(team) - m.Total(Sheet.Other(team))
        };
    }

    public sealed class AiDecision
    {
        public double Ang, Pw, Lx;
        /// <summary>What the shot is for, shown while the AI pulls: draw, guard, takeout, hit-stay, bank, bank-takeout, blank, lsd, or "".</summary>
        public string Intent;
    }

    /// <summary>
    /// Prototype AI: candidate shots per launch lane, each simulated and scored; normal / hard look one reply ahead;
    /// end-finishing shots are valued by win probability. Then aiming noise by level.
    /// </summary>
    public sealed class Ai
    {
        sealed class Cand
        {
            public double Ang, Pw, Lx;
            public string Tag;
            public double Score, Score2;
            public bool Cush;
        }

        sealed class Ctx
        {
            public int OppLeft;
            public bool Hammer;
            public double W;
            public double TCost;
            public bool OppBanks = true;
            public MatchCtx Match;
        }

        sealed class MatchCtx { public int Diff, EndsLeft, Extra; }

        struct Pt { public double X, Y; public Pt(double x, double y) { X = x; Y = y; } }

        public static readonly Dictionary<AiLevel, (double a, double p)> Noise = new Dictionary<AiLevel, (double, double)>
        {
            { AiLevel.Easy, (0.05, 0.07) }, { AiLevel.Normal, (0.018, 0.03) }, { AiLevel.Hard, (0.006, 0.012) }
        };
        static readonly Dictionary<AiLevel, (int n, double ang)> RandomCands = new Dictionary<AiLevel, (int, double)>
        {
            { AiLevel.Easy, (16, 0.32) }, { AiLevel.Normal, (50, 0.72) }, { AiLevel.Hard, (110, 1.0) }
        };
        static readonly Dictionary<AiLevel, double> Weight = new Dictionary<AiLevel, double>
        {
            { AiLevel.Easy, 0.3 }, { AiLevel.Normal, 0.8 }, { AiLevel.Hard, 1 }
        };
        // candidate tag -> the intent shown to the player
        static readonly Dictionary<string, string> Intent = new Dictionary<string, string>
        {
            { "draw", "draw" }, { "guard", "guard" }, { "takeout", "takeout" }, { "cut", "takeout" },
            { "hit-stay", "hit-stay" }, { "bank", "bank" }, { "bank-takeout", "bank-takeout" }, { "window", "bank" }, { "random", "" }
        };
        // Per-end outcomes for the side with hammer, measured from 205 AI-vs-AI ends (hammer scores n / is stolen from n).
        static readonly (int n, double p)[] EndH = { (1, .341), (2, .229), (3, .180), (4, .024), (5, .010) };
        static readonly (int n, double p)[] EndS = { (1, .176), (2, .039) };

        readonly Random rng;
        readonly Dictionary<int, double> wpMemo = new Dictionary<int, double>();
        Field map;
        PhysicsParams P;
        int[] tickets;

        public Ai(int seed) { rng = new Random(seed); }

        const double SR = Sheet.SR, W = Sheet.W, LY = Sheet.LaunchY;

        static double AngTo(double lx, double x, double y) => Math.Atan2(x - lx, y - LY);
        double PowFor(double d) => Math.Sqrt(2 * P.Decel * Math.Max(d, 1)) / P.VMax;
        static double JsRound(double v) => Math.Floor(v + 0.5);

        // ---------------- banks and lanes ----------------

        bool BankShot(double lx, double tx, double ty, bool left, out double ang, out double pw)
        {
            ang = pw = 0;
            double xp = left ? SR : W - SR, e = P.EWall;
            double A = tx - xp, B = xp - lx;
            double den = A - e * B;
            if (Math.Abs(den) < 1e-6) return false;
            double yh = (A * LY - e * B * ty) / den;
            if (yh <= LY + 5 || yh >= ty) return false;
            double d1 = Sheet.Hypot(xp - lx, yh - LY), d2 = Sheet.Hypot(tx - xp, ty - yh);
            double cx = (xp - lx) / d1, cy = (yh - LY) / d1;
            double k2 = (e * cx) * (e * cx) + cy * cy;   // share of speed kept by the bounce, squared
            double v0 = Math.Sqrt(2 * P.Decel * d1 + 2 * P.Decel * d2 / k2);
            ang = AngTo(lx, xp, yh); pw = v0 / P.VMax;
            return true;
        }

        bool BankPoint(double lx, double tx, double ty, bool left, out Pt p)
        {
            p = default;
            double xp = left ? SR : W - SR, eg = P.EWall;
            double A = tx - xp, B = xp - lx, den = A - eg * B;
            if (Math.Abs(den) < 1e-6) return false;
            double yh = (A * LY - eg * B * ty) / den;
            if (yh <= LY + 5 || yh >= ty) return false;
            if (!map.InWindow(yh)) return false;   // that stretch of rail is dead
            p = new Pt(xp, yh);
            return true;
        }

        // every path a stone can take to (tx, ty): straight from each launch lane, plus off each lively rail when banks are allowed
        List<Pt[]> LanesTo(double tx, double ty, bool banks)
        {
            var t = new Pt(tx, ty);
            var o = new List<Pt[]>();
            foreach (var lx in Sheet.Lanes)
            {
                var from = new Pt(lx, LY);
                o.Add(new[] { from, t });
                if (banks) foreach (var left in new[] { true, false }) if (BankPoint(lx, tx, ty, left, out var b)) o.Add(new[] { from, b, t });
            }
            return o;
        }

        static double SegDist(double px, double py, Pt a, Pt b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 != 0 ? Sheet.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / l2, 0, 1) : 0;
            return Sheet.Hypot(px - (a.X + t * dx), py - (a.Y + t * dy));
        }

        static bool LaneBlocked(Pt[] lane, List<Stone> stones, int? skipId)
        {
            foreach (var s in stones)
            {
                if (!s.Live || s.Id == skipId) continue;
                for (int i = 0; i < lane.Length - 1; i++)
                {
                    var a = lane[i]; var b = lane[i + 1];
                    if (i == lane.Length - 2)
                    {
                        // stop one stone short of the target, so stones behind it do not count
                        double dx = b.X - a.X, dy = b.Y - a.Y, l = Sheet.Hypot(dx, dy);
                        if (l == 0) l = 1;
                        double k = Math.Max(0, (l - 2 * SR) / l);
                        b = new Pt(a.X + dx * k, a.Y + dy * k);
                    }
                    if (SegDist(s.X, s.Y, a, b) < 2 * SR - 0.5) return true;
                }
            }
            return false;
        }

        double BlockedShare(double tx, double ty, List<Stone> stones, int? skipId, bool banks)
        {
            var ls = LanesTo(tx, ty, banks);
            int n = 0;
            foreach (var l in ls) if (LaneBlocked(l, stones, skipId)) n++;
            return (double)n / ls.Count;
        }

        static double? XOnLane(Pt[] lane, double y)
        {
            for (int i = 0; i < lane.Length - 1; i++)
            {
                var a = lane[i]; var b = lane[i + 1];
                if ((y - a.Y) * (y - b.Y) <= 0 && a.Y != b.Y) return a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
            }
            return null;
        }

        // ---------------- candidates ----------------

        List<Cand> Candidates(AiLevel lvl, List<Stone> stones, Team team)
        {
            var o = new List<Cand>();
            double lx = 50;
            void Add(double ang, double pw, string tag)
            {
                if (pw > 0.08 && Math.Abs(ang) < 1.4) o.Add(new Cand { Ang = ang, Pw = Math.Min(pw, 1), Tag = tag, Lx = lx });
            }
            bool canBank = tickets[(int)team] > 0;   // no ticket left: cushions would foul
            double[][] houseTargets =
            {
                new double[] { 50, 172 }, new double[] { 50, 180 }, new double[] { 50, 164 }, new double[] { 43, 172 },
                new double[] { 57, 172 }, new double[] { 50, 190 }, new double[] { 38, 180 }, new double[] { 62, 180 }
            };
            // guard spots: the usual three, plus spots that close a lane the opponent could use into the tee or into my
            // stones in the house (rail lanes only count while the opponent still has a cushion ticket)
            var spotKeys = new HashSet<string>();
            var spots = new List<(double x, double y, bool lane)>();
            foreach (var xy in new[] { (50.0, 128.0), (43.0, 126.0), (57.0, 126.0) })
            {
                spotKeys.Add(xy.Item1 + "," + xy.Item2);
                spots.Add((xy.Item1, xy.Item2, false));
            }
            if (lvl != AiLevel.Easy)
            {
                bool oppBanks = tickets[(int)Sheet.Other(team)] > 0;
                var targets = new List<(double, double)> { (Sheet.TeeX, Sheet.TeeY) };
                foreach (var s in stones) if (s.Live && s.Team == team && Sheet.DistTee(s) <= Sheet.R + SR) targets.Add((s.X, s.Y));
                foreach (var (tx, ty) in targets)
                    foreach (var lane in LanesTo(tx, ty, oppBanks))
                        foreach (var gy in new double[] { 96, 112, 128 })
                        {
                            if (gy >= ty - 2 * SR) continue;
                            var gxn = XOnLane(lane, gy);
                            if (gxn == null) continue;
                            double gx = gxn.Value;
                            if (gx < 6 || gx > W - 6 || Sheet.Dist(gx, gy, Sheet.TeeX, Sheet.TeeY) <= Sheet.R + SR) continue;
                            string key = JsRound(gx / 3) + "," + gy;   // lanes that cross the same spot share one guard
                            if (spotKeys.Add(key)) spots.Add((gx, gy, true));
                        }
            }
            var opp = stones.Where(s => s.Live && s.Team != team).ToList();
            foreach (var l in Sheet.Lanes)
            {
                lx = l;
                foreach (var t in houseTargets)
                {
                    double d = Sheet.Hypot(t[0] - lx, t[1] - LY);
                    foreach (var f in new[] { 0.97, 1, 1.03 }) Add(AngTo(lx, t[0], t[1]), PowFor(d) * f, "draw");
                }
                foreach (var (gx, gy, lane) in spots)
                {
                    double d = Sheet.Hypot(gx - lx, gy - LY);
                    foreach (var f in lane ? new[] { 0.98, 1.02 } : new[] { 1.0 }) Add(AngTo(lx, gx, gy), PowFor(d) * f, "guard");
                }
                foreach (var s in opp)
                {
                    double a = AngTo(lx, s.X, s.Y), d = Sheet.Hypot(s.X - lx, s.Y - LY);
                    Add(a, 1, "takeout"); Add(a, 0.85, "takeout"); Add(a, PowFor(d + 18), "hit-stay");
                    if (lvl != AiLevel.Easy)
                        foreach (var off in new[] { -0.012, 0.012, -0.025, 0.025 }) { Add(a + off, 1, "cut"); Add(a + off, PowFor(d + 18), "cut"); }
                }
                if (canBank && lvl != AiLevel.Easy)
                {
                    var bankTargets = houseTargets.Take(5).Select(t => (t[0], t[1])).Concat(opp.Select(s => (s.X, s.Y)));
                    foreach (var (x, y) in bankTargets)
                        foreach (var left in new[] { true, false })
                        {
                            if (!BankShot(lx, x, y, left, out var bang, out var bpw)) continue;
                            foreach (var f in new[] { 0.97, 1, 1.04 }) Add(bang, bpw * f, "bank");
                            Add(bang, 1, "bank-takeout");
                        }
                }
                // rail windows: aim at the middle of each lively stretch over a range of power
                if (canBank && lvl != AiLevel.Easy)
                {
                    foreach (var g in map.Walls)
                    {
                        if (g.Dead || g.IsBottom || g.X1 != g.X2 || (g.X1 != 0 && g.X1 != W)) continue;
                        double xp = g.X1 == 0 ? SR : W - SR, ym = (g.Y1 + g.Y2) / 2;
                        foreach (var pw in new[] { 0.55, 0.65, 0.75, 0.85, 0.95, 1 }) Add(AngTo(lx, xp, ym), pw, "window");
                    }
                }
            }
            var r = RandomCands[lvl];
            for (int i = 0; i < r.n; i++)
            {
                lx = Sheet.Lanes[i % Sheet.Lanes.Length];
                double ang = (rng.NextDouble() * 2 - 1) * r.ang;
                double pw = 0.4 + rng.NextDouble() * 0.6;
                Add(ang, pw, "random");
            }
            return o;
        }

        // ---------------- evaluation ----------------

        // chance that "me" wins: d = my lead, k = regulation ends still to play, h = I throw last next end, e = extra ends used
        double WinProb(int d, int k, bool h, int e)
        {
            d = Math.Max(-15, Math.Min(15, d));
            if (k == 0)
            {
                if (d > 0) return 1;
                if (d < 0) return 0;
                if (e >= 2) return 0.5;
                return WinProb(0, 1, h, e + 1);
            }
            int key = (((d + 15) * 8 + k) * 2 + (h ? 1 : 0)) * 4 + e;
            if (wpMemo.TryGetValue(key, out var memo)) return memo;
            double v = 0;
            foreach (var (n, p) in EndH) v += p * (h ? WinProb(d + n, k - 1, false, e) : WinProb(d - n, k - 1, true, e));
            foreach (var (n, p) in EndS) v += p * (h ? WinProb(d - n, k - 1, true, e) : WinProb(d + n, k - 1, false, e));
            wpMemo[key] = v;
            return v;
        }

        // value of a finished end for `team`, in 0..100
        double EndValue(ScoreResult sc, Team team, Ctx ctx)
        {
            var m = ctx.Match;
            int d = m.Diff;
            bool hammerNext;
            if (sc.Team == team) { d += sc.Points; hammerNext = false; }
            else if (sc.Team.HasValue) { d -= sc.Points; hammerNext = true; }
            else hammerNext = !ctx.Hammer;   // blank: the mixed-doubles rule passes the hammer
            return 100 * WinProb(d, m.EndsLeft, hammerNext, m.Extra);
        }

        double Evaluate(List<Stone> stones, Team team, int shotsLeft, Ctx ctx)
        {
            var sc = Rules.Scoring(stones);
            int bas = sc.Team == team ? sc.Points : (sc.Team.HasValue ? -sc.Points : 0);
            if (shotsLeft == 0) return ctx.Match != null ? EndValue(sc, team, ctx) : bas * 100;
            double v = bas * 12;
            foreach (var s in stones)
            {
                double d = Sheet.DistTee(s);
                if (d <= Sheet.R + SR) v += (s.Team == team ? 4 : -5) * (1 - d / (Sheet.R + SR));
            }
            int mine = stones.Count(s => s.Team == team);
            v += 0.3 * (mine - (stones.Count - mine));
            if (ctx.OppLeft > 0 && ctx.W > 0)
            {
                double threat = Math.Min(1, ctx.OppLeft / 2.0);
                // 1) cover: a counting stone of mine is worth more when the opponent has no clear lane to it
                if (sc.Team == team)
                    foreach (var id in sc.Ids)
                    {
                        var s = stones.First(t => t.Id == id);
                        v += ctx.W * 1.5 * threat * BlockedShare(s.X, s.Y, stones, s.Id, ctx.OppBanks);
                    }
                // 2) house access: a clogged house helps the side without hammer and hurts the side with it
                v += ctx.W * (ctx.Hammer ? -2 : 2.5) * threat * BlockedShare(Sheet.TeeX, Sheet.TeeY, stones, null, ctx.OppBanks);
            }
            return v;
        }

        List<Stone> PlayShot(List<Stone> bas, Cand c, Team team)
        {
            var st = bas.Select(s => s.Clone()).ToList();
            double v = c.Pw * P.VMax;
            int newId = -1 - st.Count;
            st.Add(new Stone { Id = newId, Team = team, X = c.Lx, Y = LY, Vx = Math.Sin(c.Ang) * v, Vy = Math.Cos(c.Ang) * v });
            Sim.Simulate(st, map, P);
            var gone = new HashSet<int>(Rules.Judge(st, newId).Select(r => r.Stone.Id));
            return st.Where(s => s.Live && !gone.Contains(s.Id)).ToList();
        }

        // the replies an opponent would look at: draw to the tee, and take out / tap back my stones near the house,
        // straight or off a lively rail
        List<Cand> ReplyCands(List<Stone> stones, Team myTeam)
        {
            var o = new List<Cand>();
            double lx = 50;
            bool canBank = tickets[(int)Sheet.Other(myTeam)] > 0;
            void Add(double ang, double pw) { if (pw > 0.08 && Math.Abs(ang) < 1.4) o.Add(new Cand { Ang = ang, Pw = Math.Min(pw, 1), Lx = lx }); }
            void Banks(double x, double y, double[] f)
            {
                if (!canBank) return;
                foreach (var left in new[] { true, false })
                    if (BankShot(lx, x, y, left, out var a, out var p) && BankPoint(lx, x, y, left, out _))
                        foreach (var k in f) Add(a, p * k);
            }
            foreach (var l in Sheet.Lanes)
            {
                lx = l;
                Add(AngTo(lx, Sheet.TeeX, Sheet.TeeY), PowFor(Sheet.Hypot(Sheet.TeeX - lx, Sheet.TeeY - LY)));
                Banks(Sheet.TeeX, Sheet.TeeY, new[] { 1.0 });
                foreach (var s in stones)
                {
                    if (!s.Live || s.Team != myTeam || Sheet.DistTee(s) > Sheet.R + SR + 16) continue;
                    double a = AngTo(lx, s.X, s.Y);
                    Add(a, 1); Add(a, PowFor(Sheet.Hypot(s.X - lx, s.Y - LY) + 18));
                    Banks(s.X, s.Y, new[] { 1.08, 1.35 });
                }
            }
            return o;
        }

        // two-ply: my shot, then the opponent's most damaging reply (blended with their average, since people miss)
        double LookAhead(List<Stone> bas, Cand c, Team team, int shotsLeft, Ctx ctx)
        {
            var after = PlayShot(bas, c, team);
            if (shotsLeft == 0 || ctx.OppLeft == 0) return Evaluate(after, team, shotsLeft, ctx);
            var opp = Sheet.Other(team);
            var ctx2 = new Ctx { OppLeft = ctx.OppLeft - 1, Hammer = ctx.Hammer, W = ctx.W, OppBanks = ctx.OppBanks };
            double worst = double.PositiveInfinity, sum = 0;
            int n = 0;
            foreach (var r in ReplyCands(after, team))
            {
                var st2 = PlayShot(after, r, opp);
                double val = Evaluate(st2, team, shotsLeft - 1, ctx2);
                worst = Math.Min(worst, val); sum += val; n++;
            }
            if (n == 0) return Evaluate(after, team, shotsLeft, ctx);
            return 0.75 * worst + 0.25 * (sum / n);
        }

        sealed class CushionProbe : IPhysicsEvents
        {
            public bool Touched, Cushioned;
            public void Hit(Stone a, Stone b, double j) { if (a.Id == -1 || b.Id == -1) Touched = true; }
            public void Post(Stone s, double vn) { }
            // same rule as the live game: a wall after a stone hit is not a cushion
            public void Wall(Stone s, double vn, Seg g) { if (s.Id == -1 && !Touched && !g.IsBottom) Cushioned = true; }
        }

        double EvalShot(List<Stone> bas, Cand c, Team team, int shotsLeft, Ctx ctx)
        {
            var st = bas.Select(s => s.Clone()).ToList();
            double v = c.Pw * P.VMax;
            st.Add(new Stone { Id = -1, Team = team, X = c.Lx, Y = LY, Vx = Math.Sin(c.Ang) * v, Vy = Math.Cos(c.Ang) * v });
            var ev = new CushionProbe();
            Sim.Simulate(st, map, P, ev);
            if (ev.Cushioned && tickets[(int)team] <= 0) return -1e9;   // would foul
            var gone = new HashSet<int>(Rules.Judge(st, -1).Select(r => r.Stone.Id));
            double score = Evaluate(st.Where(s => s.Live && !gone.Contains(s.Id)).ToList(), team, shotsLeft, ctx);
            c.Cush = ev.Cushioned;
            return ev.Cushioned ? score - ctx.TCost : score;   // spending a ticket has a cost
        }

        static List<Cand> SortDesc(IEnumerable<Cand> c, Func<Cand, double> key) => c.OrderByDescending(key).ToList();   // stable, like JS sort

        // ---------------- decision ----------------

        public AiDecision Decide(AiInput inp)
        {
            map = inp.Field; P = inp.P; tickets = inp.Tickets;
            var team = inp.Team; var lvl = inp.Level;
            var bas = inp.Stones;
            var cands = Candidates(lvl, bas, team);
            int shotsLeft = Sheet.Shots - inp.Shot - 1;
            Team ShooterAt(int k) => k % 2 == 0 ? Sheet.Other(inp.Hammer) : inp.Hammer;
            int oppLeft = 0, myLeft = 0;
            for (int k = inp.Shot + 1; k < Sheet.Shots; k++) { if (ShooterAt(k) != team) oppLeft++; else myLeft++; }
            // a ticket is worth keeping while I still have stones to throw, more so when it is my last one
            double tcost = myLeft > 0 ? (tickets[(int)team] <= 1 ? 3 : 1.5) : 0;
            var ctx = new Ctx
            {
                OppLeft = oppLeft, Hammer = inp.Hammer == team, W = Weight[lvl], TCost = tcost,
                OppBanks = tickets[(int)Sheet.Other(team)] > 0,
                Match = new MatchCtx { Diff = inp.Diff, EndsLeft = Math.Max(0, Sheet.Ends - inp.End), Extra = Math.Max(0, inp.End - Sheet.Ends) }
            };
            foreach (var c in cands) c.Score = EvalShot(bas, c, team, shotsLeft, ctx);
            cands = SortDesc(cands, c => c.Score);
            bool looked = false;
            if (lvl != AiLevel.Easy && shotsLeft > 0 && ctx.OppLeft > 0)
            {
                // short list: the best few overall, plus the best guards and draws so set-up shots get a fair look
                bool wide = lvl == AiLevel.Hard;
                var pick = new List<Cand>();
                var seen = new HashSet<Cand>();
                void Pick(Cand c) { if (seen.Add(c)) pick.Add(c); }
                foreach (var c in cands.Take(wide ? 12 : 6)) Pick(c);
                foreach (var tag in new[] { "guard", "draw", "bank" })
                    foreach (var c in cands.Where(c => c.Tag == tag).Take(tag == "guard" ? (wide ? 8 : 4) : (wide ? 4 : 2))) Pick(c);
                var deep = pick.Where(c => c.Score > -1e8).ToList();
                foreach (var d in deep) d.Score2 = LookAhead(bas, d, team, shotsLeft, ctx) - (d.Cush ? tcost : 0);
                cands = SortDesc(deep, c => c.Score2);
                looked = true;
            }
            if (lvl == AiLevel.Hard && !looked)
            {
                // re-test the best few with small errors and prefer the stable ones
                foreach (var c in cands.Take(6))
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        double ang = c.Ang + rng.Gauss() * 0.01;
                        double pw = Sheet.Clamp(c.Pw + rng.Gauss() * 0.02, 0.05, 1);
                        sum += EvalShot(bas, new Cand { Ang = ang, Pw = pw, Lx = c.Lx, Tag = c.Tag }, team, shotsLeft, ctx);
                    }
                    c.Score = 0.5 * c.Score + 0.5 * (sum / 4);
                }
                cands = SortDesc(cands, c => c.Score);
            }
            var best = cands.Count > 0 ? cands[0] : new Cand { Ang = 0, Pw = 0.7, Lx = 50, Tag = "" };
            string label = Intent.TryGetValue(best.Tag ?? "", out var lab) ? lab : "";
            if (shotsLeft == 0 && cands.Count > 0)
            {
                var r = PlayShot(bas, best, team);
                if (!Rules.Scoring(r).Team.HasValue) label = "blank";
            }
            double lx0 = best.Lx;
            var n = Noise[lvl];
            double fa = best.Ang + rng.Gauss() * n.a;
            double fp = Sheet.Clamp(best.Pw + rng.Gauss() * n.p, 0.1, 1);
            return new AiDecision { Ang = fa, Pw = fp, Lx = lx0, Intent = label };
        }

        /// <summary>Last stone draw: straight at the target from the centre lane, with this level's error.</summary>
        public AiDecision DecideLsd(AiLevel lvl, double tx, double ty, PhysicsParams p)
        {
            P = p;
            var n = Noise[lvl];
            double d = Sheet.Hypot(tx - 50, ty - LY);
            double ang = AngTo(50, tx, ty) + rng.Gauss() * n.a;
            double pw = Sheet.Clamp(PowFor(d) + rng.Gauss() * n.p, 0.1, 1);
            return new AiDecision { Ang = ang, Pw = pw, Lx = 50, Intent = "lsd" };
        }
    }
}
