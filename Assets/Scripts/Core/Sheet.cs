using System;

namespace FlickCurling.Core
{
    public enum Team { B = 0, W = 1 }

    /// <summary>Sheet geometry and match constants, in sheet units (width 100; y = 0 at the launch end, growing toward the house).</summary>
    public static class Sheet
    {
        public const double W = 100, L = 222, Hog = 64, Back = 200, R = 28, SR = 4, DT = 1.0 / 180;
        public const double TeeX = 50, TeeY = 172, LaunchY = 14;
        // every shot is thrown from the centre of the start line (the side lanes are gone)
        public static readonly double[] Lanes = { 50 };
        public const int Shots = 10, Per = Shots / 2, Ends = 4;
        // bonus guard: in the guard zone, just short of the house (y = 130)
        public const double PlaceGuardX = TeeX, PlaceGuardY = TeeY - R - SR - 10;
        // pulls weaker than this cancel the shot
        public const double CancelPw = 0.08;

        public static Team Other(Team t) => t == Team.B ? Team.W : Team.B;
        public static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));
        public static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
        public static double Dist(double ax, double ay, double bx, double by) => Hypot(ax - bx, ay - by);
        public static double DistTee(Stone s) => Dist(s.X, s.Y, TeeX, TeeY);

        public static double NearestLane(double x)
        {
            double best = Lanes[0];
            foreach (var l in Lanes) if (Math.Abs(l - x) < Math.Abs(best - x)) best = l;
            return best;
        }
    }

    public sealed class Stone
    {
        public int Id;
        public Team Team;
        public double X, Y, Vx, Vy;
        public bool Live = true;
        public bool Flew;    // went past the far end of the sheet
        public bool Placed;  // bonus guard

        public Stone Clone() => (Stone)MemberwiseClone();
        public bool IsMoving => Vx != 0 || Vy != 0;
    }

    public static class RandomExt
    {
        // Box-Muller, as in the prototype's gauss()
        public static double Gauss(this Random r)
        {
            double u = 0, v = 0;
            while (u == 0) u = r.NextDouble();
            while (v == 0) v = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
        }
    }
}
