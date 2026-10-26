using System;
using System.Collections.Generic;
using System.Linq;

namespace FlickCurling.Core
{
    public enum ItemKind : byte { None, Giant, Mini, Bomb, Magnet, Protractor }

    /// <summary>What became of a box a stone touched: its side's stone took it, it moved (that side's hands were
    /// full) or the other side's stone broke it.</summary>
    public enum BoxEvent : byte { Taken, Moved, Gone }

    /// <summary>An item box on the sheet, one side's own: what is in it is decided as it is taken.</summary>
    public sealed class ItemBox
    {
        public int Id;
        public Team Team;
        public double X, Y;
        public ItemBox Clone() => (ItemBox)MemberwiseClone();
    }

    /// <summary>
    /// Item mode (Docs/GameDesign.md): each side has its own boxes on the sheet, put at random between the min line and
    /// the house's centre: a fresh set as each end starts, and after each round (both sides having thrown) the ones
    /// taken or broken come back elsewhere. A stone of that side touching one takes an
    /// item (at random) into an empty hand, or, both hands full, the box moves elsewhere; a stone of the other side
    /// breaks it. On its turn a side may put one item on the stone it is about to throw: a giant or mini stone (for as
    /// long as it is on the sheet), a bomb that goes off the first time it hits another stone, pushing every other
    /// stone near it away (one that hit none is spent once the shot has stopped), a magnet that pulls every stone near
    /// it along while it moves, or a protractor, which only shows (on that player's screen) where the aim line goes
    /// after what it first meets.
    /// </summary>
    public sealed partial class Match
    {
        public bool Items => Field.Key == Maps.ItemKey;

        public List<ItemBox> Boxes = new List<ItemBox>();
        /// <summary>Each side's hands (ItemKind.None: empty).</summary>
        public readonly ItemKind[][] Held = { new ItemKind[Sheet.ItemSlots], new ItemKind[Sheet.ItemSlots] };
        /// <summary>The item put on the stone about to be thrown (this turn).</summary>
        public ItemKind Armed;
        int nextBoxId = 1;
        // as the shot started, for a foul (everything goes back)
        List<ItemBox> boxSnapshot = new List<ItemBox>();
        readonly ItemKind[][] heldSnapshot = { new ItemKind[Sheet.ItemSlots], new ItemKind[Sheet.ItemSlots] };

        public bool CanUseItem(Team t, int slot) =>
            Items && !InLsd && t == Cur && Armed == ItemKind.None && slot >= 0 && slot < Sheet.ItemSlots && Held[(int)t][slot] != ItemKind.None;

        /// <summary>The side to throw puts the item in its hand <paramref name="slot"/> on its stone (None: it cannot).</summary>
        public ItemKind UseItem(Team t, int slot)
        {
            if (!CanUseItem(t, slot)) return ItemKind.None;
            var k = Held[(int)t][slot];
            Held[(int)t][slot] = ItemKind.None;
            Armed = k;
            return k;
        }

        /// <summary>The radius the stone about to be thrown has (its item's).</summary>
        public double ArmedRad => Sheet.SR * SizeOf(Armed);

        static double SizeOf(ItemKind k) => k == ItemKind.Giant ? Sheet.GiantSize : k == ItemKind.Mini ? Sheet.MiniSize : 1;

        // the stone just released takes the item put on it
        void ArmStone(Stone s)
        {
            s.Size = SizeOf(Armed);
            s.Bomb = Armed == ItemKind.Bomb;
            s.Magnet = Armed == ItemKind.Magnet;
            Armed = ItemKind.None;
        }

        // as an end starts (on a cleared sheet), and after each round: each side's boxes back up to the number (the ones
        // left stay where they are)
        void TopUpBoxes()
        {
            foreach (var t in new[] { Team.B, Team.W })
                for (int n = Boxes.Count(b => b.Team == t); n < Sheet.ItemBoxes; n++)
                {
                    var p = FreeSpot(null);
                    if (!p.HasValue) break;
                    Boxes.Add(new ItemBox { Id = nextBoxId++, Team = t, X = p.Value.x, Y = p.Value.y });
                }
        }

        // a place for a box, at random in the boxes' area, clear of the stones, the other boxes and the corners where the
        // stones left are printed (null: none found): the first spot at least ItemBoxSpacing from every other box (so
        // they are spread out and still land anywhere), or, failing that, the furthest of the free ones seen
        (double x, double y)? FreeSpot(ItemBox except)
        {
            double r = Sheet.ItemBoxR;
            double lo = Math.Max(Sheet.ItemYMin, Sheet.Hog + r), hi = Math.Max(lo, Math.Min(Sheet.ItemYMax, Sheet.Back - r));
            double xlo = Math.Max(Sheet.ItemXMin, r + 1), xhi = Math.Max(xlo, Math.Min(Sheet.ItemXMax, Sheet.W - r - 1));
            (double x, double y)? best = null;
            double bestGap = -1;
            for (int i = 0; i < 300; i++)
            {
                double x = xlo + (xhi - xlo) * rng.NextDouble();
                double y = lo + (hi - lo) * rng.NextDouble();
                if (y < Sheet.Hog + Sheet.ItemKeepOutH + r && (x < Sheet.ItemKeepOutW + r || x > Sheet.W - Sheet.ItemKeepOutW - r)) continue;
                if (Stones.Any(s => s.Live && Sheet.Dist(s.X, s.Y, x, y) < s.Rad + r + 1)) continue;
                double gap = double.PositiveInfinity;
                foreach (var b in Boxes) if (b != except) gap = Math.Min(gap, Sheet.Dist(b.X, b.Y, x, y));
                if (gap < 2 * r + 1.5) continue;
                if (gap >= Sheet.ItemBoxSpacing) return (x, y);
                if (gap > bestGap) { bestGap = gap; best = (x, y); }
            }
            return best;
        }

