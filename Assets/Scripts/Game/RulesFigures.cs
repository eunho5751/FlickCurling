using FlickCurling.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The pictures at the top of the rules popup's pages: a part of the real sheet (BoardPainter's board, painted once)
    /// with stones and marks on it, and labels beside it taken from the page's own items. Drawn from code into the page's
    /// figure rect, again whenever the page is filled (so the labels follow the language).
    /// </summary>
    public static class RulesFigures
    {
        static Texture2D board, stoneB, stoneW, disc;
        static Texture2D boardBare;   // the sheet without its house (the turn order shot's)
        static Sprite arrowH, arrowV, hand, orbB, orbW, bomb, target;

        // the accent of the items' labels (on the cream), and the marks and ink on the wood
        static readonly Color Accent = new Color32(0xb0, 0x4a, 0x1c, 0xff);
        static readonly Color Mark = Palette.Gold, Dim = new Color(1, 1, 1, 0.45f);
        static readonly Color WoodInk = new Color32(0x2a, 0x1b, 0x0c, 0xff), Cream = new Color32(0xf6, 0xea, 0xd2, 0xff);

        /// <summary>How tall the figure of this page is (reference px).</summary>
        public static float Height(string page) => page switch
        {
            "basics" => 470,
            "controls" => 360,
            _ => 330,
        };

        /// <summary>Draws the page's figure into <paramref name="area"/> (emptied first), at its top (the area may be
        /// taller: the room left goes under the picture).</summary>
        public static void Draw(RectTransform area, string page, TMP_FontAsset font)
        {
            for (int i = area.childCount - 1; i >= 0; i--) Object.Destroy(area.GetChild(i).gameObject);
            Load();
            var top = new GameObject("Picture", typeof(RectTransform)).GetComponent<RectTransform>();
            top.SetParent(area, false);
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.anchoredPosition = Vector2.zero; top.sizeDelta = new Vector2(0, Height(page));
            var f = new Fig(top, font, Height(page));
            switch (page)
            {
                case "basics":
                    f.Crop(0, 222, -150);
                    f.Note(50, (float)Sheet.LaunchY, "rules.basics.3.k");
                    f.Note(76, (float)Sheet.Hog, "rules.basics.4.k");
                    f.Note(78, (float)Sheet.TeeY, "rules.basics.5.k");
                    f.Note(100, 110, "rules.basics.6.k");
                    f.Note(76, (float)Sheet.Back, "rules.basics.7.k");
                    f.Stone(Team.B, 50, (float)Sheet.LaunchY);
                    break;
                case "order":
                {
                    // the turn order shot: the sheet without its house, the target, and both stones (the other's
                    // see-through), a line from the target to each; the closer one's in gold
                    f.Crop(126, 190, -90, house: false);
                    float tx = 48, ty = 157;
                    f.Sized(target, tx, ty, (float)Sheet.LsdTargetR * 2);
                    f.Line(tx, ty, 56, 150, 5, Mark); f.Line(tx, ty, 34, 171, 3, Dim);
                    f.Stone(Team.B, 56, 150); f.Stone(Team.W, 34, 171, true);
                    f.Note(tx + 6, ty + 4, "rules.order.target");
                    f.Note(34, 171, "hud.first");    // (further off: goes first)
                    f.Note(56, 150, "hud.hammer");   // (closer: goes second)
                    break;
                }
                case "coin":
                {
                    // the turn order coin, both faces, over the house
                    f.Crop(134, 210, 0);
                    float cy = (float)Sheet.TeeY, size = 34;
                    f.Sized(CoinArt.Face(Team.B), (float)Sheet.TeeX - 22, cy, size);
                    f.Sized(CoinArt.Face(Team.W), (float)Sheet.TeeX + 22, cy, size);
                    break;
                }
                case "end":
                {
                    f.Crop(134, 210, 0);
                    float tx = (float)Sheet.TeeX, ty = (float)Sheet.TeeY;
                    var stones = new (Team t, float x, float y, int n)[] { (Team.B, 54, 167, 1), (Team.B, 39, 180, 2), (Team.W, 63, 186, 0), (Team.B, 71, 158, 0) };
                    foreach (var st in stones) f.Line(tx, ty, st.x, st.y, st.n > 0 ? 5 : 3, st.n > 0 ? Mark : Dim);
                    foreach (var st in stones) { f.Stone(st.t, st.x, st.y); if (st.n > 0) f.Number(st.x, st.y, st.n); }
                    break;
                }
                case "controls":
                {
                    f.Crop(0, 100, 0);
                    float sx = 50, sy = (float)Sheet.LaunchY;
                    for (float y = 1; y < Sheet.Hog - 1; y += 5) f.Line(sx, y, sx, Mathf.Min(y + 3, (float)Sheet.Hog - 1), 3, Dim);   // the split under the stone
                    f.Stone(Team.B, sx, sy);
                    f.Picture(arrowH, 25, 34, 140, 39); f.Tag(25, 42, "rules.controls.1.k");
                    f.Picture(arrowV, 76, 30, 39, 140); f.Tag(76, 51, "rules.controls.2.k");
                    f.Line(sx, sy, 30, 80, 4, Mark);   // the aim straight at the touch
                    f.Picture(hand, 30, 80, 60, 86, new Vector2(0.38f, 1)); f.Tag(64, 83, "rules.controls.4.k");
                    break;
                }
                case "itemkinds":
                {
                    // a giant stone, a mini one and a bomb on the sheet
                    f.Crop(80, 140, 0);
                    f.Stone(Team.B, 30, 110, size: (float)Sheet.GiantSize);
                    f.Stone(Team.W, 52, 108, size: (float)Sheet.MiniSize);
                    f.Stone(Team.B, 72, 110);
                    f.Sized(bomb, 72, 110, (float)Sheet.SR * 2 * 1.3f / 2);
                    break;
                }
                case "items":
                {
                    // a stone of one side on its way into its own orb, the other side's orb beside
                    f.Crop(70, 140, 0);
                    float d = (float)(2 * Sheet.ItemBoxR);
                    f.Line(50, 78, 38, 112, 4, Mark);
                    f.Stone(Team.B, 50, 78);
                    f.Sized(orbB, 38, 112, d); f.Sized(orbW, 66, 124, d);
                    break;
                }
            }
        }

        static void Load()
        {
            if (board) return;
            GameConfig.Load().Apply();   // the stone size (the main menu has not set it)
            board = BoardPainter.PaintBoard(Maps.Build("base"));
            stoneB = BoardPainter.PaintStone(Team.B);
            stoneW = BoardPainter.PaintStone(Team.W);
            disc = BoardPainter.PaintDisc();
            arrowH = Resources.Load<Sprite>("Tutorial/arrow_h");
            arrowV = Resources.Load<Sprite>("Tutorial/arrow_v");
            hand = Resources.Load<Sprite>("Tutorial/hand");
            orbB = Resources.Load<Sprite>("Items/box_b");
            orbW = Resources.Load<Sprite>("Items/box_w");
            bomb = Resources.Load<Sprite>("Items/bomb");
            target = Resources.Load<Sprite>("Board/target");
            boardBare = BoardPainter.PaintBoard(Maps.Build("base"), false);
        }

        // one figure: the sheet from y0 to y1 at the figure's height, its centre shifted sideways by dx (room for notes)
        sealed class Fig
        {
            readonly RectTransform area;
            readonly TMP_FontAsset font;
            readonly float h;
            float y0, scale, cx;

            public Fig(RectTransform area, TMP_FontAsset font, float height) { this.area = area; this.font = font; h = height; }

            public void Crop(float from, float to, float dx, bool house = true)
            {
                y0 = from; scale = h / (to - from); cx = dx;
                var img = New<RawImage>("Sheet");
                img.texture = house ? board : boardBare;
                float L = board.height / (float)BoardPainter.PPU;
                img.uvRect = new Rect(0, from / L, 1, (to - from) / L);
                Place(img.rectTransform, new Vector2(cx, 0), new Vector2((BoardPainter.X1 - BoardPainter.X0) * scale, h));
            }

            // sheet units to the figure's local px (its centre the origin)
            Vector2 P(float x, float y) => new Vector2(cx + (x - (BoardPainter.X0 + BoardPainter.X1) / 2) * scale, (y - y0) * scale - h / 2);
            float HalfW => (BoardPainter.X1 - BoardPainter.X0) / 2 * scale;

            public void Stone(Team t, float x, float y, bool faded = false, float size = 1)
            {
                var img = New<RawImage>("Stone");
                img.texture = t == Team.B ? stoneB : stoneW;
                if (faded) img.color = new Color(1, 1, 1, 0.5f);
                float d = (float)Sheet.SR * 2 * size * scale;
                Place(img.rectTransform, P(x, y), new Vector2(d, d));
            }

            public void Line(float x0, float y0_, float x1, float y1, float width, Color c) => Seg(P(x0, y0_), P(x1, y1), width, c);

            void Seg(Vector2 a, Vector2 b, float width, Color c)
            {
                var img = New<Image>("Line");
                img.color = c;
                var rt = img.rectTransform;
                Place(rt, (a + b) / 2, new Vector2((b - a).magnitude, width));
                rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            }

            // a picture as wide as this many sheet units
            public void Sized(Sprite s, float x, float y, float units) => Picture(s, x, y, units * scale, units * scale);

            public void Picture(Sprite s, float x, float y, float w, float hgt, Vector2? pivot = null)
            {
                var img = New<Image>("Picture");
                img.sprite = s; img.preserveAspect = true;
                Place(img.rectTransform, P(x, y), new Vector2(w, hgt));
                if (pivot.HasValue) { img.rectTransform.pivot = pivot.Value; img.rectTransform.anchoredPosition = P(x, y); }
            }

            // a label on the wood, in dark ink
            public void Tag(float x, float y, string key)
            {
                var t = Text(Loc.T(key), 34, WoodInk);
                t.alignment = TextAlignmentOptions.Bottom;
                Place(t.rectTransform, P(x, y), new Vector2(300, 44));
                t.rectTransform.pivot = new Vector2(0.5f, 0);
                t.rectTransform.anchoredPosition = P(x, y);
            }

            // a label beside the sheet (side: 1 right, -1 left), with a line from the point on the sheet to it
            public void Note(float x, float y, string key, int side = 1)
            {
                var p = P(x, y);
                float tx = side > 0 ? cx + HalfW + 24 : cx - HalfW - 24;
                Seg(p, new Vector2(tx, p.y), 3, Accent);
                var dot = New<Image>("Dot");
                dot.color = Accent;
                Place(dot.rectTransform, p, new Vector2(10, 10));
                var t = Text(Loc.T(key), 32, Accent);
                t.alignment = side > 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
                var rt = t.rectTransform;
                Place(rt, new Vector2(tx + side * 8, p.y), new Vector2(300, 40));
                rt.pivot = new Vector2(side > 0 ? 0 : 1, 0.5f);
                rt.anchoredPosition = new Vector2(tx + side * 8, p.y);
            }

            // a round dot, this many px across
            public void Dot(float x, float y, float px, Color c)
            {
                var d = New<RawImage>("Dot");
                d.texture = disc; d.color = c;
                Place(d.rectTransform, P(x, y), new Vector2(px, px));
            }

            public void Cross(float x, float y)
            {
                var t = Text("×", 64, new Color32(0xe0, 0x3a, 0x2a, 0xff));
                Place(t.rectTransform, P(x, y) + new Vector2(0, 4), new Vector2(80, 80));
            }

            public void Ring(float x, float y)
            {
                float r = ((float)Sheet.SR + 2.5f) * scale;
                const int N = 28;
                for (int i = 0; i < N; i++)
                {
                    float a0 = i * 2 * Mathf.PI / N, a1 = (i + 0.6f) * 2 * Mathf.PI / N;
                    var c = P(x, y);
                    Seg(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, 4, Mark);
                }
            }

            // a counting stone's number, in a cream badge at its upper right
            public void Number(float x, float y, int n)
            {
                float off = ((float)Sheet.SR + 1) * scale;
                var at = P(x, y) + new Vector2(off, off);
                var badge = New<RawImage>("Badge");
                badge.texture = disc; badge.color = Cream;
                Place(badge.rectTransform, at, new Vector2(46, 46));
                var t = Text(n.ToString(), 32, Accent);
                Place(t.rectTransform, at + new Vector2(0, 2), new Vector2(46, 46));
            }

            TextMeshProUGUI Text(string s, float size, Color c)
            {
                var t = New<TextMeshProUGUI>("Text");
                t.font = font; t.fontSize = size; t.color = c; t.text = s;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.alignment = TextAlignmentOptions.Center;
                return t;
            }

            T New<T>(string name) where T : Graphic
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(area, false);
                var g = go.AddComponent<T>();
                g.raycastTarget = false;
                return g;
            }

            static void Place(RectTransform rt, Vector2 pos, Vector2 size)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos; rt.sizeDelta = size;
            }
        }
    }
}
