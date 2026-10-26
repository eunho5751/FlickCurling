using System;
using FlickCurling.Core;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>Paints the static sheet (wood, rails, house, lines, map gimmicks) and the stone sprites into textures.</summary>
    public static class BoardPainter
    {
        public const int PPU = 8;              // texture pixels per sheet unit
        public const float X0 = -6, X1 = 106;  // painted range across (rails included)
        const float FIT = 3.7f;                // css px per unit at the full-sheet view; baked pixel widths use it

        sealed class Canvas
        {
            public readonly int Wd, Ht;
            public readonly Color[] Px;
            public Canvas(int w, int h, Color bg) { Wd = w; Ht = h; Px = new Color[w * h]; for (int i = 0; i < Px.Length; i++) Px[i] = bg; }

            public void Blend(int i, int j, Color c, float a)
            {
                if (i < 0 || j < 0 || i >= Wd || j >= Ht || a <= 0) return;
                a *= c.a;
                ref var d = ref Px[j * Wd + i];
                d.r += (c.r - d.r) * a; d.g += (c.g - d.g) * a; d.b += (c.b - d.b) * a;
                d.a = d.a + a * (1 - d.a);
            }

            // world -> pixel
            public float PX(float x) => (x - X0) * PPU;
            public float PY(float y) => y * PPU;
            // pixel centre -> world
            public float WX(int i) => (i + 0.5f) / PPU + X0;
            public float WY(int j) => (j + 0.5f) / PPU;

            public void Each(float x0, float y0, float x1, float y1, Func<float, float, float> cov, Color c)
            {
                int i0 = Mathf.Max(0, Mathf.FloorToInt(PX(x0)) - 1), i1 = Mathf.Min(Wd - 1, Mathf.CeilToInt(PX(x1)) + 1);
                int j0 = Mathf.Max(0, Mathf.FloorToInt(PY(y0)) - 1), j1 = Mathf.Min(Ht - 1, Mathf.CeilToInt(PY(y1)) + 1);
                for (int j = j0; j <= j1; j++)
                {
                    float wy = WY(j);
                    for (int i = i0; i <= i1; i++) Blend(i, j, c, cov(WX(i), wy));
                }
            }

            static float Overlap(float a0, float a1, float b0, float b1) => Mathf.Clamp01(Mathf.Min(a1, b1) - Mathf.Max(a0, b0));

            public void Rect(float x0, float y0, float x1, float y1, Color c)
            {
                float px0 = PX(x0), px1 = PX(x1), py0 = PY(y0), py1 = PY(y1);
                int i0 = Mathf.Max(0, Mathf.FloorToInt(px0)), i1 = Mathf.Min(Wd - 1, Mathf.CeilToInt(px1));
                int j0 = Mathf.Max(0, Mathf.FloorToInt(py0)), j1 = Mathf.Min(Ht - 1, Mathf.CeilToInt(py1));
                for (int j = j0; j <= j1; j++)
                {
                    float cy = Overlap(j, j + 1, py0, py1);
                    if (cy <= 0) continue;
                    for (int i = i0; i <= i1; i++) Blend(i, j, c, cy * Overlap(i, i + 1, px0, px1));
                }
            }

            public void Disc(float cx, float cy, float r, Color c) =>
                Each(cx - r, cy - r, cx + r, cy + r, (x, y) => Mathf.Clamp01((r - Dist(x, y, cx, cy)) * PPU + 0.5f), c);

            public void Ring(float cx, float cy, float r, float w, Color c) =>
                Each(cx - r - w, cy - r - w, cx + r + w, cy + r + w, (x, y) => Mathf.Clamp01((w / 2 - Mathf.Abs(Dist(x, y, cx, cy) - r)) * PPU + 0.5f), c);

            public void Line(float ax, float ay, float bx, float by, float w, Color c) =>
                Each(Mathf.Min(ax, bx) - w, Mathf.Min(ay, by) - w, Mathf.Max(ax, bx) + w, Mathf.Max(ay, by) + w,
                    (x, y) => Mathf.Clamp01((w / 2 - SegDist(x, y, ax, ay, bx, by)) * PPU + 0.5f), c);

            public void Poly(double[][] pts, Color c)
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var p in pts) { minX = Mathf.Min(minX, (float)p[0]); maxX = Mathf.Max(maxX, (float)p[0]); minY = Mathf.Min(minY, (float)p[1]); maxY = Mathf.Max(maxY, (float)p[1]); }
                const float q = 0.25f / PPU;
                Each(minX, minY, maxX, maxY, (x, y) =>
                {
                    int n = 0;
                    if (Inside(pts, x - q, y - q)) n++;
                    if (Inside(pts, x + q, y - q)) n++;
                    if (Inside(pts, x - q, y + q)) n++;
                    if (Inside(pts, x + q, y + q)) n++;
                    return n / 4f;
                }, c);
            }

            static bool Inside(double[][] pts, float x, float y)
            {
                bool inside = false;
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    double xi = pts[i][0], yi = pts[i][1], xj = pts[j][0], yj = pts[j][1];
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                }
                return inside;
            }

            public Texture2D ToTexture(string name)
            {
                var t = new Texture2D(Wd, Ht, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
                t.SetPixels(Px);
                t.Apply(true, true);
                return t;
            }
        }

        static float Dist(float ax, float ay, float bx, float by) { float dx = ax - bx, dy = ay - by; return Mathf.Sqrt(dx * dx + dy * dy); }

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            float t = l2 > 0 ? Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / l2) : 0;
            return Dist(px, py, ax + t * dx, ay + t * dy);
        }

        // canvas createRadialGradient(c0, r0, c1, r1): the t whose circle passes through p
        static float RadialT(float px, float py, float x0, float y0, float r0, float x1, float y1, float r1)
        {
            float cdx = x1 - x0, cdy = y1 - y0, dr = r1 - r0, pdx = px - x0, pdy = py - y0;
            float a = cdx * cdx + cdy * cdy - dr * dr, b = pdx * cdx + pdy * cdy + r0 * dr, c = pdx * pdx + pdy * pdy - r0 * r0;
            float t;
            if (Mathf.Abs(a) < 1e-6f) t = c / (2 * b);
            else
            {
                float disc = b * b - a * c;
                if (disc < 0) return 1;
                float s = Mathf.Sqrt(disc);
                t = (b + s) / a;
                if (r0 + t * dr < 0) t = (b - s) / a;
            }
            return Mathf.Clamp01(t);
        }

        static Color Stops(float t, (float at, Color c)[] stops)
        {
            if (t <= stops[0].at) return stops[0].c;
            for (int i = 1; i < stops.Length; i++)
                if (t <= stops[i].at) return Color.Lerp(stops[i - 1].c, stops[i].c, (t - stops[i - 1].at) / (stops[i].at - stops[i - 1].at));
            return stops[stops.Length - 1].c;
        }

        static Func<double> Rng(long seed) => () => { seed = seed * 16807 % 2147483647; return (seed - 1) / 2147483646.0; };

        /// <summary>The sheet as a texture; without its house for the turn order shot (its target is drawn instead).</summary>
        public static Texture2D PaintBoard(Field f, bool house = true)
        {
            float W = (float)Sheet.W, L = (float)Sheet.L, HOG = (float)Sheet.Hog, BACK = (float)Sheet.Back, R = (float)Sheet.R, SR = (float)Sheet.SR;
            float tx = (float)Sheet.TeeX, ty = (float)Sheet.TeeY;
            var cv = new Canvas((int)((X1 - X0) * PPU), (int)(L * PPU), new Color(0, 0, 0, 0));

            // rails
            cv.Rect(-4, 0, 0, L, Palette.Rail);
            cv.Rect(W, 0, W + 4, L, Palette.Rail);
            PaintWood(cv);

            // out zone
            cv.Rect(0, BACK, W, L, Palette.Rgba(32, 20, 11, .55f));
            var hatch = Palette.Rgba(239, 226, 200, .07f);
            for (float k = -L; k < W + L; k += 5)
            {
                // clip the hatch line to the out zone box
                float ax = k, ay = BACK, bx = k + (L - BACK), by = L;
                if (bx < 0 || ax > W) continue;
                float t0 = Mathf.Max(0, (0 - ax) / (bx - ax)), t1 = Mathf.Min(1, (W - ax) / (bx - ax));
                cv.Line(ax + (bx - ax) * t0, ay + (by - ay) * t0, ax + (bx - ax) * t1, ay + (by - ay) * t1, 1 / FIT, hatch);
            }

            // zones
            foreach (var z in f.Zones)
            {
                float zx0 = (float)z.X1, zy0 = (float)z.Y1, zx1 = (float)z.X2, zy1 = (float)z.Y2;
                if (z.Kind == ZoneKind.Sand)
                {
                    cv.Rect(zx0, zy0, zx1, zy1, Palette.Rgba(196, 166, 112, .92f));
                    var r2 = Rng(42);
                    var dot = Palette.Rgba(110, 82, 40, .45f);
                    for (int i = 0; i < 420; i++)
                    {
                        float a = (float)r2(), b = (float)r2(), c = (float)r2();
                        float sz = (0.6f + c * 1.2f) / FIT;
                        float x = zx0 + a * (zx1 - zx0), y = zy1 - b * (zy1 - zy0);
                        cv.Rect(x, y - sz, x + sz, y, dot);
                    }
                }
                else
                {
                    cv.Rect(zx0, zy0, zx1, zy1, Palette.Rgba(214, 236, 246, .72f));
                    var stripe = Palette.Rgba(255, 255, 255, .7f);
                    float zw = zx1 - zx0, zh = zy1 - zy0, step = 14 / FIT;
                    for (float k = 0; k < zh + zw; k += step)
                    {
                        // from (x0, top - k) rising 45 degrees to the right, clipped to the zone
                        float ax = zx0, ay = zy1 - k, bx = zx1, by = zy1 - k + zw;
                        float t0 = Mathf.Max(0, (zy0 - ay) / (by - ay)), t1 = Mathf.Min(1, (zy1 - ay) / (by - ay));
                        if (t1 <= t0) continue;
                        cv.Line(ax + (bx - ax) * t0, ay + (by - ay) * t0, ax + (bx - ax) * t1, ay + (by - ay) * t1, 1.2f / FIT, stripe);
                    }
                }
            }

            // house
            if (house)
            {
                cv.Ring(tx, ty, (R + 18.5f) / 2, R - 18.5f, Palette.Rgba(38, 55, 100, .88f));
                cv.Ring(tx, ty, 6, 6, Palette.Rgba(178, 64, 44, .9f));
                foreach (var rr in new[] { R, 18.5f, 9, 3 }) cv.Ring(tx, ty, rr, 1 / FIT, Palette.Rgba(40, 24, 12, .5f));
            }

            // lines
            float dash = 4 / FIT;
            for (float y = HOG; y < BACK; y += 2 * dash) cv.Line(50, y, 50, Mathf.Min(BACK, y + dash), 1 / FIT, Palette.Rgba(52, 32, 16, .45f));
            if (house) cv.Rect(0, ty - 0.5f / FIT, W, ty + 0.5f / FIT, Palette.Rgba(52, 32, 16, .55f));   // (the tee line)
            cv.Rect(0, HOG - 1.5f / FIT, W, HOG + 1.5f / FIT, Palette.Rgba(59, 36, 20, .5f));   // light: the shot-clock fuse lies on it
            cv.Rect(0, BACK - 1f / FIT, W, BACK + 1f / FIT, Palette.Hex("#3b2414"));


            // posts
            foreach (var p in f.Posts)
            {
                float px = (float)p.X, py = (float)p.Y, pr = (float)p.R;
                cv.Disc(px + 2 / FIT, py - 3 / FIT, pr, Palette.Rgba(45, 22, 6, .4f));
                var stops = new[] { (0f, Palette.Hex("#f6d58a")), (.55f, Palette.Hex("#c38f3a")), (1f, Palette.Hex("#7a5016")) };
                float fx = px - pr * 0.3f, fy = py + pr * 0.35f;
                for (int j = Mathf.FloorToInt(cv.PY(py - pr)) - 1; j <= Mathf.CeilToInt(cv.PY(py + pr)) + 1; j++)
                    for (int i = Mathf.FloorToInt(cv.PX(px - pr)) - 1; i <= Mathf.CeilToInt(cv.PX(px + pr)) + 1; i++)
                    {
                        float x = cv.WX(i), y = cv.WY(j);
                        float cov = Mathf.Clamp01((pr - Dist(x, y, px, py)) * PPU + 0.5f);
                        if (cov <= 0) continue;
                        cv.Blend(i, j, Stops(RadialT(x, y, fx, fy, 1 / FIT, px, py, pr), stops), cov);
                    }
            }

            // kinked rails
            foreach (var k in f.Kinks)
            {
                cv.Poly(k, Palette.Rail);
                for (int i = 0; i < k.Length; i++)
                {
                    var a = k[i]; var b = k[(i + 1) % k.Length];
                    cv.Line((float)a[0], (float)a[1], (float)b[0], (float)b[1], 1.5f / FIT, Palette.Hex("#7a4a26"));
                }
            }

            // rail inner edge highlight
            cv.Rect(-1.5f / FIT, 0, 0, L, Palette.Rgba(255, 220, 170, .18f));
            cv.Rect(W, 0, W + 1.5f / FIT, L, Palette.Rgba(255, 220, 170, .18f));

            // rail windows: brass = lively, muted grey-brown = dead (beside the out area)
            foreach (var w in f.Walls)
            {
                if (w.IsBottom) continue;
                bool lively = !w.Dead;
                if (w.IsSide)
                {
                    // side walls run past the end of the sheet; paint only the part beside it
                    float bot = Mathf.Max(0, (float)Math.Min(w.Y1, w.Y2)), top = Mathf.Min(L, (float)Math.Max(w.Y1, w.Y2));
                    if (top - bot <= 0) continue;
                    bool left = w.X1 == 0;
                    float rx = left ? -4 : W;
                    cv.Rect(rx, bot, rx + 4, top, lively ? Palette.Brass : Palette.Hex("#6e5a4a"));
                    if (lively)
                    {
                        float hx = left ? 0 : W - 2 / FIT;
                        cv.Rect(hx, bot, hx + 2 / FIT, top, Palette.Rgba(255, 226, 150, .85f));
                    }
                }
                else
                {
                    cv.Line((float)w.X1, (float)w.Y1, (float)w.X2, (float)w.Y2, 1.2f, lively ? Palette.Brass : Palette.Hex("#2a1910"));
                }
            }
            return cv.ToTexture("Board_" + f.Key + (house ? "" : "_nohouse"));
        }

        static void PaintWood(Canvas cv)
        {
            float W = (float)Sheet.W;
            int i0 = (int)cv.PX(0), i1 = (int)cv.PX(W);
            var c0 = Palette.Hex("#cf9b5a"); var c1 = Palette.Hex("#ddae6c"); var c2 = Palette.Hex("#cd985a");
            for (int i = i0; i < i1; i++)
            {
                float t = (i - i0 + 0.5f) / (i1 - i0);
                var col = t < 0.5f ? Color.Lerp(c0, c1, t / 0.5f) : Color.Lerp(c1, c2, (t - 0.5f) / 0.5f);
                for (int j = 0; j < cv.Ht; j++) cv.Px[j * cv.Wd + i] = col;
            }
            // grain, as the prototype's 800 x 1776 canvas (dpr 2.5): row yy counts down from the far end
            var r = Rng(7);
            const float dpr = 2.5f;
            int h = cv.Ht;
            for (int g = 0; g < 150; g++)
            {
                float x0 = (float)r() * (i1 - i0), amp = (2 + (float)r() * 10) * dpr, f = 0.002f + (float)r() * 0.006f, ph = (float)r() * 6.28f;
                bool dark = r() < 0.5;
                float alpha = 0.04f + (float)r() * 0.09f, lw = (0.6f + (float)r() * 2.2f) * dpr;
                var col = dark ? Palette.Rgba(120, 70, 28, alpha) : Palette.Rgba(245, 205, 150, alpha);
                for (int j = 0; j < h; j++)
                {
                    float yy = h - j - 0.5f;
                    float xx = x0 + Mathf.Sin(yy * f / dpr + ph) * amp;
                    float a = xx - lw / 2, b = xx + lw / 2;
                    for (int i = Mathf.Max(0, Mathf.FloorToInt(a)); i <= Mathf.Min(i1 - i0 - 1, Mathf.CeilToInt(b)); i++)
                    {
                        float cov = Mathf.Clamp01(Mathf.Min(i + 1, b) - Mathf.Max(i, a));
                        cv.Blend(i0 + i, j, col, cov);
                    }
                }
            }
        }

        public static Texture2D PaintStone(Team team, int size = 128)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Stone_" + team, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            t.SetPixels(StonePixels(team, size));
            t.Apply(true, true);
            return t;
        }

        static Color[] StonePixels(Team team, int size)
        {
            var px = new Color[size * size];
            float r = size / 2f - 1, c = size / 2f;
            var stops = team == Team.B
                ? new[] { (0f, Palette.Hex("#6a6a6a")), (.42f, Palette.Hex("#232323")), (1f, Palette.Hex("#0b0b0b")) }
                : new[] { (0f, Palette.Hex("#ffffff")), (.6f, Palette.Hex("#eeeae0")), (1f, Palette.Hex("#bcb4a3")) };
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = i + 0.5f, y = j + 0.5f;
                    float cov = Mathf.Clamp01(r - Dist(x, y, c, c) + 0.5f);
                    var col = Stops(RadialT(x, y, c - r * 0.35f, c + r * 0.4f, r * 0.08f, c, c, r), stops);
                    col.a = cov;
                    px[j * size + i] = col;
                }
            return px;
        }

        /// <summary>
        /// The table the sheet sits on: dark wooden planks running along the sheet, tileable (planks and grain are
        /// periodic over the texture), for the room left beside and around the sheet on wide screens.
        /// </summary>
        public static Texture2D PaintTable(int size = 512)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Table", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color[size * size];
            var baseCol = Palette.Hex("#2b1d13");
            const int planks = 4;
            int pw = size / planks;
            var r = Rng(11);
            var tone = new float[planks];
            var phase = new float[planks];
            for (int p = 0; p < planks; p++) { tone[p] = 0.88f + (float)r() * 0.22f; phase[p] = (float)r() * 6.28f; }
            const float TwoPi = Mathf.PI * 2;
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    int p = i / pw;
                    float u = (float)i / size, v = (float)j / size;
                    // grain lines wander a little along the plank; integer frequencies keep the tile seamless
                    float wobble = 0.012f * Mathf.Sin(TwoPi * 2 * v + phase[p]) + 0.006f * Mathf.Sin(TwoPi * 5 * v + 2 * phase[p]);
                    float grain = Mathf.Sin(TwoPi * 37 * (u + wobble) + phase[p]) * 0.5f + Mathf.Sin(TwoPi * 91 * (u + wobble * 0.5f)) * 0.25f;
                    float k = tone[p] * (1 + 0.07f * grain);
                    int seam = i % pw;
                    if (seam < 2 || seam > pw - 2) k *= 0.6f;   // the gap between planks
                    var col = new Color(baseCol.r * k, baseCol.g * k, baseCol.b * k, 1);
                    px[j * size + i] = col;
                }
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>Soft-edged black rectangle for the sheet's drop shadow, drawn 9-sliced (border = blur).</summary>
        public static Texture2D PaintShadow(int size = 64, int blur = 24)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SheetShadow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    // distance outside the inner rectangle [blur, size - blur]
                    float dx = Mathf.Max(0, Mathf.Max(blur - (i + 0.5f), (i + 0.5f) - (size - blur)));
                    float dy = Mathf.Max(0, Mathf.Max(blur - (j + 0.5f), (j + 0.5f) - (size - blur)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / blur;
                    px[j * size + i] = new Color(0, 0, 0, 1 - Mathf.SmoothStep(0, 1, d));
                }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>
        /// Side icons for text (black and white stones side by side in one 2:1 atlas): the stone with a thin dark rim
        /// so the white one reads on the wood as well as on the dark panels.
        /// </summary>
        public static Texture2D PaintStoneIcons(int size = 96)
        {
            var t = new Texture2D(size * 2, size, TextureFormat.RGBA32, true) { name = "StoneIcons", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[size * 2 * size];
            var rim = Palette.Rgba(24, 14, 6, .85f);
            for (int k = 0; k < 2; k++)
            {
                var src = StonePixels(k == 0 ? Team.B : Team.W, size);
                float c = size / 2f, r = size / 2f - 1;
                for (int j = 0; j < size; j++)
                    for (int i = 0; i < size; i++)
                    {
                        float d = Dist(i + 0.5f, j + 0.5f, c, c);
                        var s = src[j * size + i];
                        // rim: about 6% of the radius at the edge
                        float rimCov = Mathf.Clamp01((d - (r - r * 0.06f)) * 1.5f) * Mathf.Clamp01(r - d + 0.5f);
                        var col = Color.Lerp(s, new Color(rim.r, rim.g, rim.b, s.a), rimCov * rim.a);
                        px[j * size * 2 + k * size + i] = col;
                    }
            }
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>White anti-aliased disc, tinted by the renderer (shadows, dots).</summary>
        public static Texture2D PaintDisc(int size = 64)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Disc", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[size * size];
            float r = size / 2f - 1, c = size / 2f;
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                    px[j * size + i] = new Color(1, 1, 1, Mathf.Clamp01(r - Dist(i + 0.5f, j + 0.5f, c, c) + 0.5f));
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }
    }
}
