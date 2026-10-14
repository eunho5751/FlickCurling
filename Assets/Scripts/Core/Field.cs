using System;
using System.Collections.Generic;

namespace FlickCurling.Core
{
    public struct Seg
    {
        public double X1, Y1, X2, Y2;
        public bool Dead;   // dead rail: soaks up the stone
        public Seg(double x1, double y1, double x2, double y2, bool dead = false) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; Dead = dead; }
        public bool IsBottom => Y1 == 0 && Y2 == 0;
        public bool IsSide => X1 == X2 && (X1 == 0 || X1 == Sheet.W);
    }

    public struct Post
    {
        public double X, Y, R;
        public Post(double x, double y, double r) { X = x; Y = y; R = r; }
    }

    public enum ZoneKind { Sand, Ice }

    public struct Zone
    {
        public ZoneKind Kind;
        public double X1, Y1, X2, Y2, M;
        public Zone(ZoneKind k, double x1, double y1, double x2, double y2, double m) { Kind = k; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; M = m; }
    }

    /// <summary>A built playing field: walls already split into lively and dead rail stretches.</summary>
    public sealed class Field
    {
        public string Key, Name;
        public Seg[] Walls;
        public Post[] Posts;
        public Zone[] Zones;
        public List<double[][]> Kinks;   // kinked rail outlines, for drawing
        public double LiveTop;           // side rails are lively from y = 0 up to here

        public double ZoneMult(double x, double y)
        {
            foreach (var z in Zones) if (x >= z.X1 && x <= z.X2 && y >= z.Y1 && y <= z.Y2) return z.M;
            return 1;
        }

        public bool InWindow(double y) => y >= 0 && y <= LiveTop;
    }

    public static class Maps
    {
        public static readonly string[] Keys = { "base", "bumper", "zones", "kink" };
        const double TopY = Sheet.L + 60;

        public static string NameOf(string key)
        {
            switch (key)
            {
                case "bumper": return "범퍼";
                case "zones": return "모래·빙판";
                case "kink": return "꺾인 측벽";
                default: return "기본";
            }
        }

        static Seg[] BaseWalls() => new[] { new Seg(0, 0, 0, TopY), new Seg(Sheet.W, 0, Sheet.W, TopY), new Seg(0, 0, Sheet.W, 0) };

        static readonly double[][] LeftKink =
        {
            new double[] { 0, 0 }, new double[] { 0, 84 }, new double[] { 11, 100 },
            new double[] { 11, 116 }, new double[] { 0, 132 }, new double[] { 0, TopY }
        };

        static double[][] Mirror(double[][] pts)
        {
            var o = new double[pts.Length][];
            for (int i = 0; i < pts.Length; i++) o[i] = new[] { Sheet.W - pts[i][0], pts[i][1] };
            return o;
        }

        static IEnumerable<Seg> Poly(double[][] pts)
        {
            for (int i = 1; i < pts.Length; i++) yield return new Seg(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1]);
        }

        static double[][] Slice(double[][] a, int from, int to)
        {
            var o = new double[to - from][];
            for (int i = from; i < to; i++) o[i - from] = a[i];
            return o;
        }

        public static Field Build(string key, double liveTop = 80)
        {
            var f = new Field
            {
                Key = key, Name = NameOf(key), Posts = new Post[0], Zones = new Zone[0],
                Kinks = new List<double[][]>(), LiveTop = liveTop
            };
            Seg[] walls = BaseWalls();
            switch (key)
            {
                case "bumper":
                    f.Posts = new[] { new Post(27, 108, 4), new Post(73, 108, 4), new Post(13, 160, 3.5), new Post(87, 160, 3.5) };
                    break;
                case "zones":
                    f.Zones = new[]
                    {
                        new Zone(ZoneKind.Sand, 32, 92, 68, 116, 2.6),
                        new Zone(ZoneKind.Ice, 0, 100, 16, 170, 0.35),
                        new Zone(ZoneKind.Ice, 84, 100, 100, 170, 0.35)
                    };
                    break;
                case "kink":
                    var right = Mirror(LeftKink);
                    var list = new List<Seg>(Poly(LeftKink));
                    list.AddRange(Poly(right));
                    list.Add(new Seg(0, 0, Sheet.W, 0));
                    walls = list.ToArray();
                    f.Kinks.Add(Slice(LeftKink, 1, 5));
                    f.Kinks.Add(Slice(right, 1, 5));
                    break;
            }
            var split = new List<Seg>();
            foreach (var g in walls) SplitRails(g, liveTop, split);
            f.Walls = split.ToArray();
            return f;
        }

        // Rail windows: only the side rails from the launch end up to liveTop are lively; the rest is dead rail.
        static void SplitRails(Seg g, double liveTop, List<Seg> output)
        {
            if (g.IsBottom || g.Y1 == g.Y2) { output.Add(g); return; }
            double lo = Math.Min(g.Y1, g.Y2), hi = Math.Max(g.Y1, g.Y2);
            var cuts = new List<double> { lo };
            foreach (var y in new[] { 0.0, liveTop }) if (y > lo && y < hi) cuts.Add(y);
            cuts.Add(hi);
            cuts.Sort();
            double At(double y) => g.X1 + (y - g.Y1) / (g.Y2 - g.Y1) * (g.X2 - g.X1);
            for (int i = 0; i < cuts.Count - 1; i++)
            {
                double a = cuts[i], b = cuts[i + 1];
                if (b - a < 1e-6) continue;
                double mid = (a + b) / 2;
                output.Add(new Seg(At(a), a, At(b), b, !(mid >= 0 && mid <= liveTop)));
            }
        }
    }
}
