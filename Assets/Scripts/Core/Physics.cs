using System;
using System.Collections.Generic;

namespace FlickCurling.Core
{
    public sealed class PhysicsParams
    {
        // EStone, EWall: how much of the speed into another stone / a lively wall comes back (1: all of it, a wall then
        // a mirror bounce; 0: none). The game sets them from GameConfig (Configure)
        public double Decel = 60, EStone = DefaultEStone, EWall = DefaultEWall, VMax = 197;
        public const double DefaultEStone = 0.9, DefaultEWall = 0.75;
        public const double DeadE = 0.15, DeadSlide = 0.45;
        public static readonly PhysicsParams Default = new PhysicsParams();

        /// <summary>The bounces every match plays with (Default's).</summary>
        public static void Configure(double stoneBounce = DefaultEStone, double wallBounce = DefaultEWall)
        {
            Default.EStone = Sheet.Clamp(stoneBounce, 0, 1);
            Default.EWall = Sheet.Clamp(wallBounce, 0, 1);
        }
    }

    public interface IPhysicsEvents
    {
        void Hit(Stone a, Stone b, double j);
        void Post(Stone s, double vn);
        void Wall(Stone s, double vn, Seg g);
    }

    /// <summary>Deterministic fixed-step simulation (prototype step / hitSeg / hitPost / hitPair).</summary>
    public static class Sim
    {
        public const int MaxSteps = 180 * 25;

        static void HitSeg(Stone s, in Seg g, PhysicsParams p, IPhysicsEvents ev)
        {
            double dx = g.X2 - g.X1, dy = g.Y2 - g.Y1;
            double t = Sheet.Clamp(((s.X - g.X1) * dx + (s.Y - g.Y1) * dy) / (dx * dx + dy * dy), 0, 1);
            double qx = g.X1 + t * dx, qy = g.Y1 + t * dy;
            double nx = s.X - qx, ny = s.Y - qy;
            double d = Sheet.Hypot(nx, ny);
            if (d >= s.Rad || d == 0) return;
            nx /= d; ny /= d;
            s.X = qx + nx * s.Rad; s.Y = qy + ny * s.Rad;
            double vn = s.Vx * nx + s.Vy * ny;
            if (vn < 0)
            {
                double e = g.Dead ? PhysicsParams.DeadE : p.EWall;
                // only the part heading into the wall loses energy, so the rebound lies flatter
                s.Vx -= (1 + e) * vn * nx; s.Vy -= (1 + e) * vn * ny;
                // a dead rail also eats the slide along the wall
                if (!g.IsBottom && g.Dead)
                {
                    double k = PhysicsParams.DeadSlide, tx = -ny, ty = nx, vt = s.Vx * tx + s.Vy * ty;
                    s.Vx += (k - 1) * vt * tx; s.Vy += (k - 1) * vt * ty;
                }
                ev?.Wall(s, -vn, g);
            }
        }

        static void HitPost(Stone s, in Post p, PhysicsParams pp, IPhysicsEvents ev)
        {
            double nx = s.X - p.X, ny = s.Y - p.Y;
            double d = Sheet.Hypot(nx, ny), min = s.Rad + p.R;
            if (d >= min || d == 0) return;
            nx /= d; ny /= d;
            s.X = p.X + nx * min; s.Y = p.Y + ny * min;
            double vn = s.Vx * nx + s.Vy * ny;
            if (vn < 0)
            {
                s.Vx -= (1 + pp.EStone) * vn * nx; s.Vy -= (1 + pp.EStone) * vn * ny;
                ev?.Post(s, -vn);
            }
        }

        static void HitPair(Stone a, Stone b, PhysicsParams p, IPhysicsEvents ev)
        {
            double nx = b.X - a.X, ny = b.Y - a.Y;
            double d2 = nx * nx + ny * ny, min = a.Rad + b.Rad;
            if (d2 >= min * min) return;
            double d = Math.Sqrt(d2);
            if (d == 0) d = 1e-6;
            nx /= d; ny /= d;
            double ov = (min - d) / 2;
            a.X -= nx * ov; a.Y -= ny * ov; b.X += nx * ov; b.Y += ny * ov;
            double vn = (b.Vx - a.Vx) * nx + (b.Vy - a.Vy) * ny;
            if (vn < 0)
            {
                double j = -(1 + p.EStone) * vn / 2;
                a.Vx -= j * nx; a.Vy -= j * ny; b.Vx += j * nx; b.Vy += j * ny;
                ev?.Hit(a, b, j);
            }
        }

        public static void Step(List<Stone> stones, Field map, PhysicsParams p, IPhysicsEvents ev = null)
        {
            foreach (var s in stones)
            {
                if (!s.Live) continue;
                double sp = Sheet.Hypot(s.Vx, s.Vy);
                if (sp == 0) continue;
                double ns = sp - p.Decel * map.ZoneMult(s.X, s.Y) * Sheet.DT;
                if (ns <= 0) { s.Vx = 0; s.Vy = 0; continue; }
                double f = ns / sp;
                s.Vx *= f; s.Vy *= f;
                s.X += s.Vx * Sheet.DT; s.Y += s.Vy * Sheet.DT;
            }
            foreach (var s in stones)
            {
                if (!s.Live) continue;
                for (int i = 0; i < map.Walls.Length; i++) HitSeg(s, map.Walls[i], p, ev);
                for (int i = 0; i < map.Posts.Length; i++) HitPost(s, map.Posts[i], p, ev);
            }
            for (int i = 0; i < stones.Count; i++)
            {
                var a = stones[i];
                if (!a.Live) continue;
                for (int j = i + 1; j < stones.Count; j++) if (stones[j].Live) HitPair(a, stones[j], p, ev);
            }
            foreach (var s in stones)
            {
                if (!s.Live) continue;
                // off the far end, or wholly back past the launch end (there is no wall there)
                if (s.Y - s.Rad > Sheet.L + 1 || s.Y + s.Rad < 0) { s.Live = false; s.Flew = true; s.Vx = 0; s.Vy = 0; }
            }
        }

        public static bool AnyMoving(List<Stone> stones)
        {
            foreach (var s in stones) if (s.Live && s.IsMoving) return true;
            return false;
        }

        public static void Simulate(List<Stone> stones, Field map, PhysicsParams p, IPhysicsEvents ev = null)
        {
            for (int i = 0; i < MaxSteps && AnyMoving(stones); i++) Step(stones, map, p, ev);
        }
    }
}
