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
        public CardButton(string label, Action onClick) { Label = label; OnClick = onClick; }
    }

    public sealed class CardSpec
    {
        public string Eyebrow = "", Title = "", Head = "", Body = "";
        public bool Soft;
        public List<CardButton> Buttons = new List<CardButton>();
    }

    /// <summary>
    /// The overlay canvas: top HUD row, score table, toast, cards, the start menu, corner buttons and world labels.
    /// Fixed text is bound to string-table keys and refreshed by <see cref="Relocalize"/>.
    /// </summary>
    public sealed class Hud
    {
        public readonly Canvas Canvas;
        readonly CanvasScaler scaler;
        readonly RectTransform root, scorePanel, scoreTable, br, more, toastLayer, cardOv, cardRt, menu, menuCard, watchLv, labelLayer, miniFrame;
        readonly TextMeshProUGUI controlsTxt, sensTxt, scorePill, cardEyebrow, cardTitle, cardHead, cardBody, soundTxt, speedTxt;
        readonly Image cardOvImg;
        readonly RectTransform cardActions;
        readonly Button speedBtn;
        readonly LayoutElement pillLe;
        // safe-area root for the HUD, and the two side columns used on wide screens
        readonly RectTransform safeRoot, safeTop, leftCol, pillRt;
        bool sideMode, hudShown;
        float insL, insT, insR, insB;
        readonly Pills modePills, mapPills, lvB, lvW;
        readonly List<(TextMeshProUGUI t, string key)> bound = new List<(TextMeshProUGUI, string)>();
        readonly List<(TextMeshProUGUI t, string code)> langButtons = new List<(TextMeshProUGUI, string)>();
        // toasts: a stack sitting just above the fuse on the min line, the newest at the bottom
        sealed class ToastItem
        {
            public RectTransform Rt; public CanvasGroup Cg; public Tween Show, Slide;
            public float Until, Off, Target = float.NaN; public bool Turn, Leaving;
        }
        readonly List<ToastItem> toasts = new List<ToastItem>();
        float toastY = -1, viewW, viewH;
        int cardSeq;

        public Action<int, int, int, int> OnStart;   // mode, map, black level, white level
        public Action OnMenu, OnSound, OnSpeed, OnUiClick;
        public Action<string> OnLanguage;
        public Action<int> OnControls;   // -1 / +1 from the arrow buttons
        public Action<int> OnSens;

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
            cardEyebrow = Ui.Label(cardRt, "", 11, Palette.Muted);
            cardTitle = FitOneLine(Ui.Label(cardRt, "", 26, Palette.Text, true), 18);
            cardHead = FitOneLine(Ui.Label(cardRt, "", 34, Palette.Brass, true), 22);
            cardBody = Ui.Label(cardRt, "", 14, Palette.Text);
            cardBody.lineSpacing = 12;
            cardActions = Ui.Rect(cardRt, "Actions");
            cardActions.gameObject.AddComponent<FlowLayout>().SpacingX = 8;
            cardOv.gameObject.SetActive(false);

            // ---- start menu ----
            var menuImg = Ui.Rect(root, "Menu").gameObject.AddComponent<Image>();
            menu = menuImg.rectTransform; Ui.Stretch(menu);
            menuImg.color = Palette.Rgba(20, 13, 8, .62f);
            menuCard = Ui.Panel(menu, "MenuCard", Palette.Panel, Palette.Line, 12).rectTransform;
            var mc = menuCard;
            Ui.VStack(mc.gameObject, 20, 20, 20, 20, 12);
            Ui.Fit(mc.gameObject, false, true);
            Bind(Ui.Label(mc, "", 11, Palette.Muted), "menu.eyebrow");
            Bind(FitOneLine(Ui.Label(mc, "", 44, Palette.Text, true), 26), "menu.title");
            var lede = Bind(Ui.Label(mc, "", 14, Palette.Text), "menu.lede");
            lede.lineSpacing = 12;
            var mut = Bind(Ui.Label(mc, "", 14, Palette.Muted), "menu.note");
            mut.lineSpacing = 12;
            Legend(mc, "menu.mode");
            modePills = new Pills(mc, ModeLabels(), 0);
            watchLv = Ui.Rect(mc, "WatchLv");
            Ui.VStack(watchLv.gameObject, 0, 0, 0, 0, 6);
            Legend(watchLv, "menu.watchLevels");
            lvB = LevelRow(watchLv, Team.B);
            lvW = LevelRow(watchLv, Team.W);
            watchLv.gameObject.SetActive(false);
            modePills.Changed += i => watchLv.gameObject.SetActive(i == 4);
            Legend(mc, "menu.map");
            mapPills = new Pills(mc, MapLabels(), 0);
            var acts = Ui.Rect(mc, "Actions");
            acts.gameObject.AddComponent<FlowLayout>();
            var start = Ui.Button(acts, "", true, () => OnStart?.Invoke(modePills.Selected, mapPills.Selected, lvB.Selected, lvW.Selected));
            Bind(Ui.ButtonText(start), "menu.start");

            // ---- bottom right: settings (language, sound, menu), speed. Above the overlays, as in the prototype ----
            br = Ui.Rect(root, "HudBR");
            // its own safe-area root, created last so the corner buttons draw above the cards and the menu
            safeTop = Ui.Rect(root, "SafeTop"); Ui.Stretch(safeTop);
            br.SetParent(safeTop, false);
            br.anchorMin = br.anchorMax = new Vector2(1, 0); br.pivot = new Vector2(1, 0); br.anchoredPosition = new Vector2(-12, 12);
            var bv = Ui.VStack(br.gameObject, 0, 0, 0, 0, 6, TextAnchor.LowerRight);
            bv.childForceExpandWidth = false;
            Ui.Fit(br.gameObject, true, true);
            more = Ui.Panel(br, "More", Palette.Glass, Palette.Line, 10).rectTransform;
            Ui.VStack(more.gameObject, 6, 6, 6, 6, 2);
            var mb = Ui.Button(more, "", false, () => { ClosePopovers(); OnMenu?.Invoke(); }, 13);
            Flat(mb);
            Bind(Ui.ButtonText(mb), "settings.toMenu");
            var sb = Ui.Button(more, "", false, () => OnSound?.Invoke(), 13);
            Flat(sb);
            soundTxt = Ui.ButtonText(sb);
            var ctl = Ui.Label(more, "", 11, Palette.Muted);
            ctl.margin = new Vector4(12, 6, 12, 0);
            Bind(ctl, "settings.controls");
            var ctlRow = Ui.Rect(more, "Controls");
            Ui.HStack(ctlRow.gameObject, 4, 4, 0, 2, 0, TextAnchor.MiddleLeft);
            var prev = Ui.Button(ctlRow, "◀", false, () => OnControls?.Invoke(-1), 13, "ControlsPrev");
            Flat(prev); prev.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 8, 6, 6);
            controlsTxt = Ui.NoWrap(Ui.Label(ctlRow, "", 13, Palette.Text, false, TextAnchor.MiddleCenter));
            controlsTxt.gameObject.AddComponent<LayoutElement>().minWidth = 84;
            var next = Ui.Button(ctlRow, "▶", false, () => OnControls?.Invoke(1), 13, "ControlsNext");
            Flat(next); next.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 8, 6, 6);
            var sens = Ui.Label(more, "", 11, Palette.Muted);
            sens.margin = new Vector4(12, 6, 12, 0);
            Bind(sens, "settings.aimSens");
            var sensRow = Ui.Rect(more, "AimSensitivity");
            Ui.HStack(sensRow.gameObject, 4, 4, 0, 2, 0, TextAnchor.MiddleLeft);
            var sPrev = Ui.Button(sensRow, "◀", false, () => OnSens?.Invoke(-1), 13, "SensPrev");
            Flat(sPrev); sPrev.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 8, 6, 6);
            sensTxt = Ui.NoWrap(Ui.Label(sensRow, "", 13, Palette.Text, false, TextAnchor.MiddleCenter));
            sensTxt.gameObject.AddComponent<LayoutElement>().minWidth = 84;
            var sNext = Ui.Button(sensRow, "▶", false, () => OnSens?.Invoke(1), 13, "SensNext");
            Flat(sNext); sNext.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 8, 6, 6);
            var lang = Ui.Label(more, "", 11, Palette.Muted);
            lang.margin = new Vector4(12, 6, 12, 0);
            Bind(lang, "settings.language");
            var langRow = Ui.Rect(more, "Languages");
            Ui.HStack(langRow.gameObject, 4, 4, 0, 2, 0, TextAnchor.MiddleLeft);
            for (int i = 0; i < Loc.Codes.Length; i++)
            {
                string code = Loc.Codes[i];
                var lb = Ui.Button(langRow, Loc.NativeNames[i], false, () => OnLanguage?.Invoke(code), 13, "Lang_" + code);
                Flat(lb);
                lb.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 8, 6, 6);
                langButtons.Add((Ui.ButtonText(lb), code));
            }
            more.gameObject.SetActive(false);
            speedBtn = Ui.Button(br, "", false, () => OnSpeed?.Invoke(), 14, "Speed");
            Compact(speedBtn);
            speedTxt = Ui.ButtonText(speedBtn);
            speedBtn.gameObject.SetActive(false);
            var moreBtn = Ui.Button(br, "", false, () => { bool open = !more.gameObject.activeSelf; ClosePopovers(); more.gameObject.SetActive(open); }, 14, "Settings");
            Compact(moreBtn);
            Bind(Ui.ButtonText(moreBtn), "settings.button");

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

        static string[] ModeLabels() => new[] { Loc.T("mode.pvp"), Loc.T("mode.easy"), Loc.T("mode.normal"), Loc.T("mode.hard"), Loc.T("mode.watch") };
        static string[] MapLabels() => new[] { Loc.T("map.base"), Loc.T("map.bumper"), Loc.T("map.zones"), Loc.T("map.kink"), Loc.T("map.random") };
        static string[] LevelLabels() => new[] { Loc.T("level.easy"), Loc.T("level.normal"), Loc.T("level.hard") };

        /// <summary>Re-reads every fixed text from the string table (after a language change).</summary>
        public void Relocalize()
        {
            foreach (var (t, key) in bound) t.text = Loc.T(key);
            modePills?.SetLabels(ModeLabels());
            mapPills?.SetLabels(MapLabels());
            lvB?.SetLabels(LevelLabels());
            lvW?.SetLabels(LevelLabels());
            foreach (var (t, code) in langButtons) t.color = code == Loc.Code ? Palette.Brass : Palette.Text;
            lastTable = null;
        }

        static void Flat(Button b)
        {
            var img = (Image)b.targetGraphic;
            img.color = new Color(0, 0, 0, 0);
            var border = img.transform.Find("Border");
            if (border) border.gameObject.SetActive(false);
            var h = b.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(12, 12, 8, 8); h.childAlignment = TextAnchor.MiddleLeft;
            Ui.ButtonText(b).alignment = TextAlignmentOptions.Left;
            b.transition = Selectable.Transition.None;
        }

        static void Compact(Button b)
        {
            ((Image)b.targetGraphic).color = Palette.Rgba(44, 32, 23, .92f);
            b.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 12, 6, 6);
        }

        void Legend(Transform parent, string key)
        {
            var t = Bind(Ui.Label(parent, "", 11, Palette.Muted), key);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 16;
        }

        Pills LevelRow(Transform parent, Team team)
        {
            var row = Ui.Rect(parent, "Lv" + team);
            var h = Ui.HStack(row.gameObject, 0, 0, 0, 0, 10, TextAnchor.MiddleLeft);
            h.childForceExpandWidth = false;
            var name = Ui.Rect(row, "Name");
            Ui.HStack(name.gameObject, 0, 0, 0, 0, 4, TextAnchor.MiddleLeft);
            name.gameObject.AddComponent<LayoutElement>().preferredWidth = 52;
            Ui.Dot(name, team);
            Bind(Ui.NoWrap(Ui.Label(name, "", 13, Palette.Text)), team == Team.B ? "team.B" : "team.W");
            var p = new Pills(row, LevelLabels(), 2);
            p.Root.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            return p;
        }

        // ---------------- per-frame ----------------

        public void Tick(float cssPx, float cw, float ch)
        {
            scaler.scaleFactor = cssPx;
            // the safe area: everything below is laid out inside it
            float sw = cw - insL - insR, sh = ch - insT - insB;
            if (!sideMode)
            {
                // top-left corner: the pill as wide as its text, wrapping only if the row is narrower
                float rowW = sw - 20;
                float pillW = Mathf.Min(scorePill.GetPreferredValues(scorePill.text).x + 18 + 2, rowW);
                leftCol.sizeDelta = new Vector2(pillW, leftCol.sizeDelta.y);
            }
            float cardW = Mathf.Min(420, sw - 32);
            foreach (var rt in new[] { cardRt, menuCard })
            {
                rt.sizeDelta = new Vector2(cardW, rt.sizeDelta.y);
                // a card taller than the screen shrinks to fit rather than running off it
                float h = rt.rect.height, room = sh - 32;
                rt.localScale = Vector3.one * (h > room && h > 0 ? room / h : 1);
            }
            viewW = cw; viewH = ch;
            TickToasts();
            var sp = scorePanel;
            sp.anchorMin = sp.anchorMax = new Vector2(0, 1); sp.pivot = new Vector2(0, 1);
            float under = leftCol.rect.height;
            sp.anchoredPosition = new Vector2(10, -(10 + under + 4));
        }

        public bool HudVisible { set { hudShown = value; ApplyHudVisibility(); if (!value) scorePanel.gameObject.SetActive(false); } }
        public bool MenuVisible { set => menu.gameObject.SetActive(value); }
        public bool SpeedVisible { set => speedBtn.gameObject.SetActive(value); }
        public string SpeedText { set => speedTxt.text = value; }
        public string SoundText { set => soundTxt.text = value; }
        public string ControlsText { set => controlsTxt.text = value; }
        public string SensText { set => sensTxt.text = value; }

        /// <summary>Bottom edge of the top HUD row plus a margin, in css px (the camera keeps the far end clear of it).</summary>
        public float HudBottom => insT + (hudShown && !sideMode ? 10 + leftCol.rect.height + 8 : 18);

        /// <summary>Where the right edge is free from (css px from the top): nothing sits in the top-right corner.</summary>
        public float RightColumnBottom => insT + 10;

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
            more.gameObject.SetActive(false);
        }

        public void SetScorePill(string rich) { if (scorePill.text != rich) scorePill.text = rich; }

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

        public void Toast(string title, string body = null, float ms = 2400, int titleSize = 17) => Push(MakeToast(title, body, false, titleSize), ms);

        /// <summary>True until every toast has finished fading out.</summary>
        public bool ToastsUp => toasts.Count > 0;

        /// <summary>Whose turn, larger; a new turn replaces the last one.</summary>
        public void TurnToast(string text)
        {
            foreach (var t in toasts) if (t.Turn) Leave(t);
            Push(MakeToast(text, null, true, 24), 1050);
        }

        public void ClearToasts()
        {
            foreach (var t in toasts) { t.Show?.Kill(); t.Slide?.Kill(); UnityEngine.Object.Destroy(t.Rt.gameObject); }
            toasts.Clear();
        }

        ToastItem MakeToast(string title, string body, bool turn, int titleSize)
        {
            var img = Ui.Panel(toastLayer, turn ? "TurnToast" : "Toast", Palette.Panel, Palette.Line, turn ? 10 : 8);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 0);
            if (turn) Ui.VStack(rt.gameObject, 22, 22, 10, 10, 0, TextAnchor.MiddleCenter);
            else Ui.VStack(rt.gameObject, 14, 14, 9, 9, 2, TextAnchor.UpperCenter);
            Ui.Fit(rt.gameObject, false, true);
            var tt = Ui.Label(rt, title, titleSize, turn ? Palette.Text : Palette.Brass, true, TextAnchor.MiddleCenter);
            tt.raycastTarget = false;
            float want = tt.GetPreferredValues(title).x;
            if (!string.IsNullOrEmpty(body))
            {
                var tb = Ui.Label(rt, body, 13, Palette.Text, false, TextAnchor.MiddleCenter);
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
            it.Until = Time.unscaledTime + ms / 1000f;
            toasts.Add(it);
            // no more than four up at once: the oldest goes first
            var live = toasts.FindAll(t => !t.Leaving);
            for (int i = 0; i < live.Count - 4; i++) Leave(live[i]);
            it.Rt.localScale = Vector3.one * 0.85f;
            it.Show = DOTween.Sequence().SetUpdate(true).SetLink(it.Rt.gameObject)
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
            it.Show = DOTween.To(() => it.Cg.alpha, v => it.Cg.alpha = v, 0, 0.3f).SetUpdate(true).SetLink(it.Rt.gameObject)
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
                    else t.Slide = DOTween.To(() => t.Off, v => t.Off = v, off, 0.28f).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(t.Rt.gameObject);
                }
                off += t.Rt.rect.height + 8;
            }
        }

        void TickToasts()
        {
            if (toasts.Count == 0) return;
            float now = Time.unscaledTime;
            foreach (var t in toasts.ToArray()) if (!t.Leaving && now > t.Until) Leave(t);
            // just above the fuse; kept on screen when the camera has taken the min line off it
            float y = Mathf.Clamp(toastY < 0 ? viewH * 0.6f : toastY, insT + 140, viewH - insB - 12);
            float x = (insL - insR) / 2;
            foreach (var t in toasts) t.Rt.anchoredPosition = new Vector2(x, -y + t.Off);
        }

        // ---------------- card ----------------

        public bool CardVisible => cardOv.gameObject.activeSelf;
        public int CardSeq => cardSeq;

        public int ShowCard(CardSpec c, Action onAnyClick)
        {
            cardSeq++;
            FillCard(c, onAnyClick);
            cardOv.gameObject.SetActive(true);
            return cardSeq;
        }

        /// <summary>Re-fills the visible card in place (same sequence number, so a pending auto-advance still fires).</summary>
        public void ReplaceCard(CardSpec c, Action onAnyClick) { if (CardVisible) FillCard(c, onAnyClick); }

        void FillCard(CardSpec c, Action onAnyClick)
        {
            cardEyebrow.text = c.Eyebrow;
            cardTitle.text = c.Title;
            cardHead.text = c.Head; cardHead.gameObject.SetActive(!string.IsNullOrEmpty(c.Head));
            cardBody.text = c.Body; cardBody.gameObject.SetActive(!string.IsNullOrEmpty(c.Body));
            for (int i = cardActions.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(cardActions.GetChild(i).gameObject);
            for (int i = 0; i < c.Buttons.Count; i++)
            {
                var b = c.Buttons[i];
                Ui.Button(cardActions, b.Label, i == 0, () => { HideCard(); onAnyClick?.Invoke(); b.OnClick(); }, 14, "CardBtn" + i);
            }
            if (c.Soft)
            {
                cardOvImg.sprite = Ui.FadeUp; cardOvImg.color = Color.white;
                cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0); cardRt.pivot = new Vector2(0.5f, 0);
                cardRt.anchoredPosition = new Vector2(0, 16 + insB);
            }
            else
            {
                cardOvImg.sprite = null; cardOvImg.color = Palette.Rgba(20, 13, 8, .62f);
                cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f); cardRt.pivot = new Vector2(0.5f, 0.5f);
                cardRt.anchoredPosition = Vector2.zero;
            }
        }

        public void HideCard() => cardOv.gameObject.SetActive(false);

        // ---------------- minimap frame ----------------

        public void MiniFrame(Rect? r)
        {
            miniFrame.gameObject.SetActive(r.HasValue);
            if (r.HasValue) Ui.TopLeft(miniFrame, r.Value.x - 3, r.Value.y - 3, r.Value.width + 6, r.Value.height + 6);
        }

        // ---------------- world labels (immediate mode) ----------------

        readonly List<TextMeshProUGUI> pool = new List<TextMeshProUGUI>();
        int used;

        public void BeginLabels() { used = 0; }

        /// <summary>A text at a css-px point (top-left origin). align: -1 left, 0 centre, 1 right of the point. rot in degrees (counter-clockwise).</summary>
        public void Label(string text, float x, float y, int size, Color color, int align = 0, bool display = false, bool stroke = false, float rot = 0, float scale = 1)
        {
            TextMeshProUGUI t;
            if (used < pool.Count) t = pool[used];
            else
            {
                t = Ui.NoWrap(Ui.Label(labelLayer, "", size, color));
                pool.Add(t);
            }
            used++;
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            var font = display ? Ui.Display : Ui.Body;
            if (t.font != font) t.font = font;
            var mat = stroke ? Ui.StrokeFor(font) : font.material;
            if (t.fontSharedMaterial != mat) t.fontSharedMaterial = mat;
            if (t.text != text) t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align < 0 ? TextAlignmentOptions.Left : align > 0 ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(align < 0 ? 0 : align > 0 ? 1 : 0.5f, 0.5f);
            rt.sizeDelta = new Vector2(10, size * 1.4f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.localEulerAngles = new Vector3(0, 0, rot);
            rt.localScale = Vector3.one * scale;
        }

        public void EndLabels()
        {
            for (int i = used; i < pool.Count; i++) if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
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
