using System;
using System.Collections.Generic;
using DG.Tweening;
using FlickCurling.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    public sealed class CardButton
    {
        public string Label;
        public Action OnClick;
        /// <summary>Pressing leaves the card up (the caller hides it, and can change the button with
        /// <see cref="Hud.SetCardButton"/>): online, a card waits for both players.</summary>
        public bool KeepOpen;
        public bool Interactable = true;
        public CardButton(string label, Action onClick) { Label = label; OnClick = onClick; }
    }

    public sealed class CardSpec
    {
        public string Eyebrow = "", Title = "", Head = "", Body = "";
        public bool Soft;        // a lighter dimming, so the sheet behind still reads
        public float Countdown;  // seconds until the first button presses itself (its fill drains to empty); 0 = waits
        public List<CardButton> Buttons = new List<CardButton>();
    }

    /// <summary>
    /// The overlay canvas: top HUD row, score table, toast, cards, corner buttons and world labels.
    /// Fixed text is bound to string-table keys and refreshed by <see cref="Relocalize"/>.
    /// </summary>
    public sealed class Hud
    {
        public readonly Canvas Canvas;
        readonly CanvasScaler scaler;
        readonly RectTransform root, scorePanel, scoreTable, toastLayer, cardOv, cardRt, labelLayer, miniFrame;
        readonly TextMeshProUGUI scorePill, cardEyebrow, cardTitle, cardHead, cardBody;
        readonly Image cardOvImg;
        readonly RectTransform cardActions;
        readonly Image gear;
        readonly LayoutElement pillLe;
        // safe-area root for the HUD, and the two side columns used on wide screens
        readonly RectTransform safeRoot, safeTop, leftCol, pillRt;
        bool sideMode, hudShown;
        float insL, insT, insR, insB;
        readonly List<(TextMeshProUGUI t, string key)> bound = new List<(TextMeshProUGUI, string)>();
        // toasts: a stack sitting just above the fuse on the min line, the newest at the bottom
        sealed class ToastItem
        {
            public RectTransform Rt; public CanvasGroup Cg; public Tween Show, Slide;
            public float Until, Off, Target = float.NaN; public bool Turn, Leaving;
        }
        readonly List<ToastItem> toasts = new List<ToastItem>();
        float toastY = -1, viewW, viewH;
        int cardSeq;
        // the card countdown: the first button's fill drains right to left, then the button presses itself
        float cardDeadline, cardSpan;
        Image cardFill;
        Button cardFirst;

        public Action OnSettings, OnUiClick;
        const float GearSize = 35, GearInset = 15;   // css px

        public Hud(Transform parent)
        {
            var go = new GameObject("HUD", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            go.AddComponent<GraphicRaycaster>();
            root = (RectTransform)go.transform;

            // light vignette over the table and sheet edges
            var vig = Ui.Rect(root, "Vignette").gameObject.AddComponent<Image>();
            vig.sprite = Ui.Vignette; vig.raycastTarget = false; Ui.Stretch(vig.rectTransform);
            labelLayer = Ui.Rect(root, "Labels"); Ui.Stretch(labelLayer);
            miniFrame = Ui.Rect(root, "MinimapFrame");
            var mf = miniFrame.gameObject.AddComponent<Image>();
            mf.sprite = Ui.RoundStroke; mf.type = Image.Type.Sliced; mf.pixelsPerUnitMultiplier = 16f / 6; mf.color = Palette.Rgba(239, 226, 200, .25f); mf.raycastTarget = false;
            miniFrame.gameObject.SetActive(false);
            safeRoot = Ui.Rect(root, "Safe"); Ui.Stretch(safeRoot);

            // ---- HUD pinned to the safe-area edges: score pill in the top-left corner (turn info is printed on the sheet) ----
            leftCol = Column("LeftColumn", 0);

            var pill = Ui.Panel(leftCol, "ScorePill", Palette.Glass, Palette.Line, 10);
            Ui.HStack(pill.gameObject, 9, 9, 5, 5, 0, TextAnchor.MiddleLeft);
            scorePill = Ui.Label(pill.transform, "", 13, Palette.Text);
            pillLe = pill.gameObject.AddComponent<LayoutElement>();
            pillRt = pill.rectTransform;
            var pb = pill.gameObject.AddComponent<Button>(); pb.targetGraphic = pill; pb.transition = Selectable.Transition.None;
            pb.onClick.AddListener(() => { bool open = !scorePanel.gameObject.activeSelf; ClosePopovers(); scorePanel.gameObject.SetActive(open); OnUiClick?.Invoke(); });

            // opaque: without the prototype's backdrop blur, a see-through table mixes with the board labels under it
            scorePanel = Ui.Panel(safeRoot, "ScorePanel", Palette.Panel, Palette.Line, 10).rectTransform;
            scorePanel.anchorMin = scorePanel.anchorMax = new Vector2(0, 1); scorePanel.pivot = new Vector2(0, 1);   // placed in Tick
            Ui.VStack(scorePanel.gameObject, 6, 6, 8, 8, 0);
            Ui.Fit(scorePanel.gameObject, true, true);
            scoreTable = Ui.Rect(scorePanel, "Table");
            Ui.VStack(scoreTable.gameObject, 0, 0, 0, 0, 0);
            scorePanel.gameObject.SetActive(false);

            // ---- toasts ----
            toastLayer = Ui.Rect(root, "Toasts"); Ui.Stretch(toastLayer);

            // ---- card overlay ----
            cardOvImg = Ui.Rect(root, "CardOverlay").gameObject.AddComponent<Image>();
            cardOv = cardOvImg.rectTransform; Ui.Stretch(cardOv);
            cardOvImg.color = Palette.Rgba(20, 13, 8, .62f);
            cardRt = Ui.Panel(cardOv, "Card", Palette.Panel, Palette.Line, 12).rectTransform;
            Ui.VStack(cardRt.gameObject, 20, 20, 20, 20, 12);
            Ui.Fit(cardRt.gameObject, false, true);
            cardEyebrow = Ui.Label(cardRt, "", 14, Palette.Muted);
            cardTitle = FitOneLine(Ui.Label(cardRt, "", 26, Palette.Text, true), 18);
            cardHead = FitOneLine(Ui.Label(cardRt, "", 34, Palette.Brass, true), 22);
            cardBody = Ui.Label(cardRt, "", 14, Palette.Text);
            cardBody.lineSpacing = 12;
            cardActions = Ui.Rect(cardRt, "Actions");
            cardActions.gameObject.AddComponent<FlowLayout>().SpacingX = 8;
            cardGroup = cardOv.gameObject.AddComponent<CanvasGroup>();
            cardOv.gameObject.SetActive(false);

            // ---- waiting overlay: a dim and one pulsing line in the middle (over the cards, under the gear) ----
            var waitImg = Ui.Rect(root, "WaitOverlay").gameObject.AddComponent<Image>();
            waitOv = waitImg.rectTransform; Ui.Stretch(waitOv);
            waitImg.color = Palette.Rgba(20, 13, 8, .62f);   // (it takes the touches: nothing under it is pressed meanwhile)
            waitText = Ui.Label(waitOv, "", 20, Palette.Brass, true, TextAnchor.MiddleCenter);
            waitText.raycastTarget = false;
            Ui.Stretch(waitText.rectTransform);
            waitOv.gameObject.SetActive(false);

            // ---- top right: the settings gear (the main menu's icon), above the overlays ----
            // its own safe-area root, created last so the gear draws above the cards
            safeTop = Ui.Rect(root, "SafeTop"); Ui.Stretch(safeTop);
            gear = Ui.Rect(safeTop, "Settings").gameObject.AddComponent<Image>();
            var grt = gear.rectTransform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(1, 1); grt.anchoredPosition = new Vector2(-GearInset, -GearInset);
            grt.sizeDelta = new Vector2(GearSize, GearSize);
            gear.preserveAspect = true;
            var gb = gear.gameObject.AddComponent<Button>();
            var gc = gb.colors; gc.highlightedColor = new Color(1.06f, 1.06f, 1.06f); gc.pressedColor = new Color(.78f, .78f, .78f); gb.colors = gc;
            gb.onClick.AddListener(() => { ClosePopovers(); OnSettings?.Invoke(); });

            Relocalize();
        }

        TextMeshProUGUI Bind(TextMeshProUGUI t, string key) { bound.Add((t, key)); return t; }


        // headings stay on one line and shrink for longer translations
        static TextMeshProUGUI FitOneLine(TextMeshProUGUI t, int min)
        {
            Ui.NoWrap(t);
            t.enableAutoSizing = true; t.fontSizeMax = t.fontSize; t.fontSizeMin = min;
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = t.fontSize * 1.3f;
            return t;
        }

        /// <summary>Re-reads every fixed text from the string table (after a language change).</summary>
        public void Relocalize()
        {
            foreach (var (t, key) in bound) t.text = Loc.T(key);
            pillTextW = -1;
            lastTable = null;
        }

        /// <summary>The settings gear's art (the main menu's icon).</summary>
        public Sprite SettingsIcon { set => gear.sprite = value; }

        // ---------------- per-frame ----------------

        // only what changed is set: a layout property set to the same value still makes the canvas rebuild
        static void SetSize(RectTransform rt, Vector2 v) { if (rt.sizeDelta != v) rt.sizeDelta = v; }
        static void SetPos(RectTransform rt, Vector2 v) { if (rt.anchoredPosition != v) rt.anchoredPosition = v; }
        static void SetScale(RectTransform rt, Vector3 v) { if (rt.localScale != v) rt.localScale = v; }

        /// <summary>Dims the screen with <paramref name="text"/> in the middle, pulsing, until <see cref="HideWaiting"/>.</summary>
        public void ShowWaiting(string text)
        {
            if (waitText.text != text) waitText.text = text;
            waitOv.gameObject.SetActive(true);
        }

        public void HideWaiting() { if (waitOv.gameObject.activeSelf) waitOv.gameObject.SetActive(false); }

        readonly RectTransform waitOv;
        readonly TextMeshProUGUI waitText;

        public void Tick(float cssPx, float cw, float ch)
        {
            if (waitOv.gameObject.activeSelf) waitText.alpha = 0.55f + 0.45f * Mathf.Cos(Time.time * 4);
            if (scaler.scaleFactor != cssPx) scaler.scaleFactor = cssPx;
            // the safe area: everything below is laid out inside it
            float sw = cw - insL - insR, sh = ch - insT - insB;
            if (!sideMode)
            {
                // top-left corner: the pill as wide as its text (measured when the text changes), wrapping only if the
                // row is narrower
                pillRowW = sw - 20;
                FitPill();
            }
            float cardW = CardWidth(sw);
            SetSize(cardRt, new Vector2(cardW, cardRt.sizeDelta.y));
            // a card taller than the screen shrinks to fit rather than running off it
            float h = cardRt.rect.height, room = sh - 32;
            SetScale(cardRt, Vector3.one * (h > room && h > 0 ? room / h : 1) * cardPop);
            viewW = cw; viewH = ch;
            TickToasts();
            TickCard();
            var sp = scorePanel;
            float under = leftCol.rect.height;
            SetPos(sp, new Vector2(10, -(10 + under + 4)));
        }

        float pillTextW = -1, pillRowW = -1;   // the pill's text width (measured when it changes) and the row's room

        // the pill as wide as its text, wrapping only if the row is narrower; done as soon as the text changes, so the
        // layout never runs with new text in the old width (it would wrap for a frame, the row grow, and the camera,
        // which keeps the far end clear of the row, jump with it)
        void FitPill()
        {
            if (sideMode || pillRowW < 0) return;
            if (pillTextW < 0) pillTextW = scorePill.GetPreferredValues(scorePill.text).x;
            SetSize(leftCol, new Vector2(Mathf.Min(pillTextW + 18 + 2, pillRowW), leftCol.sizeDelta.y));
        }

        public bool HudVisible { set { hudShown = value; ApplyHudVisibility(); if (!value) scorePanel.gameObject.SetActive(false); } }

        /// <summary>Bottom edge of the top HUD row plus a margin, in css px (the camera keeps the far end clear of it).</summary>
        public float HudBottom => insT + (hudShown && !sideMode ? 10 + leftCol.rect.height + 8 : 18);

        /// <summary>Where the right edge is free from (css px from the top): under the settings gear.</summary>
        public float RightColumnBottom => insT + GearInset + GearSize + 8;

        public void SetInsets(float l, float t, float r, float b)
        {
            insL = l; insT = t; insR = r; insB = b;
            foreach (var s in new[] { safeRoot, safeTop }) { s.offsetMin = new Vector2(l, b); s.offsetMax = new Vector2(-r, -t); }
        }

        RectTransform Column(string name, int side)
        {
            var col = Ui.Rect(safeRoot, name);
            col.anchorMin = col.anchorMax = col.pivot = new Vector2(side, 1);
            col.anchoredPosition = new Vector2(side == 0 ? 10 : -10, -10);
            Ui.VStack(col.gameObject, 0, 0, 0, 0, 6, side == 0 ? TextAnchor.UpperLeft : TextAnchor.UpperRight);
            Ui.Fit(col.gameObject, false, true);
            col.gameObject.SetActive(false);
            return col;
        }

        /// <summary>
        /// Wide screens: the score column gets a fixed width (colW, css px) beside the sheet, with no top row reserved
        /// above it. Narrow screens size the pill to its text and reserve the row.
        /// </summary>
        public bool SideMode => sideMode;

        public void SetSideLayout(bool side, float colW)
        {
            if (side)
            {
                leftCol.sizeDelta = new Vector2(colW, leftCol.sizeDelta.y);

            }
            sideMode = side;
        }

        void ApplyHudVisibility()
        {
            leftCol.gameObject.SetActive(hudShown);

        }

        public void ClosePopovers()
        {
            scorePanel.gameObject.SetActive(false);
        }

        public void SetScorePill(string rich) { if (scorePill.text != rich) { scorePill.text = rich; pillTextW = -1; FitPill(); } }

        string lastTable;

        /// <summary>The end-by-end score table. cells[row][col]; row 0 is the header. curCol highlights the current end.</summary>
        public void SetScoreTable(List<string[]> cells, int curCol)
        {
            string key = curCol + "|" + string.Join("/", cells.ConvertAll(r => string.Join(",", r)));
            if (key == lastTable) return;
            lastTable = key;
            for (int i = scoreTable.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(scoreTable.GetChild(i).gameObject);
            // the name column fits its widest entry (side icon, "(AI)", hammer badge)
            float nameW = 0;
            foreach (var row0 in cells) nameW = Mathf.Max(nameW, scorePill.GetPreferredValues(row0[0], 1000, 100).x * 13 / scorePill.fontSize + 16);
            for (int r = 0; r < cells.Count; r++)
            {
                var row = Ui.Rect(scoreTable, "Row");
                Ui.HStack(row.gameObject, 0, 0, 0, 0, 0, TextAnchor.MiddleLeft);
                for (int c = 0; c < cells[r].Length; c++)
                {
                    bool last = c == cells[r].Length - 1;
                    var cell = Ui.Rect(row, "Cell");
                    if (c == curCol)
                    {
                        var bg = cell.gameObject.AddComponent<Image>(); bg.color = Palette.Panel2; bg.raycastTarget = false;
                    }
                    Ui.HStack(cell.gameObject, 7, c == 0 ? 4 : 7, 3, 3, 0, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
                    var le = cell.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = c == 0 ? nameW : (last ? 40 : 32);
                    int size = r == 0 ? 11 : (last ? 17 : 13);
                    var col = r == 0 ? Palette.Muted : (last ? Palette.Brass : Palette.Text);
                    Ui.NoWrap(Ui.Label(cell, cells[r][c], size, col, last && r > 0, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter));
                    if (c > 0)
                    {
                        var line = Ui.Rect(cell, "Sep");
                        line.anchorMin = new Vector2(0, 0); line.anchorMax = new Vector2(0, 1); line.pivot = new Vector2(0, .5f);
                        line.sizeDelta = new Vector2(1, 0); line.anchoredPosition = Vector2.zero;
                        var li = line.gameObject.AddComponent<Image>(); li.color = Palette.Line; li.raycastTarget = false;
                        line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    }
                }
            }
        }

        // ---------------- toast ----------------

        /// <summary>Where the stack sits: the css y (from the top) just above the min line, set by the game every frame.</summary>
        public void SetToastAnchor(float cssY) => toastY = cssY;

        /// <param name="icon">a picture over the title (an item taken)</param>
        public void Toast(string title, string body = null, float ms = 2400, int titleSize = 17, Sprite icon = null) =>
            Push(MakeToast(title, body, false, titleSize, icon), ms);

        /// <summary>True until every toast has finished fading out.</summary>
        public bool ToastsUp => toasts.Count > 0;

        /// <summary>Whose turn, larger; a new turn replaces the last one.</summary>
        public void TurnToast(string text, string note = null)
        {
            foreach (var t in toasts) if (t.Turn) Leave(t);
            Push(MakeToast(text, note, true, 24), note != null ? 1500 : 1050);
        }

        // ---------------- the turn order coin ----------------

        const float CoinViewPx = 340, CoinHold = 0.9f;

        /// <summary>
        /// The turn order coin tossed in 3D over a light dimming (<see cref="CoinFlip"/>), landing on
        /// <paramref name="shown"/>'s stone (the side as shown on this device): <paramref name="hit"/> at each bounce,
        /// <paramref name="landed"/> once it lies still, <paramref name="done"/> once it has gone.
        /// </summary>
        public void CoinToss(Team shown, int seed, Action<float> hit, Action landed, Action done)
        {
            var dim = Ui.Rect(root, "CoinToss"); Ui.Stretch(dim);
            dim.SetSiblingIndex(cardOv.GetSiblingIndex());   // over the sheet and the toasts, under the cards
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = Palette.Rgba(20, 13, 8, 0);        // (it takes the touches while the coin is up)
            var group = dim.gameObject.AddComponent<CanvasGroup>();
            var view = Ui.Rect(dim, "Coin");
            view.anchorMin = view.anchorMax = new Vector2(.5f, .5f);
            view.sizeDelta = Vector2.one * Mathf.Min(CoinViewPx, viewW - 32);
            var raw = view.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            DOTween.To(() => dimImg.color.a, a => dimImg.color = Palette.WithA(dimImg.color, a), .4f, .25f).SetTarget(dim);
            CoinFlip.Toss(raw, shown, seed, hit, () =>
            {
                landed?.Invoke();
                DOTween.Sequence().SetTarget(dim)
                    .AppendInterval(CoinHold)
                    .Append(DOTween.To(() => group.alpha, a => group.alpha = a, 0, .25f))
                    .OnComplete(() => { UnityEngine.Object.Destroy(dim.gameObject); done?.Invoke(); });
            });
        }

        public void ClearToasts()
        {
            foreach (var t in toasts) { t.Show?.Kill(); t.Slide?.Kill(); UnityEngine.Object.Destroy(t.Rt.gameObject); }
            toasts.Clear();
        }

        const float ToastBgAlpha = 0.72f;

        const float ToastIcon = 46;

        ToastItem MakeToast(string title, string body, bool turn, int titleSize, Sprite icon = null)
        {
            // see-through backing so the sheet shows behind; the text and icons stay solid (the fades scale both together)
            var img = Ui.Panel(toastLayer, turn ? "TurnToast" : "Toast", Palette.WithA(Palette.Panel, ToastBgAlpha), Palette.Line, turn ? 10 : 8);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 0);
            if (turn) Ui.VStack(rt.gameObject, 22, 22, 10, 10, 0, TextAnchor.MiddleCenter);
            else Ui.VStack(rt.gameObject, 14, 14, 9, 9, 2, TextAnchor.UpperCenter);
            Ui.Fit(rt.gameObject, false, true);
            if (icon)
            {
                var pic = Ui.Rect(rt, "Icon").gameObject.AddComponent<Image>();
                pic.sprite = icon; pic.preserveAspect = true; pic.raycastTarget = false;
                var le = pic.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = ToastIcon;
            }
            var tt = Ui.Label(rt, title, titleSize, turn ? Palette.Text : Palette.Brass, true, TextAnchor.MiddleCenter);
            tt.raycastTarget = false;
            float want = tt.GetPreferredValues(title).x;
            if (!string.IsNullOrEmpty(body))
            {
                // under the turn line: smaller, in the brass of a toast title
                var tb = Ui.Label(rt, body, turn ? 15 : 13, turn ? Palette.Brass : Palette.Text, turn, TextAnchor.MiddleCenter);
                tb.raycastTarget = false;
                want = Mathf.Max(want, tb.GetPreferredValues(body).x);
            }
            float pad = turn ? 44 : 28;
            rt.sizeDelta = new Vector2(Mathf.Min(Mathf.Min(420, viewW - 32), want + pad + 2), rt.sizeDelta.y);
            var cg = rt.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false; cg.interactable = false; cg.alpha = 0;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            return new ToastItem { Rt = rt, Cg = cg, Turn = turn };
        }

        void Push(ToastItem it, float ms)
        {
            it.Until = Time.time + ms / 1000f;
            toasts.Add(it);
            // no more than four up at once: the oldest goes first
            var live = toasts.FindAll(t => !t.Leaving);
            for (int i = 0; i < live.Count - 4; i++) Leave(live[i]);
            it.Rt.localScale = Vector3.one * 0.85f;
            it.Show = DOTween.Sequence().SetLink(it.Rt.gameObject)
                .Append(DOTween.To(() => it.Cg.alpha, v => it.Cg.alpha = v, 1, 0.18f))
                .Join(it.Rt.DOScale(1, 0.25f).SetEase(Ease.OutBack));
            Restack();
        }

        // fades out where it is; the ones above it close the gap
        void Leave(ToastItem it)
        {
            if (it.Leaving) return;
            it.Leaving = true;
            it.Show?.Kill();
            it.Show = DOTween.To(() => it.Cg.alpha, v => it.Cg.alpha = v, 0, 0.3f).SetLink(it.Rt.gameObject)
                .OnComplete(() => { it.Slide?.Kill(); toasts.Remove(it); UnityEngine.Object.Destroy(it.Rt.gameObject); });
            Restack();
        }

        // the newest at the bottom, each older one above the ones after it; they slide there when the stack changes
        void Restack()
        {
            float off = 0;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                if (t.Leaving) continue;
                if (t.Target != off)
                {
                    bool fresh = float.IsNaN(t.Target);
                    t.Target = off;
                    t.Slide?.Kill();
                    if (fresh) t.Off = off;
                    else t.Slide = DOTween.To(() => t.Off, v => t.Off = v, off, 0.28f).SetEase(Ease.OutCubic).SetLink(t.Rt.gameObject);
                }
                off += t.Rt.rect.height + 8;
            }
        }

        /// <summary>Where the toasts stand (css px from the top: the bottom of their stack), and how tall the stack is now.</summary>
        public float ToastY => Mathf.Clamp(toastY < 0 ? viewH * 0.6f : toastY, insT + 140, viewH - insB - 12);
        public float ToastStackHeight
        {
            get
            {
                float h = 0;
                foreach (var t in toasts) if (!t.Leaving) h += t.Rt.rect.height + 8;
                return h;
            }
        }

        void TickToasts()
        {
            if (toasts.Count == 0) return;
            float now = Time.time;
            foreach (var t in toasts.ToArray()) if (!t.Leaving && now > t.Until) Leave(t);
            // just above the fuse; kept on screen when the camera has taken the min line off it
            float y = ToastY;
            float x = (insL - insR) / 2;
            foreach (var t in toasts) t.Rt.anchoredPosition = new Vector2(x, -y + t.Off);
        }

        // ---------------- card ----------------

        // up as far as the game is concerned (a card fading out is already gone)
        public bool CardVisible => cardShown;

        /// <summary>A full-screen layer (css px) over the sheet, its labels and the score, under the toasts, cards and the
        /// settings gear.</summary>
        public RectTransform AddLayer(string name)
        {
            var rt = Ui.Rect(root, name);
            Ui.Stretch(rt);
            rt.SetSiblingIndex(toastLayer.GetSiblingIndex());
            return rt;
        }
        bool cardShown;
        CanvasGroup cardGroup;
        float cardPop = 1;   // the entrance's grow, on top of the fit-to-screen scale
        public int CardSeq => cardSeq;

        public int ShowCard(CardSpec c, Action onAnyClick)
        {
            cardSeq++;
            cardSpan = c.Countdown;
            cardDeadline = c.Countdown > 0 ? Time.time + c.Countdown : 0;
            FillCard(c, onAnyClick);
            cardShown = true;
            cardOv.gameObject.SetActive(true);
            // the main menu popup's entrance: the dim and card fade in while the card grows a little past its size (real
            // time: a card can come up while the match stands still)
            DOTween.Kill(cardGroup); DOTween.Kill(cardRt);
            cardGroup.alpha = 0; cardGroup.interactable = cardGroup.blocksRaycasts = true;
            cardPop = 0.9f;
            DOTween.To(() => cardGroup.alpha, a => cardGroup.alpha = a, 1, 0.15f).SetTarget(cardGroup).SetUpdate(true);
            DOTween.To(() => cardPop, v => cardPop = v, 1, 0.2f).SetEase(Ease.OutBack).SetTarget(cardRt).SetUpdate(true);
            return cardSeq;
        }

        /// <summary>Re-fills the visible card in place; its countdown carries on where it was.</summary>
        public void ReplaceCard(CardSpec c, Action onAnyClick) { if (CardVisible) FillCard(c, onAnyClick); }

        const float CardButtonH = 46;

        // cards: at most 420 wide, with a side margin of 6% of the (safe) width but never under 24
        static float CardWidth(float safeW) => Mathf.Min(420, safeW - 2 * Mathf.Max(24, safeW * 0.06f));

        void FillCard(CardSpec c, Action onAnyClick)
        {
            cardEyebrow.text = c.Eyebrow;
            cardTitle.text = c.Title;
            cardHead.text = c.Head; cardHead.gameObject.SetActive(!string.IsNullOrEmpty(c.Head));
            // the big line: a short value (a score) large; a sentence smaller than the title, shrinking to stay on one line
            cardHead.enableAutoSizing = false; cardHead.fontSize = 34;   // measure at full size
            bool longHead = cardHead.GetPreferredValues(c.Head, float.PositiveInfinity, 100).x * 22f / 34f > CardWidth(viewW - insL - insR) - 40;
            int headMax = longHead ? 18 : 34;
            cardHead.fontSize = cardHead.fontSizeMax = headMax; cardHead.fontSizeMin = longHead ? 12 : 22;
            cardHead.enableAutoSizing = true;
            cardHead.GetComponent<LayoutElement>().preferredHeight = headMax * 1.3f;
            cardBody.text = c.Body; cardBody.gameObject.SetActive(!string.IsNullOrEmpty(c.Body));
            for (int i = cardActions.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(cardActions.GetChild(i).gameObject);
            cardFill = null; cardFirst = null;
            cardButtons.Clear();
            for (int i = 0; i < c.Buttons.Count; i++)
            {
                var b = c.Buttons[i];
                Button btn = null;
                btn = Ui.Button(cardActions, b.Label, i == 0, () =>
                {
                    cardDeadline = 0;
                    if (cardFill) { cardFill.gameObject.SetActive(false); cardFill = null; }
                    if (!b.KeepOpen) HideCard();
                    onAnyClick?.Invoke();
                    b.OnClick();
                }, 14, "CardBtn" + i);
                btn.interactable = b.Interactable;
                cardButtons.Add(btn);
                if (i == 0) { cardFirst = btn; if (cardDeadline > 0 && b.Interactable) cardFill = CountdownFill(btn); }
                // side by side buttons share one height (the primary's larger text would make it taller)
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = CardButtonH;
            }
            // every card sits in the middle of the screen
            cardOvImg.sprite = null; cardOvImg.color = Palette.Rgba(20, 13, 8, c.Soft ? .35f : .62f);
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f); cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = new Vector2((insL - insR) / 2, (insB - insT) / 2);
        }

        readonly List<Button> cardButtons = new List<Button>();

        /// <summary>Changes a button of the card that is up (its text, whether it can be pressed).</summary>
        public void SetCardButton(int index, string label, bool interactable)
        {
            if (!cardShown || index < 0 || index >= cardButtons.Count || !cardButtons[index]) return;
            var b = cardButtons[index];
            b.interactable = interactable;
            var t = Ui.ButtonText(b);
            if (t && t.text != label) t.text = label;
        }

        // gone for the game at once; on screen it fades out like the main menu popup
        public void HideCard()
        {
            cardDeadline = 0;
            if (!cardShown) return;
            cardShown = false;
            DOTween.Kill(cardGroup); DOTween.Kill(cardRt);
            cardGroup.interactable = cardGroup.blocksRaycasts = false;
            DOTween.To(() => cardGroup.alpha, a => cardGroup.alpha = a, 0, 0.12f).SetTarget(cardGroup).SetUpdate(true)
                .OnComplete(() => cardOv.gameObject.SetActive(false));
        }

        // a lighter band inside the button, clipped to its rounded shape, that drains from the right toward the left
        static Image CountdownFill(Button btn)
        {
            btn.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var rt = Ui.Rect(btn.transform, "Countdown");
            Ui.Stretch(rt);
            rt.SetAsFirstSibling();   // under the label
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Ui.White; img.raycastTarget = false;
            img.color = Palette.Rgba(255, 240, 205, .5f);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1;
            return img;
        }

        void TickCard()
        {
            if (cardDeadline <= 0 || !CardVisible) return;
            float left = cardDeadline - Time.time;
            if (cardFill) cardFill.fillAmount = Mathf.Clamp01(left / cardSpan);
            if (left <= 0 && cardFirst) cardFirst.onClick.Invoke();
        }

        // ---------------- minimap frame ----------------

        public void MiniFrame(Rect? r)
        {
            miniFrame.gameObject.SetActive(r.HasValue);
            if (r.HasValue) Ui.TopLeft(miniFrame, r.Value.x - 3, r.Value.y - 3, r.Value.width + 6, r.Value.height + 6);
        }

        // ---------------- world labels (immediate mode) ----------------

        // a pooled label and what was last given to it: every property is only set (and the text only measured) when it
        // changes, so a label that stays the same costs nothing from frame to frame
        sealed class WorldLabel
        {
            public TextMeshProUGUI T;
            public RectTransform Rt;
            public string Text;
            public int Size = -1;
            public float Width = -1, Measured, Height;
            public Vector2 Pivot = new Vector2(-1, -1), Pos = new Vector2(float.NaN, float.NaN), SizeDelta = new Vector2(-1, -1);
            public float Rot = float.NaN, Scale = float.NaN;
            public bool Wrap;
        }

        readonly List<WorldLabel> pool = new List<WorldLabel>();
        int used;

        public void BeginLabels() { used = 0; }

        WorldLabel Next(int size, Color color)
        {
            WorldLabel l;
            if (used < pool.Count) l = pool[used];
            else
            {
                var t = Ui.NoWrap(Ui.Label(labelLayer, "", size, color));
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0, 1);
                l = new WorldLabel { T = t, Rt = t.rectTransform };
                pool.Add(l);
            }
            used++;
            if (!l.T.gameObject.activeSelf) l.T.gameObject.SetActive(true);
            return l;
        }

        static void Style(WorldLabel l, string text, int size, Color color, int align, bool display, bool stroke, bool wrap)
        {
            var t = l.T;
            var font = display ? Ui.Display : Ui.Body;
            if (t.font != font) t.font = font;
            var mat = stroke ? Ui.StrokeFor(font) : font.material;
            if (t.fontSharedMaterial != mat) t.fontSharedMaterial = mat;
            if (l.Wrap != wrap) { l.Wrap = wrap; t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap; l.Width = -1; }
            if (!ReferenceEquals(l.Text, text) && l.Text != text) { l.Text = text; t.text = text; l.Width = -1; }
            if (l.Size != size) { l.Size = size; t.fontSize = size; l.Width = -1; }
            if (t.color != color) t.color = color;
            // align 2: from the top left (wrapping blocks)
            var a = align == 2 ? TextAlignmentOptions.TopLeft : align < 0 ? TextAlignmentOptions.Left : align > 0 ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
            if (t.alignment != a) t.alignment = a;
        }

        static void Place(WorldLabel l, Vector2 pivot, Vector2 sizeDelta, Vector2 pos, float rot, float scale)
        {
            var rt = l.Rt;
            if (l.Pivot != pivot) { l.Pivot = pivot; rt.pivot = pivot; }
            if (l.SizeDelta != sizeDelta) { l.SizeDelta = sizeDelta; rt.sizeDelta = sizeDelta; }
            if (l.Pos != pos) { l.Pos = pos; rt.anchoredPosition = pos; }
            if (l.Rot != rot) { l.Rot = rot; rt.localEulerAngles = new Vector3(0, 0, rot); }
            if (l.Scale != scale) { l.Scale = scale; rt.localScale = Vector3.one * scale; }
        }

        /// <summary>A text at a css-px point (top-left origin). align: -1 left, 0 centre, 1 right of the point. rot in degrees (counter-clockwise).</summary>
        public void Label(string text, float x, float y, int size, Color color, int align = 0, bool display = false, bool stroke = false, float rot = 0, float scale = 1, float maxWidth = 0)
        {
            var l = Next(size, color);
            Style(l, text, size, color, align, display, stroke, false);
            // wider than maxWidth: shrunk to fit (the text is measured again only when it changes)
            if (maxWidth > 0)
            {
                if (l.Width < 0) { l.Width = 0; l.Measured = l.T.GetPreferredValues(text, 100000, 1000).x; }
                if (l.Measured * scale > maxWidth) scale = maxWidth / l.Measured;
            }
            Place(l, new Vector2(align < 0 ? 0 : align > 0 ? 1 : 0.5f, 0.5f), new Vector2(10, size * 1.4f), new Vector2(x, -y), rot, scale);
        }

        /// <summary>
        /// A text block centred on x with its first line's top at yTop (css px), at most <paramref name="maxWidth"/> wide:
        /// past that it wraps, the first line staying put and the rest stacking under it, left-aligned (with a hanging
        /// indent, if the text sets one). Returns the height of its lines in css px. Laid out again only when it changes.
        /// </summary>
        public float LabelWrapped(string text, float x, float yTop, int size, Color color, float maxWidth, out float width, bool display = false)
        {
            var l = Next(size, color);
            Style(l, text, size, color, 2, display, false, true);
            if (l.Width != maxWidth)
            {
                l.Width = maxWidth;
                // the block as wide as the text on one line, up to the limit (a little slack, so a line that just fits
                // is not wrapped by rounding)
                float one = l.T.GetPreferredValues(text, 100000, 1000).x;
                l.Measured = Mathf.Min(one, maxWidth) + 2;
                l.SizeDelta = new Vector2(-1, -1);
                Place(l, new Vector2(0.5f, 1), new Vector2(l.Measured, size * 4), new Vector2(x, -yTop), 0, 1);
                // the lines as laid out
                l.T.ForceMeshUpdate();
                var ti = l.T.textInfo;
                l.Height = ti.lineCount > 0 ? ti.lineInfo[0].ascender - ti.lineInfo[ti.lineCount - 1].descender : 0;
            }
            Place(l, new Vector2(0.5f, 1), new Vector2(l.Measured, Mathf.Max(l.Height, 1)), new Vector2(x, -yTop), 0, 1);
            width = l.Measured;
            return l.Height;
        }

        TextMeshProUGUI measurer;

        /// <summary>How wide <paramref name="text"/> is on one line at <paramref name="size"/>, in css px.</summary>
        public float Measure(string text, int size, bool display = false)
        {
            if (!measurer)
            {
                measurer = Ui.NoWrap(Ui.Label(labelLayer, "", size, Color.clear, false, TextAnchor.MiddleCenter, "Measure"));
                measurer.rectTransform.anchoredPosition = new Vector2(-10000, 10000);
            }
            var font = display ? Ui.Display : Ui.Body;
            if (measurer.font != font) measurer.font = font;
            measurer.fontSize = size;
            return measurer.GetPreferredValues(text, 100000, 1000).x;
        }

        public void EndLabels()
        {
            for (int i = used; i < pool.Count; i++) if (pool[i].T.gameObject.activeSelf) pool[i].T.gameObject.SetActive(false);
        }

        // ---------------- input ----------------

        static readonly List<RaycastResult> hits = new List<RaycastResult>();

        public static bool IsOverUi(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var ped = new PointerEventData(es) { position = screenPos };
            hits.Clear();
            es.RaycastAll(ped, hits);
            return hits.Count > 0;
        }
    }
}
