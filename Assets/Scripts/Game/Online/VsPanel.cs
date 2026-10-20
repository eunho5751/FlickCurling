using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game.Online
{
    /// <summary>
    /// The record panel shown as an online match opens (Docs/OnlineMatch.md, 3.3): the opponent's card on top, this
    /// player's at the bottom and VS between them, each card with its stone, nickname and won / lost / drawn. Its own
    /// overlay canvas (1080 x 1920 reference, like the menus); it slides in, holds, fades out and calls back.
    /// It holds until <c>ready</c> says both players have opened the match, then for the time given from that moment,
    /// so it ends together on both devices however long each took to get there.
    /// The art is in Resources/Online (Codex).
    /// </summary>
    public static class VsPanel
    {
        const float CardW = 860, CardH = 300, CardY = 420, StoneSize = 190;
        static readonly Color Ink = new Color32(0x4a, 0x2a, 0x12, 0xff), Value = new Color32(0x86, 0x61, 0x3d, 0xff);

        const float InTime = 0.55f, OutTime = 0.25f;

        /// <summary>Shows the panel; returns it, to be destroyed if the match is called off meanwhile.</summary>
        public static GameObject Show(Transform parent, int sortingOrder, float seconds, Func<bool> ready, Action done)
        {
            var go = new GameObject("VsPanel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();   // nothing under it takes a touch while it is up
            var group = go.AddComponent<CanvasGroup>();

            var dim = Ui.Rect(go.transform, "Dim").gameObject.AddComponent<Image>();
            Ui.Stretch(dim.rectTransform);
            dim.color = new Color(0.08f, 0.05f, 0.03f, 0.72f);

            // this device's player is shown as black, the opponent as white
            var top = Card(go.transform, "Opponent", new Vector2(0, CardY), "stone_white", OnlineLobby.OpponentName, OnlineLobby.OpponentStats);
            var bottom = Card(go.transform, "Me", new Vector2(0, -CardY), "stone_black", OnlineLobby.MyName, OnlineLobby.MyStats);
            var vs = Ui.Rect(go.transform, "Vs").gameObject.AddComponent<Image>();
            vs.sprite = Resources.Load<Sprite>("Online/vs_emblem"); vs.preserveAspect = true; vs.raycastTarget = false;
            vs.rectTransform.sizeDelta = new Vector2(440, 264);

            // in: the cards slide in from either side and VS pops; out: everything fades
            group.alpha = 0;
            top.anchoredPosition += new Vector2(900, 0);
            bottom.anchoredPosition -= new Vector2(900, 0);
            vs.rectTransform.localScale = Vector3.zero;
            var seq = DOTween.Sequence().SetUpdate(true).SetLink(go);
            seq.Append(DOTween.To(() => group.alpha, a => group.alpha = a, 1, 0.2f));
            seq.Join(SlideX(top, 0.35f));
            seq.Join(SlideX(bottom, 0.35f));
            seq.Append(vs.rectTransform.DOScale(1, 0.3f).SetEase(Ease.OutBack));
            // then held until both are here, for the time given counted from then (at least the entrance), and faded
            var hold = go.AddComponent<Hold>();
            hold.Begin(ready, seconds, () =>
                DOTween.To(() => group.alpha, a => group.alpha = a, 0, OutTime).SetUpdate(true).SetLink(go)
                    .OnComplete(() => { UnityEngine.Object.Destroy(go); done?.Invoke(); }));
            return go;
        }

        // waits for both players, then counts the panel's time from the later of its own start and that moment
        sealed class Hold : MonoBehaviour
        {
            Func<bool> ready;
            float seconds, shown, from = -1;
            Action finish;

            public void Begin(Func<bool> isReady, float time, Action onDone)
            {
                ready = isReady; seconds = time; finish = onDone; shown = Time.unscaledTime;
            }

            void Update()
            {
                if (finish == null) return;
                if (from < 0)
                {
                    if (ready != null && !ready()) return;
                    from = Time.unscaledTime;
                }
                float end = Mathf.Max(from + seconds, shown + InTime + 0.3f) - OutTime;
                if (Time.unscaledTime < end) return;
                var f = finish; finish = null;
                f();
            }
        }

        // to x = 0 (its place), easing out
        static Tween SlideX(RectTransform rt, float t) =>
            DOTween.To(() => rt.anchoredPosition.x, x => rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y), 0, t).SetEase(Ease.OutCubic);

        static RectTransform Card(Transform parent, string name, Vector2 pos, string stone, string nick, OnlineApi.PlayerStats stats)
        {
            var card = Ui.Rect(parent, name);
            card.sizeDelta = new Vector2(CardW, CardH);
            card.anchoredPosition = pos;
            var bg = card.gameObject.AddComponent<Image>();
            bg.sprite = Resources.Load<Sprite>("Online/vs_card"); bg.type = Image.Type.Sliced; bg.raycastTarget = false;

            var st = Ui.Rect(card, "Stone").gameObject.AddComponent<Image>();
            st.sprite = Resources.Load<Sprite>("Online/" + stone); st.preserveAspect = true; st.raycastTarget = false;
            var srt = st.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0, 0.5f);
            srt.sizeDelta = new Vector2(StoneSize, StoneSize);
            srt.anchoredPosition = new Vector2(70 + StoneSize / 2, 0);

            float textX = 70 + StoneSize + 40;
            var n = Text(card, "Name", string.IsNullOrEmpty(nick) ? "?" : nick, 64, Ink, true);
            Place(n.rectTransform, textX, 34, CardW - textX - 60, 100);
            n.enableAutoSizing = true; n.fontSizeMin = 36; n.fontSizeMax = 64;
            string rec = stats != null ? Loc.T("hud.record", ("w", stats.W), ("l", stats.L), ("d", stats.D)) : "-";
            var r = Text(card, "Record", rec, 44, Value, false);
            Place(r.rectTransform, textX, -54, CardW - textX - 60, 72);
            return card;
        }

        static TextMeshProUGUI Text(Transform parent, string name, string text, int size, Color color, bool display)
        {
            var t = Ui.Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = display ? Ui.Display : Ui.Body;
            t.text = text; t.fontSize = size; t.color = color;
            t.alignment = TextAlignmentOptions.Left;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
        }
    }
}