        // after every step: stones touching boxes
        void CheckBoxes()
        {
            for (int i = Boxes.Count - 1; i >= 0; i--)
            {
                var b = Boxes[i];
                var s = Stones.FirstOrDefault(t => t.Live && Sheet.Dist(t.X, t.Y, b.X, b.Y) < t.Rad + Sheet.ItemBoxR);
                if (s == null) continue;
                double fx = b.X, fy = b.Y;
                if (s.Team != b.Team)
                {
                    Boxes.RemoveAt(i);
                    Listener?.OnBox(s, b, BoxEvent.Gone, fx, fy, ItemKind.None, -1);
                    continue;
                }
                var hands = Held[(int)s.Team];
                int slot = Array.IndexOf(hands, ItemKind.None);
                if (slot >= 0)
                {
                    var k = (ItemKind)(1 + (int)(rng.NextDouble() * Kinds));
                    hands[slot] = k;
                    Boxes.RemoveAt(i);
                    Listener?.OnBox(s, b, BoxEvent.Taken, fx, fy, k, slot);
                    continue;
                }
                var p = FreeSpot(b);
                if (!p.HasValue) { Boxes.RemoveAt(i); Listener?.OnBox(s, b, BoxEvent.Gone, fx, fy, ItemKind.None, -1); continue; }
                b.X = p.Value.x; b.Y = p.Value.y;
                Listener?.OnBox(s, b, BoxEvent.Moved, fx, fy, ItemKind.None, -1);
            }
        }

        const int Kinds = 5;   // the items a box may hold (ItemKind after None)

        // every step while a magnet stone moves: each other stone near it is towed, its speed drawn toward the magnet's
        // own plus a little toward the magnet (until they touch), harder the closer it is, so it follows the magnet along
        void PullToMagnets()
        {
            foreach (var mg in Stones)
            {
                if (!mg.Live || !mg.Magnet || !mg.IsMoving) continue;
                foreach (var o in Stones)
                {
                    if (o == mg || !o.Live) continue;
                    double dx = mg.X - o.X, dy = mg.Y - o.Y, d = Sheet.Hypot(dx, dy);
                    if (d >= Sheet.MagnetR || d < 1e-6) continue;
                    double close = Math.Min(MagnetClose, Math.Max(0, d - mg.Rad - o.Rad) * 4);
                    double tx = mg.Vx + dx / d * close, ty = mg.Vy + dy / d * close;
                    double k = Math.Min(1, Sheet.MagnetPull * (1 - d / Sheet.MagnetR) * Sheet.DT);
                    o.Vx += (tx - o.Vx) * k; o.Vy += (ty - o.Vy) * k;
                }
            }
        }
        const double MagnetClose = 30;   // the most a towed stone closes in on the magnet (speed)

        // the bomb goes off: every other stone near it is pushed straight away from it, harder the closer it is
        void Explode(Stone bomb)
        {
            bomb.Bomb = false;
            foreach (var o in Stones)
            {
                if (o == bomb || !o.Live) continue;
                double dx = o.X - bomb.X, dy = o.Y - bomb.Y, d = Sheet.Hypot(dx, dy);
                if (d >= Sheet.BombR + o.Rad) continue;
                if (d < 1e-6) { dx = 0; dy = 1; d = 1; }
                double v = Sheet.BombPush * (1 - 0.6 * Math.Min(1, d / Math.Max(1e-6, Sheet.BombR)));
                o.Vx += dx / d * v; o.Vy += dy / d * v;
            }
            Listener?.OnBomb(bomb);
        }

        // the shot's start (for a foul) and a fresh turn's hands
        void SnapshotItems()
        {
            Armed = ItemKind.None;
            boxSnapshot = Boxes.Select(b => b.Clone()).ToList();
            for (int t = 0; t < 2; t++) Array.Copy(Held[t], heldSnapshot[t], Sheet.ItemSlots);
        }

        void RestoreItems()
        {
            Boxes = boxSnapshot.Select(b => b.Clone()).ToList();
            for (int t = 0; t < 2; t++) Array.Copy(heldSnapshot[t], Held[t], Sheet.ItemSlots);
        }
    }
}
