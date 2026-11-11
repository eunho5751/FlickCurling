using FlickCurling.Core;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// The turn order coin, painted once from code: a gold coin with a raised rim, a black stone on one face and a white
    /// one on the other (the game's own stone, BoardPainter's). For the toss in the match (<see cref="Hud.CoinToss"/>) and
    /// the rules' picture.
    /// </summary>
    public static class CoinArt
    {
        const int Size = 256;
        static Sprite faceB, faceW;

        /// <summary>The coin's face with the side's stone (the colour as painted: pass the side as shown).</summary>
        public static Sprite Face(Team t)
        {
            // (Unity's own null: kept from being unloaded with a scene's unused assets, but checked all the same)
            if (t == Team.B) { if (!faceB) faceB = Paint(Team.B); return faceB; }
            if (!faceW) faceW = Paint(Team.W);
            return faceW;
        }

        static readonly Color GoldLight = new Color32(0xff, 0xe7, 0x9a, 0xff), Gold = new Color32(0xf0, 0xbb, 0x45, 0xff),
            GoldDeep = new Color32(0xb8, 0x7a, 0x1e, 0xff), Rim = new Color32(0x7a, 0x4a, 0x10, 0xff);

        static Sprite Paint(Team stone)
        {
            var px = new Color[Size * Size];
            float c = Size / 2f, R = Size / 2f - 2;
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                {
                    float dx = i + 0.5f - c, dy = j + 0.5f - c, d = Mathf.Sqrt(dx * dx + dy * dy) / R;
                    if (d > 1.01f) { px[j * Size + i] = Color.clear; continue; }
                    Color col;
                    if (d > 0.93f) col = Rim;                                                        // the dark outer edge
                    else if (d > 0.80f) col = Color.Lerp(GoldLight, Gold, Mathf.InverseLerp(0.80f, 0.93f, d));   // the raised rim
                    else if (d > 0.77f) col = GoldDeep;                                              // the groove inside it
                    else
                    {
                        // the face: lit from the top left
                        float lit = Mathf.Clamp01(0.55f - (dx - dy) / (2.2f * R));
                        col = Color.Lerp(Gold, GoldLight, lit);
                    }
                    col.a = Mathf.Clamp01((1.01f - d) * R / 1.5f);   // a soft edge
                    px[j * Size + i] = col;
                }
            // the stone in the middle, 62% of the coin
            int s = Mathf.RoundToInt(Size * 0.62f), o = (Size - s) / 2;
            var st = BoardPainter.StonePixels(stone, s);
            for (int j = 0; j < s; j++)
                for (int i = 0; i < s; i++)
                {
                    var a = st[j * s + i];
                    if (a.a <= 0) continue;
                    int k = (o + j) * Size + o + i;
                    var b = px[k];
                    px[k] = new Color(Mathf.Lerp(b.r, a.r, a.a), Mathf.Lerp(b.g, a.g, a.a), Mathf.Lerp(b.b, a.b, a.a), b.a);
                }
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Coin", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            t.SetPixels(px);
            t.Apply();
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;   // (made once for the whole run: a scene change must not take it)
            var sp = Sprite.Create(t, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100);
            sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sp;
        }
    }
}
