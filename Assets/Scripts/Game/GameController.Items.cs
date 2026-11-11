using System.Collections.Generic;
using System.Linq;
using FlickCurling.Core;
using UnityEngine;

namespace FlickCurling.Game
{
    // Item mode (Core/Items.cs): the boxes on the sheet, each side's two hands under the min line (its own in its half,
    // pressed on its turn to put the item on the stone it is about to throw), and what goes with them: a box taken
    // sparkles and is called in a toast both players see, one moved puffs away and pops up elsewhere, one broken
    // cracks, a bomb booms. The art is Codex's (Resources/Items).
    public sealed partial class GameController
    {
        Sprite boxB, boxW, iconGiant, iconMini, iconBomb, iconMagnet, iconProtractor;

        void LoadItemArt()
        {
            boxB = Resources.Load<Sprite>("Items/box_b"); boxW = Resources.Load<Sprite>("Items/box_w");
            iconGiant = Resources.Load<Sprite>("Items/giant"); iconMini = Resources.Load<Sprite>("Items/mini"); iconBomb = Resources.Load<Sprite>("Items/bomb");
            iconMagnet = Resources.Load<Sprite>("Items/magnet"); iconProtractor = Resources.Load<Sprite>("Items/protractor");
        }

        Sprite ItemIcon(ItemKind k) => k switch
        {
            ItemKind.Giant => iconGiant, ItemKind.Mini => iconMini, ItemKind.Bomb => iconBomb,
            ItemKind.Magnet => iconMagnet, ItemKind.Protractor => iconProtractor, _ => null
        };
        string ItemName(ItemKind k) => LT(k switch
        {
            ItemKind.Giant => "item.giant", ItemKind.Mini => "item.mini", ItemKind.Bomb => "item.bomb",
            ItemKind.Magnet => "item.magnet", _ => "item.protractor"
        });

        // ---------------- the hands ----------------

        // each side's two hands, in sheet units: in its half under the min line, side by side
        const float SlotR = 4.4f, SlotGap = 11f, SlotY = 54f;
        Vector2 SlotAt(int side, int i) => new Vector2((side == 0 ? W / 4 : W * 3 / 4) + (i - 0.5f) * SlotGap, SlotY);
        static Team SideTeam(int side) => side == 0 ? Left : Right;
        readonly float[,] slotPop = new float[2, Sheet.ItemSlots];   // when a hand was last filled (it pops in)

        /// <summary>A press on a hand: on this player's turn, one of theirs holding an item puts it on the stone (and the
        /// press is not an aim). Anywhere else, an empty hand or the other side's, the press aims and pulls as usual.</summary>
        bool PressSlot(Vector2 p)
        {
            if (m == null || !m.Items || m.InLsd || state == St.Menu) return false;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < Sheet.ItemSlots; i++)
                {
                    var c = SlotAt(side, i);
                    if (Vector2.Distance(p, new Vector2(X(c.x), Y(c.y))) > SlotR * s + 6) continue;
                    var team = SideTeam(side);
                    if (!HumanAim || team != m.Cur || m.Held[(int)team][i] == ItemKind.None) return false;
                    if (!Charging) UseItem(team, i);
                    return true;
                }
            return false;
        }

        // the item goes on the stone about to be thrown (online, the other device is told)
        void UseItem(Team team, int slot)
        {
            var k = m.UseItem(team, slot);
            if (k == ItemKind.None) return;
            ItemUsed(team, k);
            if (IsOnline) OnlineSend(Online.OnlineMsg.UseItem, a: slot);
        }

        void ItemUsed(Team team, ItemKind k)
        {
            sfx.ItemUse();
            Haptics.Pulse(0.3f);
            itemFx.Add(new ItemFx { Kind = FxSparkle, X = (float)m.LaunchX, Y = LY, Dur = 0.6f });
            floats.Add(new Float { X = (float)m.LaunchX, Y = LY + 4, Text = ItemName(k) });
            RenderHud();
        }

