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
    /// It ends when <c>endAt</c> says (a time on the clock <c>now</c> reads, shared by both devices): online, the given
    /// time after the later of the two panels appeared, so both go together however long each device took.
    /// While <c>connecting</c> says so (the two devices are still being connected), a line under the cards says that.
    /// The art is in Resources/Online (Codex).
    /// </summary>
    public static class VsPanel
    {
        const float CardW = 860, CardH = 300, CardY = 420, StoneSize = 190;
        static readonly Color Ink = new Color32(0x4a, 0x2a, 0x12, 0xff), Value = new Color32(0x86, 0x61, 0x3d, 0xff);

        const float InTime = 0.55f, OutTime = 0.25f;

        /// <summary>Shows the panel (<paramref name="shown"/> as it appears); it fades out at <paramref name="endAt"/>
        /// (null while not known yet). Returns it, to be destroyed if the match is called off meanwhile.</summary>
        public static GameObject Show(Transform parent, int sortingOrder, Func<double> now, Func<double?> endAt, Func<bool> connecting, Action shown, Action done)
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
            var note = Text(go.transform, "Connecting", Loc.T("online.connecting"), 44, Color.white, false);
            note.alignment = TextAlignmentOptions.Center;
            note.rectTransform.sizeDelta = new Vector2(CardW, 80);
            note.rectTransform.anchoredPosition = new Vector2(0, -CardY - CardH / 2 - 90);

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
            hold.Records(top.Find("Record").GetComponent<TextMeshProUGUI>(), bottom.Find("Record").GetComponent<TextMeshProUGUI>());
            hold.Connecting(note, connecting);
            shown?.Invoke();
            hold.Begin(now, endAt, () =>
                DOTween.To(() => group.alpha, a => group.alpha = a, 0, OutTime).SetUpdate(true).SetLink(go)
                    .OnComplete(() => { UnityEngine.Object.Destroy(go); done?.Invoke(); }));
            return go;
        }

        // holds until the end time is known and near (the fade-out ends on it), and at least past the entrance
        sealed class Hold : MonoBehaviour
        {
            Func<double> now;
            Func<double?> endAt;
            float shown;
            Action finish;

            // the records come from the server while the panel is up (the match does not wait for them)
            TextMeshProUGUI oppRecord, myRecord;
            bool oppDone, myDone;

            // "connecting", pulsing, until the two devices are connected
            TextMeshProUGUI note;
            Func<bool> connecting;

            public void Connecting(TextMeshProUGUI text, Func<bool> busy)
            {
                note = text; connecting = busy;
                note.gameObject.SetActive(connecting != null && connecting());
            }

            public void Records(TextMeshProUGUI opponent, TextMeshProUGUI mine)
            {
                oppRecord = opponent; myRecord = mine;
                oppDone = OnlineLobby.OpponentStats != null; myDone = OnlineLobby.MyStats != null;
            }

            public void Begin(Func<double> clock, Func<double?> end, Action onDone)
            {
                now = clock; endAt = end; finish = onDone; shown = Time.unscaledTime;
            }

            void Update()
            {
                if (!oppDone && OnlineLobby.OpponentStats != null) { oppDone = true; oppRecord.text = RecordText(OnlineLobby.OpponentStats); }
                if (!myDone && OnlineLobby.MyStats != null) { myDone = true; myRecord.text = RecordText(OnlineLobby.MyStats); }
                if (note && note.gameObject.activeSelf)
                {
                    if (connecting == null || !connecting()) note.gameObject.SetActive(false);
                    else note.alpha = 0.55f + 0.45f * Mathf.Cos(Time.unscaledTime * 4);
                }
                if (finish == null || Time.unscaledTime < shown + InTime + 0.3f) return;
                var end = endAt();
                if (!end.HasValue || now() < end.Value - OutTime) return;
                var f = finish; finish = null;
                f();
            }
        }

        // to x = 0 (its place), easing out
        static Tween SlideX(RectTransform rt, float t) =>
            DOTween.To(() => rt.anchoredPosition.x, x => rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y), 0, t).SetEase(Ease.OutCubic);

        static string RecordText(OnlineApi.PlayerStats stats) =>
            Loc.T("hud.record", ("w", stats?.W ?? 0), ("l", stats?.L ?? 0), ("d", stats?.D ?? 0));   // none: all zero

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
            var r = Text(card, "Record", RecordText(stats), 44, Value, false);
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
