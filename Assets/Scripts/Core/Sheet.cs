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

        // ---- item mode (ConfigureItems) ----
        public const int ItemSlots = 2;                                  // the items a side can hold
        public static int ItemBoxes { get; private set; } = 2;           // each side's boxes on the sheet as an end starts
        public static double ItemBoxR { get; private set; } = 3.2;       // a box's half size (it is touched as a circle)
        public static double ItemBoxSpacing { get; private set; } = 25;  // how far apart boxes are put where they can be (centre to centre)
        public static double ItemXMin { get; private set; } = 0;         // where a box's centre may be put, across the sheet
        public static double ItemXMax { get; private set; } = W;
        public static double ItemYMin { get; private set; } = Hog;       // and up it
        public static double ItemYMax { get; private set; } = TeeY;
        public static double GiantSize { get; private set; } = 1.5;      // a giant / mini stone's radius against a stone's
        public static double MiniSize { get; private set; } = 0.6;
        public static double BombR { get; private set; } = 20;           // how far a bomb reaches (from its centre; it goes off on a stone)
        public static double BombPush { get; private set; } = 80;        // the speed it gives a stone beside it (less further off)
        public static double MagnetR { get; private set; } = 20;         // how far a magnet pulls (from its centre)
        public static double MagnetPull { get; private set; } = 8;       // its grip on a stone right beside it (how fast that takes the magnet's way, per second; less further off)
        // the corners just above the min line where each side's stones left are printed: no box is put there
        public const double ItemKeepOutW = 24, ItemKeepOutH = 9;

        public static void ConfigureItems(int boxes, double boxR, double xMin, double xMax, double yMin, double yMax, double giant, double mini, double bombR, double bombPush,
                                          double magnetR = 20, double magnetPull = 8, double spacing = 25)
        {
            ItemBoxSpacing = Math.Max(0, spacing);
            MagnetR = Math.Max(0, magnetR); MagnetPull = Math.Max(0, magnetPull);
            ItemXMin = Math.Min(xMin, xMax); ItemXMax = Math.Max(xMin, xMax);
            ItemBoxes = Math.Max(0, boxes);
            ItemBoxR = Clamp(boxR, 1, 10);
            ItemYMin = Math.Min(yMin, yMax); ItemYMax = Math.Max(yMin, yMax);
            GiantSize = Clamp(giant, 0.2, 3); MiniSize = Clamp(mini, 0.2, 3);
            BombR = Math.Max(0, bombR); BombPush = Math.Max(0, bombPush);
        }

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
        public double Size = 1;   // item mode: a giant or mini stone's radius against the usual one
        public bool Bomb;         // item mode: goes off the first time it hits another stone
        public bool Magnet;       // item mode: pulls the stones near it along while it moves (this shot)

        public double Rad => Sheet.SR * Size;

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