        // the AI's turn: an item on its stone first, if it wants one
        void AiItem(Team team)
        {
            int slot = brain.ChooseItem(m, team, ai[(int)team].Value);
            if (slot < 0) return;
            var k = m.UseItem(team, slot);
            if (k != ItemKind.None) ItemUsed(team, k);
        }

        // ---------------- what happens on the sheet ----------------

        const int FxSparkle = 0, FxPoof = 1, FxCrack = 2, FxBoom = 3, FxSpark = 4;
        sealed class ItemFx { public int Kind; public float X, Y, T, Dur, Ang, K; }   // (Ang, K: a spark's direction and strength)
        readonly List<ItemFx> itemFx = new List<ItemFx>();
        readonly Dictionary<int, float> boxBorn = new Dictionary<int, float>();   // when a box (by id) appeared where it is

        void OnItemBox(Stone s, ItemBox b, BoxEvent what, double fromX, double fromY, ItemKind kind, int slot)
        {
            float fx = (float)fromX, fy = (float)fromY;
            switch (what)
            {
                case BoxEvent.Taken:
                    itemFx.Add(new ItemFx { Kind = FxSparkle, X = fx, Y = fy, Dur = 0.7f });
                    sfx.ItemGet();
                    Haptics.Pulse(0.35f);
                    if (slot >= 0 && slot < Sheet.ItemSlots) slotPop[s.Team == Left ? 0 : 1, slot] = Time.time;
                    hud.Toast(Loc.T("item.got", ("team", Ui.Icon(s.Team)), ("item", ItemName(kind))), null, 2200, 17, ItemIcon(kind));
                    break;
                case BoxEvent.Moved:
                    itemFx.Add(new ItemFx { Kind = FxPoof, X = fx, Y = fy, Dur = 0.5f });
                    boxBorn[b.Id] = Time.time;
                    sfx.BoxMove();
                    break;
                default:
                    itemFx.Add(new ItemFx { Kind = FxCrack, X = fx, Y = fy, Dur = 0.5f });
                    sfx.BoxBreak();
                    break;
            }
        }

        // two stones meeting (strength 0..1, as the hit's sound): a small spark at the point they touch; the softest
        // touches none
        void Spark(Stone a, Stone b, float k)
        {
            if (k < SparkMin) return;
            float t = (float)(a.Rad / (a.Rad + b.Rad));
            itemFx.Add(new ItemFx
            {
                Kind = FxSpark, Dur = 0.22f + 0.1f * Mathf.Clamp01(k),
                X = (float)(a.X + (b.X - a.X) * t), Y = (float)(a.Y + (b.Y - a.Y) * t),
                Ang = (float)fx.NextDouble() * Mathf.PI * 2, K = Mathf.Clamp01(k),
            });
        }
        const float SparkMin = 0.08f;

        void OnItemBomb(Stone s)
        {
            itemFx.Add(new ItemFx { Kind = FxBoom, X = (float)s.X, Y = (float)s.Y, Dur = 0.7f });
            sfx.Boom();
            Haptics.Pulse(1);
        }

        // ---------------- drawing ----------------

        // the boxes (popping in where they have just appeared), a little see-through, under the stones; while they wait
        // each breathes a little and now and then a glint runs over it (each to its own beat)
        const float BoxAlpha = 0.8f;

