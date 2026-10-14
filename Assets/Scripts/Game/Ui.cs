using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// Code-built uGUI. The canvas uses constant pixel size with scaleFactor = css px, so every size here is in the
    /// prototype's CSS pixels (y measured down from the top).
    /// </summary>
    public static class Ui
    {
        // body (Noto Sans KR Regular) and display (Noto Sans KR Bold) TMP font assets; both fall back to Noto Sans JP for Japanese
        public static TMP_FontAsset Body, Display;
        static Material bodyStroke, displayStroke;
        public static Sprite Round, RoundStroke, White, DotSprite, StoneB, StoneW, FadeUp;
        const int RoundPx = 48, RoundR = 16;

        public static void Init(Sprite dot, Sprite stoneB, Sprite stoneW, TMP_FontAsset body, TMP_FontAsset display)
        {
            Body = body ? body : TMP_Settings.defaultFontAsset;
            Display = display ? display : Body;
            bodyStroke = StrokeMaterial(Body);
            displayStroke = StrokeMaterial(Display);
            DotSprite = dot; StoneB = stoneB; StoneW = stoneW;
            White = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 100);
            Round = MakeRound(false);
            RoundStroke = MakeRound(true);
            var t = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int j = 0; j < 64; j++)
            {
                // overlay.soft: rgba(20,13,8,.75) at the bottom fading to clear at 55% height
                float y = (j + 0.5f) / 64f, a = y < 0.55f ? Mathf.Lerp(.75f, 0, y / 0.55f) : 0;
                t.SetPixel(0, j, Palette.Rgba(20, 13, 8, a));
            }
            t.Apply();
            FadeUp = Sprite.Create(t, new Rect(0, 0, 1, 64), new Vector2(.5f, .5f), 100);
        }

        // canvas strokeText: a dark outline around world labels
        static Material StrokeMaterial(TMP_FontAsset f)
        {
            var m = new Material(f.material) { name = f.name + " Stroke" };
            m.EnableKeyword(ShaderUtilities.Keyword_Outline);
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.22f);
            m.SetColor(ShaderUtilities.ID_OutlineColor, Palette.Stroke);
            m.SetFloat(ShaderUtilities.ID_FaceDilate, 0.1f);
            return m;
        }

        public static Material StrokeFor(TMP_FontAsset f) => f == Display ? displayStroke : bodyStroke;

        static Sprite MakeRound(bool stroke)
        {
            var t = new Texture2D(RoundPx, RoundPx, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int j = 0; j < RoundPx; j++)
                for (int i = 0; i < RoundPx; i++)
                {
                    // signed distance to a rounded square
                    float x = i + 0.5f, y = j + 0.5f;
                    float qx = Mathf.Max(Mathf.Abs(x - RoundPx / 2f) - (RoundPx / 2f - RoundR), 0);
                    float qy = Mathf.Max(Mathf.Abs(y - RoundPx / 2f) - (RoundPx / 2f - RoundR), 0);
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - RoundR;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (stroke) a *= Mathf.Clamp01(d + 1.5f);   // a ~1px band along the edge
                    t.SetPixel(i, j, new Color(1, 1, 1, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, RoundPx, RoundPx), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(RoundR + 2, RoundR + 2, RoundR + 2, RoundR + 2));
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        /// <summary>Place by top-left corner in css px.</summary>
        public static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static Image Panel(Transform parent, string name, Color fill, Color? border = null, float radius = 10)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Round; img.type = Image.Type.Sliced; img.color = fill;
            img.pixelsPerUnitMultiplier = RoundR / Mathf.Max(1, radius);
            if (border.HasValue)
            {
                var b = Rect(rt, "Border");
                Stretch(b);
                var bi = b.gameObject.AddComponent<Image>();
                bi.sprite = RoundStroke; bi.type = Image.Type.Sliced; bi.color = border.Value; bi.raycastTarget = false;
                bi.pixelsPerUnitMultiplier = img.pixelsPerUnitMultiplier;
                b.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            return img;
        }

        /// <summary>A TMP label. display = the bold display face for titles and numbers.</summary>
        public static TextMeshProUGUI Label(Transform parent, string text, int size, Color color, bool display = false, TextAnchor align = TextAnchor.MiddleLeft, string name = "Text")
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = display ? Display : Body;
            t.fontSize = size; t.color = color; t.alignment = Align(align); t.text = text;
            t.richText = true; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public static TextMeshProUGUI NoWrap(TextMeshProUGUI t) { t.textWrappingMode = TextWrappingModes.NoWrap; return t; }

        public static TextAlignmentOptions Align(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Left;
            }
        }

        public static VerticalLayoutGroup VStack(GameObject go, int padL, int padR, int padT, int padB, float spacing, TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padL, padR, padT, padB);
            v.spacing = spacing; v.childAlignment = align;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(GameObject go, int padL, int padR, int padT, int padB, float spacing, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(padL, padR, padT, padB);
            h.spacing = spacing; h.childAlignment = align;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            return h;
        }

        public static void Fit(GameObject go, bool w, bool h)
        {
            var f = go.AddComponent<ContentSizeFitter>();
            f.horizontalFit = w ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            f.verticalFit = h ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        /// <summary>A .btn: rounded, padded, label sized to content. primary = the brass .btn.primary.</summary>
        public static Button Button(Transform parent, string label, bool primary, Action onClick, int size = 14, string name = "Button")
        {
            var img = Panel(parent, name, primary ? Palette.Brass : Palette.Panel2, primary ? (Color?)null : Palette.Line, 8);
            HStack(img.gameObject, primary ? 20 : 16, primary ? 20 : 16, primary ? 9 : 10, primary ? 9 : 10, 0, TextAnchor.MiddleCenter);
            var t = Label(img.transform, label, primary ? 18 : size, primary ? Palette.BrassInk : Palette.Text, primary, TextAnchor.MiddleCenter);
            Ui.NoWrap(t);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors; cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f); cb.pressedColor = new Color(.9f, .9f, .9f); b.colors = cb;
            b.onClick.AddListener(new UnityAction(onClick));
            return b;
        }

        public static TextMeshProUGUI ButtonText(Button b) => b.GetComponentInChildren<TextMeshProUGUI>();

        public static Image Dot(Transform parent, Core.Team team, float size = 11)
        {
            var rt = Rect(parent, "Dot");
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = team == Core.Team.B ? StoneB : StoneW; img.raycastTarget = false; img.preserveAspect = true;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = size; le.preferredHeight = le.minHeight = size;
            return img;
        }
    }

    /// <summary>Wrapping row layout (CSS flex-wrap) for pill groups and button rows.</summary>
    public sealed class FlowLayout : LayoutGroup
    {
        public float SpacingX = 6, SpacingY = 6;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            float w = padding.horizontal, min = padding.horizontal;
            for (int i = 0; i < rectChildren.Count; i++)
            {
                float cw = LayoutUtility.GetPreferredWidth(rectChildren[i]);
                w += cw + (i > 0 ? SpacingX : 0);
                min = Mathf.Max(min, cw + padding.horizontal);
            }
            SetLayoutInputForAxis(min, w, -1, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            float h = Run(false);
            SetLayoutInputForAxis(h, h, -1, 1);
        }

        public override void SetLayoutHorizontal() => Run(true);
        public override void SetLayoutVertical() => Run(true);

        float Run(bool apply)
        {
            float width = rectTransform.rect.width;
            float x = padding.left, y = padding.top, rowH = 0;
            foreach (var c in rectChildren)
            {
                // a child wider than the row gets the row (its text then ends in an ellipsis)
                float cw = Mathf.Min(LayoutUtility.GetPreferredWidth(c), width - padding.horizontal), ch = LayoutUtility.GetPreferredHeight(c);
                if (x + cw > width - padding.right && x > padding.left) { x = padding.left; y += rowH + SpacingY; rowH = 0; }
                if (apply) { SetChildAlongAxis(c, 0, x, cw); SetChildAlongAxis(c, 1, y, ch); }
                x += cw + SpacingX;
                rowH = Mathf.Max(rowH, ch);
            }
            return y + rowH + padding.bottom;
        }
    }

    /// <summary>A radio group of .pills (the selected one is brass).</summary>
    public sealed class Pills
    {
        public readonly RectTransform Root;
        readonly Image[] imgs;
        readonly TextMeshProUGUI[] texts;
        public int Selected { get; private set; }
        public event Action<int> Changed;

        public Pills(Transform parent, string[] labels, int selected)
        {
            Root = Ui.Rect(parent, "Pills");
            Root.gameObject.AddComponent<FlowLayout>();
            imgs = new Image[labels.Length];
            texts = new TextMeshProUGUI[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                var img = Ui.Panel(Root, "Pill", Palette.Panel2, Palette.Line, 15);
                Ui.HStack(img.gameObject, 12, 12, 7, 7, 0, TextAnchor.MiddleCenter);
                var t = Ui.Label(img.transform, labels[i], 13, Palette.Text, false, TextAnchor.MiddleCenter);
                Ui.NoWrap(t);
                var b = img.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => Select(k));
                imgs[i] = img; texts[i] = t;
            }
            Select(selected, false);
        }

        public void SetLabels(string[] labels)
        {
            for (int i = 0; i < texts.Length && i < labels.Length; i++) texts[i].text = labels[i];
        }

        public void Select(int i, bool notify = true)
        {
            Selected = i;
            for (int k = 0; k < imgs.Length; k++)
            {
                imgs[k].color = k == i ? Palette.Brass : Palette.Panel2;
                texts[k].color = k == i ? Palette.BrassInk : Palette.Text;
            }
            if (notify) Changed?.Invoke(i);
        }
    }
}
