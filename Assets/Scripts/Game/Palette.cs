using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>Prototype palette (CSS variables and canvas colours).</summary>
    public static class Palette
    {
        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
        public static Color Rgba(int r, int g, int b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);
        public static Color WithA(Color c, float a) { c.a = a; return c; }

        public static readonly Color Table = Hex("#20160f");
        public static readonly Color Panel = Hex("#2c2017");
        public static readonly Color Panel2 = Hex("#3a2b20");
        public static readonly Color Line = Rgba(239, 226, 200, .14f);
        public static readonly Color Text = Hex("#efe2c8");
        public static readonly Color Muted = Hex("#b39f84");
        public static readonly Color Brass = Hex("#d9a54c");
        public static readonly Color BrassInk = Hex("#2a1b0c");
        public static readonly Color InkSoft = Hex("#9fb2e0");
        public static readonly Color Glass = Rgba(30, 20, 13, .8f);
        public static readonly Color Rail = Hex("#5a3418");
        public static readonly Color Gold = Hex("#ffd98a");
        public static readonly Color Stroke = Rgba(32, 20, 11, .85f);

        /// <summary>CSS hsl(), h in degrees, s and l in 0..1.</summary>
        public static Color Hsl(float h, float s, float l)
        {
            h = ((h % 360) + 360) % 360 / 360f;
            float q = l < 0.5f ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            float Hue(float t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1 / 6f) return p + (q - p) * 6 * t;
                if (t < 0.5f) return q;
                if (t < 2 / 3f) return p + (q - p) * (2 / 3f - t) * 6;
                return p;
            }
            return new Color(Hue(h + 1 / 3f), Hue(h), Hue(h - 1 / 3f));
        }
    }
}