        void DrawBoxes()
        {
            if (!m.Items || m.InLsd) return;
            float now = Time.time;
            bool fresh = false;
            foreach (var b in m.Boxes)
            {
                if (!boxBorn.TryGetValue(b.Id, out float born)) { boxBorn[b.Id] = born = now; fresh = true; }
                float k = Mathf.Clamp01((now - born) / 0.35f), pop = k < 1 ? Mathf.Sin(k * Mathf.PI * 0.5f) * (1 + 0.25f * Mathf.Sin(k * Mathf.PI)) : 1;
                float phase = b.Id * 1.7f, beat = (Mathf.Sin(now * 2.6f + phase) + 1) / 2;
                float size = (float)(2 * Sheet.ItemBoxR) * pop * (1 + 0.06f * beat);
                var c = new Vector2((float)b.X, (float)b.Y);
                var sp = Ui.Shown(b.Team) == Team.B ? boxB : boxW;
                if (sp) DrawItemSprite(sp, c.x, c.y, size, 6, BoxAlpha);
                // the glint: a little four-pointed star at the top right, once every few seconds
                float g = Mathf.Repeat(now * 0.45f + b.Id * 0.37f, 1);
                if (g < 0.18f)
                {
                    float a = Mathf.Sin(g / 0.18f * Mathf.PI), arm = size * 0.32f * a;
                    var p = c + new Vector2(size * 0.24f, size * 0.24f);
                    over.Line(p - new Vector2(arm, 0), p + new Vector2(arm, 0), U(1.6f), Palette.Rgba(255, 250, 225, a));
                    over.Line(p - new Vector2(0, arm), p + new Vector2(0, arm), U(1.6f), Palette.Rgba(255, 250, 225, a));
                }
            }
            if (fresh) sfx.Pop();   // (new ones popping in: as an end starts, after a round)
        }

        // both sides' hands in their halves under the min line; this player's light up on their turn
        void DrawSlots(float time)
        {
            if (!m.Items || m.InLsd || state == St.Menu) return;
            float a = Charging || state == St.AiPull ? 0.3f : 1;
            float pulse = (Mathf.Sin(time / 260) + 1) / 2;
            for (int side = 0; side < 2; side++)
            {
                var team = SideTeam(side);
                var hands = m.Held[(int)team];
                bool usable = HumanAim && team == m.Cur && m.Armed == ItemKind.None;
                for (int i = 0; i < Sheet.ItemSlots; i++)
                {
                    var c = SlotAt(side, i);
                    var k = hands[i];
                    under.Disc(c, SlotR, Palette.Rgba(52, 32, 16, .22f * a));
                    under.Ring(c, SlotR, U(1.6f), Palette.Rgba(184, 140, 72, .75f * a));
                    if (k == ItemKind.None) continue;
                    float since = Time.time - slotPop[side, i], pop = since < 0.35f ? 1 + 0.3f * Mathf.Sin(since / 0.35f * Mathf.PI) : 1;
                    var icon = ItemIcon(k);
                    if (icon) DrawItemSprite(icon, c.x, c.y, SlotR * 1.75f * pop, 6, a);
                    if (usable) aim.Ring(c, SlotR + 0.9f + pulse * 0.7f, U(2), Palette.Rgba(255, 214, 120, 0.4f + 0.5f * pulse));
                }
            }
        }

        // a magnet stone shows its magnet; while it moves, a faint ring as far as it pulls
        void DrawMagnetMark(float x, float y, float rad, bool moving, float time)
        {
            if (iconMagnet) DrawItemSprite(iconMagnet, x, y, rad * 1.3f, 11);
            float beat = (Mathf.Sin(time / 90) + 1) / 2;
            var c = new Vector2(x, y);
            over.Ring(c, rad + 0.8f + beat * 0.5f, U(2), Palette.Rgba(90, 150, 230, 0.5f + 0.4f * beat));
            if (moving) over.Ring(c, (float)Sheet.MagnetR, U(1.5f), Palette.Rgba(120, 175, 240, 0.25f + 0.2f * beat));
        }

        // ---------------- the protractor: where the aim line goes after what it first meets ----------------

