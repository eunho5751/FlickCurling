using System;

namespace FlickCurling.Core
{
    public enum Team { B = 0, W = 1 }

    /// <summary>Sheet geometry and match constants, in sheet units (width 100; y = 0 at the launch end, growing toward the house).</summary>
    public static class Sheet
    {
        public const double W = 100, L = 222, Hog = 64, Back = 200, R = 28, DT = 1.0 / 180;
        public const double TeeX = 50, TeeY = 172, LaunchY = 14;
        // every shot is thrown from the centre of the start line (the side lanes are gone)
        public static readonly double[] Lanes = { 50 };
        // ---- tunable rules (Configure; the game applies its GameConfig asset at start-up) ----
        public static double SR { get; private set; } = 4;       // stone radius
        public static int Per { get; private set; } = 5;         // shots per team in an end
        public static int Shots => Per * 2;
        public static int Ends { get; private set; } = 4;
        public static int Tickets { get; private set; } = 2;      // cushion tickets per team in an end
        public static bool UnlimitedTickets { get; private set; }  // cushions are always free: no ticket spent, never a foul

        // the turn order shot's target: where it may be put (its centre), from .. to across and up the sheet
        public static double LsdTargetXMin { get; private set; } = 15;
        public static double LsdTargetXMax { get; private set; } = 85;
        public static double LsdTargetYMin { get; private set; } = Hog + 20;
        public static double LsdTargetYMax { get; private set; } = Back - 10;
        public const double LsdTargetR = 6.75;   // its radius (three quarters of the house's button)

        public static void ConfigureLsdTarget(double xMin, double xMax, double yMin, double yMax)
        {
            LsdTargetXMin = Math.Min(xMin, xMax); LsdTargetXMax = Math.Max(xMin, xMax);
            LsdTargetYMin = Math.Min(yMin, yMax); LsdTargetYMax = Math.Max(yMin, yMax);
        }

        public static void Configure(int ends = 4, int shotsPerTeam = 5, double stoneRadius = 4, int tickets = 2, bool unlimitedTickets = false)
        {
            Tickets = Math.Max(0, tickets);
            UnlimitedTickets = unlimitedTickets;
            Ends = Math.Max(1, ends);
            Per = Math.Max(1, shotsPerTeam);
            SR = Clamp(stoneRadius, 1, 10);
        }

        // bonus guard: in the guard zone, just short of the house (y = 130 at the default size)
        public const double PlaceGuardX = TeeX;
        public static double PlaceGuardY => TeeY - R - SR - 10;
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
        public bool Flew;    // went off the sheet (past the far end, or back past the launch end)
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