        // on this player's screen only, with the protractor on the stone: off a wall or post, the bounce; off a stone,
        // where that stone goes and where this one glances off. The directions are the physics' own (Sim: the part of
        // the speed into what it meets comes back as much as that bounces, the rest goes on), so they hold at any power
        void DrawProtractor(double ang)
        {
            if (m.Armed != ItemKind.Protractor) return;
            // while the power is pulled: the shot itself, run on a copy of the sheet up to its first contact (exact at
            // that power: a stone hit lands a little differently at different speeds)
            if (Charging && power > 0 && ProtractorBySim(ang, power)) return;
            var dir = new Vector2((float)System.Math.Sin(ang), (float)System.Math.Cos(ang));
            var hit = AimRay(ang);   // (the first thing it really meets, whatever the hit marker setting)
            if (hit.kind == HitKind.None) return;
            float rad = (float)m.ArmedRad;
            var p = hit.p;
            switch (hit.kind)
            {
                case HitKind.Stone:
                {
                    Stone st = null; float best = float.MaxValue;
                    foreach (var t in m.Stones)
                    {
                        if (!t.Live) continue;
                        float d = Vector2.Distance(p, new Vector2((float)t.X, (float)t.Y)) - (float)t.Rad;
                        if (d < best) { best = d; st = t; }
                    }
                    if (st == null) return;
                    var c = new Vector2((float)st.X, (float)st.Y);
                    var n = (c - p).normalized;
                    ProtractorLine(c, n, (float)st.Rad, st, HitPathCol);   // the stone hit, pushed along the line of centres
                    float e = (float)m.P.EStone;
                    var glance = dir - (1 + e) / 2 * Vector2.Dot(dir, n) * n;   // this one, what is left of its way
                    if (glance.magnitude > 0.08f) ProtractorLine(p, glance.normalized, rad, st);
                    break;
                }
                case HitKind.Post:
                {
                    Post best = default; float bd = float.MaxValue;
                    foreach (var q in m.ActiveField.Posts)
                    {
                        float d = Vector2.Distance(p, new Vector2((float)q.X, (float)q.Y));
                        if (d < bd) { bd = d; best = q; }
                    }
                    var n = (p - new Vector2((float)best.X, (float)best.Y)).normalized;
                    ProtractorLine(p, (dir - (1 + (float)m.P.EStone) * Vector2.Dot(dir, n) * n).normalized, rad, null);
                    break;
                }
                default:
                {
                    Vector2 near = p; float bd = float.MaxValue; Seg wall = default;
                    foreach (var w in m.ActiveField.Walls)
                    {
                        var a = new Vector2((float)w.X1, (float)w.Y1); var b = new Vector2((float)w.X2, (float)w.Y2);
                        var ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                        var q = a + ab * t;
                        float d = Vector2.Distance(p, q);
                        if (d < bd) { bd = d; near = q; wall = w; }
                    }
                    var n = (p - near).normalized;
                    float e = wall.Dead ? (float)PhysicsParams.DeadE : (float)m.P.EWall;
                    var outv = dir - (1 + e) * Vector2.Dot(dir, n) * n;
                    if (wall.Dead && !wall.IsBottom)
                    {
                        // a dead rail also eats the slide along it
                        var t = new Vector2(-n.y, n.x);
                        outv += ((float)PhysicsParams.DeadSlide - 1) * Vector2.Dot(outv, t) * t;
                    }
                    if (outv.magnitude > 0.05f) ProtractorLine(p, outv.normalized, rad, null);
                    break;
                }
            }
        }

        // the shot at this power on a copy of the stones, step by step to the first thing it meets: where it is then and
        // which ways it (and a stone it hit) go. False if it meets nothing
        bool ProtractorBySim(double ang, float pw)
        {
            var stones = m.Stones.Where(t => t.Live).Select(t => t.Clone()).ToList();
            double v = pw * m.P.VMax;
            var shot = new Stone { Id = int.MinValue, Team = m.Cur, X = m.LaunchX, Y = Sheet.LaunchY, Vx = System.Math.Sin(ang) * v, Vy = System.Math.Cos(ang) * v, Size = m.ArmedRad / Sheet.SR };
            stones.Add(shot);
            for (int i = 0; i < Sim.MaxSteps; i++)
            {
                double vx = shot.Vx, vy = shot.Vy;
                Sim.Step(stones, m.ActiveField, m.P);
                if (!shot.Live || !shot.IsMoving) return false;
                var hit = stones.FirstOrDefault(t => t != shot && t.IsMoving);
                bool turned = System.Math.Abs(vx * shot.Vy - vy * shot.Vx) > 1e-3 * (vx * vx + vy * vy);
                if (hit == null && !turned) continue;
                var at = new Vector2((float)shot.X, (float)shot.Y);
                var sd = new Vector2((float)shot.Vx, (float)shot.Vy);
                Stone real = hit != null ? m.Stones.FirstOrDefault(t => t.Id == hit.Id) : null;
                if (sd.magnitude > 1e-3f) ProtractorLine(at, sd.normalized, (float)shot.Rad, real);
                if (hit != null && real != null)
                    ProtractorLine(new Vector2((float)real.X, (float)real.Y), new Vector2((float)hit.Vx, (float)hit.Vy).normalized, (float)real.Rad, real, HitPathCol);
                return true;
            }
            return false;
        }

        // a stone of radius rad going from `from` along d: dashed on until it meets a wall, post or another stone (or a
        // good way on), with a faint circle where it would get to
        // the protractor's lines: this player's stone in gold, a stone it hits in red
        static readonly Color ShotPathCol = Palette.Rgba(255, 214, 120, .9f), HitPathCol = Palette.Rgba(232, 92, 72, .95f);

        void ProtractorLine(Vector2 from, Vector2 d, float rad, Stone ignore, Color? col = null)
        {
            var lineCol = col ?? ShotPathCol;
            // (it stops only at what it closes in on: not the wall or stone it has just come off)
            bool Meets(float dq, float dp, float reach) => dq < reach && dq < dp;
            var p = from;
            for (int i = 0; i < 200; i++)
            {
                var q = p + d * 0.35f;
                if (q.y > Sheet.L || q.y < 0 || Vector2.Distance(from, q) > 70) break;
                bool stop = false;
                foreach (var st in m.Stones)
                {
                    if (!st.Live || st == ignore) continue;
                    var c = new Vector2((float)st.X, (float)st.Y);
                    if (Meets(Vector2.Distance(q, c), Vector2.Distance(p, c), (float)st.Rad + rad)) { stop = true; break; }
                }
                foreach (var po in m.ActiveField.Posts)
                {
                    var c = new Vector2((float)po.X, (float)po.Y);
                    if (Meets(Vector2.Distance(q, c), Vector2.Distance(p, c), (float)po.R + rad)) { stop = true; break; }
                }
                foreach (var w in m.ActiveField.Walls)
                {
                    var a = new Vector2((float)w.X1, (float)w.Y1); var ab = new Vector2((float)w.X2, (float)w.Y2) - a;
                    float Dist(Vector2 x) => Vector2.Distance(x, a + ab * Mathf.Clamp01(Vector2.Dot(x - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude)));
                    if (Meets(Dist(q), Dist(p), rad)) { stop = true; break; }
                }
                if (stop) break;
                p = q;
            }
            aim.Dashed(from, p, U(1.6f), U(4), U(4), lineCol);
            aim.Ring(p, rad, U(1.5f), Palette.WithA(lineCol, .55f));
        }

        // a bomb stone shows its bomb, and a red ring that beats
        void DrawBombMark(float x, float y, float rad, float time)
        {
            if (iconBomb) DrawItemSprite(iconBomb, x, y, rad * 1.3f, 11);
            float beat = (Mathf.Sin(time / 120) + 1) / 2;
            over.Ring(new Vector2(x, y), rad + 0.8f + beat * 0.5f, U(2), Palette.Rgba(220, 70, 40, 0.5f + 0.4f * beat));
        }

        void TickItemFx(float dt)
        {
            foreach (var f in itemFx) f.T += dt / f.Dur;
            itemFx.RemoveAll(f => f.T >= 1);
        }

        void DrawItemFx()
        {
            foreach (var f in itemFx)
            {
                var c = new Vector2(f.X, f.Y);
                float t = f.T, e = 1 - (1 - t) * (1 - t), fade = 1 - t;
                switch (f.Kind)
                {
                    case FxSparkle:
                        over.Ring(c, 2 + 9 * e, U(3 * fade + 0.5f), Palette.Rgba(255, 226, 140, .9f * fade));
                        for (int i = 0; i < 8; i++)
                        {
                            float ang = i * Mathf.PI / 4 + 0.3f, r0 = 3 + 7 * e, r1 = r0 + 3 * fade;
                            var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                            over.Line(c + d * r0, c + d * r1, U(2), Palette.Rgba(255, 244, 200, fade));
                        }
                        break;
                    case FxPoof:
                        for (int i = 0; i < 6; i++)
                        {
                            float ang = i * Mathf.PI / 3;
                            over.Disc(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (2 + 5 * e), 2.2f * fade + 0.4f, Palette.Rgba(240, 228, 205, .7f * fade));
                        }
                        break;
                    case FxCrack:
                        for (int i = 0; i < 7; i++)
                        {
                            float ang = i * 2 * Mathf.PI / 7 + 0.4f;
                            var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                            over.Line(c + d * (1 + 6 * e), c + d * (2.5f + 6 * e), U(2.5f), Palette.Rgba(110, 70, 30, fade));
                        }
                        break;
                    case FxSpark:
                    {
                        // two stones meeting: a quick white flash and a few short streaks flying off it, as hard as they hit
                        over.Disc(c, (0.6f + 1.4f * f.K) * fade, Palette.Rgba(255, 252, 235, .85f * fade));
                        for (int i = 0; i < 5; i++)
                        {
                            float ang = f.Ang + i * Mathf.PI * 2 / 5 + (i % 2) * 0.35f;
                            var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                            float reach = (2.5f + 4.5f * f.K) * (i % 2 == 0 ? 1 : 0.7f), r0 = 0.8f + reach * e, r1 = r0 + 1.6f * fade * (0.5f + f.K);
                            over.Line(c + d * r0, c + d * r1, U(1.6f), Palette.Rgba(255, 228, 150, fade));
                        }
                        break;
                    }
                    case FxBoom:
                        over.Disc(c, (float)Sheet.BombR * (0.3f + 0.7f * e), Palette.Rgba(255, 160, 60, .35f * fade));
                        over.Ring(c, (float)Sheet.BombR * e, U(4 * fade + 1), Palette.Rgba(255, 220, 140, .9f * fade));
                        over.Disc(c, 4 * fade, Palette.Rgba(255, 250, 220, fade));
                        break;
                }
            }
        }

        // ---------------- a pool of sprites for the boxes, hands' items and bombs ----------------

        readonly List<SpriteRenderer> itemPool = new List<SpriteRenderer>();
        int itemUsed;

        void DrawItemSprite(Sprite sp, float x, float y, float size, int order, float alpha = 1)
        {
            if (itemUsed >= itemPool.Count)
            {
                var r = new GameObject("ItemSprite").AddComponent<SpriteRenderer>();
                r.transform.SetParent(transform, false);
                r.sharedMaterial = unlitMaterial;
                itemPool.Add(r);
            }
            var sr = itemPool[itemUsed++];
            if (!sr.gameObject.activeSelf) sr.gameObject.SetActive(true);
            sr.sprite = sp; sr.sortingOrder = order;
            sr.color = new Color(1, 1, 1, alpha);
            sr.transform.localPosition = new Vector3(x, y, -0.01f * order);
            sr.transform.localScale = Vector3.one * (size / sp.bounds.size.x);
        }

        void EndItemSprites()
        {
            for (int i = itemUsed; i < itemPool.Count; i++)
                if (itemPool[i].gameObject.activeSelf) itemPool[i].gameObject.SetActive(false);
            itemUsed = 0;
        }
    }
}
