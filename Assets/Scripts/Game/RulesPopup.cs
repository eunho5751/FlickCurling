using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The game rules popup (the prefab Resources/RulesPopup, built by MainMenuBuilder on the shared Popup). It opens on the
    /// list of its pages, one button each; a page shows its title on the title bar, "n / count" under it and its items (a
    /// label and a short line each), with a list button at the bar's left end back to the list. A sideways swipe turns the
    /// page, round from the last to the first and back. On the first page a hand shows the swipe, again and again, until the
    /// player has turned a page while it was showing (remembered on the device). The numbers in the text (ends, stones,
    /// seconds) are GameConfig's.
    /// </summary>
    public sealed class RulesPopup : MonoBehaviour
    {
        public const string ResourcePath = "RulesPopup";
        const string SwipeLearned = "rules.swipeLearned";   // PlayerPrefs: the swipe hint is not shown again

        // each page: its key in the string table ("rules.<id>.title", "rules.<id>.<n>.k" / ".t"), and its items, one letter
        // each: 'i' a heading and its text, 's' a section's heading alone, 't' a term under a section (its own colour,
        // set in), 'p' a paragraph with no heading
        static readonly (string id, string items)[] Pages =
        {
            ("basics", "isttttt"), ("order", "iii"), ("end", "iii"), ("controls", "iiiii"), ("items", "iii"), ("itemkinds", "iiiii")
        };
        static readonly Color TermColor = new Color32(0x34, 0x40, 0x6a, 0xff);   // the house's navy
        const float TermIndent = 22;

        // items shown with a picture before their label (page id, item number from 1): the item mode's orb and items
        static string IconOf(string page, int n) => (page, n) switch
        {
            ("items", 1) => "Items/box_b",
            ("itemkinds", 1) => "Items/giant", ("itemkinds", 2) => "Items/mini", ("itemkinds", 3) => "Items/bomb",
            ("itemkinds", 4) => "Items/magnet", ("itemkinds", 5) => "Items/protractor",
            _ => null
        };
        const float IconSize = 64, IconGap = 14;
        const float FigureGap = 26;   // more room between a page's picture and its items than between the items
        public static int PageCount => Pages.Length;
        public static string TitleKey(int page) => "rules." + Pages[page].id + ".title";

        [SerializeField] Popup popup;
        [SerializeField] GameObject listView, pageView;
        [SerializeField] Button[] pageButtons;   // the list, one per page in order
        [SerializeField] Button listButton;      // the title bar's left end, on a page
        [SerializeField] TextMeshProUGUI counter;
        [SerializeField] RectTransform viewport;
        [SerializeField] RectTransform pageTemplate;   // a page: a scroll view whose content is its figure, then the item template (inactive), then its items
        [SerializeField] SwipeArea swipe;
        [SerializeField] RectTransform hint, hintHand;   // the swipe hint: the hand (and the arrow over it) inside hint
        [SerializeField] LocalizeStringEvent titleLoc;   // the popup's title: the rules, or the page's
        [SerializeField] Sprite icon;
        [SerializeField] Vector2 sizeList = new Vector2(900, 1040), sizePage = new Vector2(900, 1660);
        [SerializeField] float flingSpeed = 900, turnShare = 0.18f;   // a turn: a quick flick, or past this share of the width

        RectTransform cur, other;   // the page shown and the one beside it while swiping
        int page, otherPage = -1;
        float drag;
        bool turning, hintSeen;
        Sequence hintAnim;

        bool ready;

        void Init()
        {
            if (ready) return;
            ready = true;
            Loc.Init();   // (hooks the language change, should this come before Boot has)
            pageTemplate.gameObject.SetActive(false);
            cur = Instantiate(pageTemplate, viewport); cur.name = "PageA";
            other = Instantiate(pageTemplate, viewport); other.name = "PageB";
            for (int i = 0; i < pageButtons.Length; i++) { int p = i; pageButtons[i].onClick.AddListener(() => ShowPage(p)); }
            listButton.onClick.AddListener(ShowList);
            swipe.Moved += OnMoved;
            swipe.Released += OnReleased;
            popup.Closed += StopHint;
        }

        void OnEnable() => Loc.Changed += OnLanguage;
        void OnDisable() { Loc.Changed -= OnLanguage; StopHint(); }

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>Opens on the list of pages.</summary>
        public void Open()
        {
            Init();
            ShowList();
            popup.Show();
        }

        void ShowList()
        {
            StopHint();
            KillTurn();
            listView.SetActive(true);
            pageView.SetActive(false);
            SetTitle("mainmenu.rules");
            popup.SetIcon(icon);
            listButton.gameObject.SetActive(false);
            popup.SetSize(sizeList.x, sizeList.y);
        }

        void ShowPage(int p)
        {
            KillTurn();
            listView.SetActive(false);
            pageView.SetActive(true);
            listButton.gameObject.SetActive(true);
            popup.SetIcon(null);
            popup.SetSize(sizePage.x, sizePage.y);
            hintSeen = false;
            Turned(p);
        }

        // the page now shown (by the list or a swipe): its title, the count, the page itself
        void Turned(int p)
        {
            page = p;
            drag = 0;
            Fill(cur, page);
            cur.gameObject.SetActive(true);
            cur.anchoredPosition = Vector2.zero;
            other.gameObject.SetActive(false);
            otherPage = -1;
            SetTitle(TitleKey(page));
            counter.text = (page + 1) + " / " + Pages.Length;
            if (page == 0 && PlayerPrefs.GetInt(SwipeLearned, 0) == 0) StartHint(); else StopHint();
        }

        void SetTitle(string key)
        {
            if (titleLoc) { titleLoc.StringReference.TableEntryReference = key; titleLoc.RefreshString(); }
            else popup.Title = Loc.T(key);
        }

        void OnLanguage()
        {
            if (!ready || !pageView.activeSelf) return;
            Fill(cur, page);
            if (otherPage >= 0) Fill(other, otherPage);
        }

        // ---------------- a page's items ----------------

        // a page's content (in its scroll view), and back at its top
        static RectTransform Content(RectTransform page) => page.GetComponent<ScrollRect>().content;

        Color? accent;   // the headings' colour (the template's)

        void Fill(RectTransform page, int p)
        {
            var target = Content(page);
            var cfg = GameConfig.Load();
            var args = new (string, object)[]
            {
                ("ends", cfg.ends), ("shots", cfg.shotsPerTeam), ("seconds", Mathf.RoundToInt(cfg.turnSeconds)),
                ("giant", cfg.giantSize), ("mini", cfg.miniSize)
            };
            var figure = (RectTransform)target.GetChild(0);
            var template = target.GetChild(1);
            var labelFont = template.GetChild(0).GetComponent<TextMeshProUGUI>().font;
            figure.GetComponent<LayoutElement>().preferredHeight = RulesFigures.Height(Pages[p].id) + FigureGap;   // (the picture at its top)
            RulesFigures.Draw(figure, Pages[p].id, labelFont);
            const int First = 2;   // (the figure and the template come first)
            if (accent == null) accent = template.GetChild(0).GetComponent<TextMeshProUGUI>().color;
            string styles = Pages[p].items;
            int n = styles.Length;
            for (int i = 0; i < n; i++)
            {
                var item = First + i < target.childCount ? target.GetChild(First + i) : Instantiate(template, target);
                item.gameObject.SetActive(true);
                string key = "rules." + Pages[p].id + "." + (i + 1);
                char style = styles[i];
                var label = item.GetChild(0).GetComponent<TextMeshProUGUI>();
                var body = item.GetChild(1).GetComponent<TextMeshProUGUI>();
                string icon = IconOf(Pages[p].id, i + 1);
                label.gameObject.SetActive(style != 'p');
                if (style != 'p') label.text = icon != null ? $"<indent={IconSize + IconGap}>{Loc.T(key + ".k")}</indent>" : Loc.T(key + ".k");
                label.color = style == 't' ? TermColor : accent.Value;
                var inset = new Vector4(style == 't' ? TermIndent : 0, 0, 0, 0);
                label.margin = inset; body.margin = inset;
                SetIcon(label, icon);
                body.gameObject.SetActive(style != 's');
                if (style != 's') body.text = Loc.T(key + ".t", args);
            }
            for (int i = First + n; i < target.childCount; i++) target.GetChild(i).gameObject.SetActive(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(target);
            page.GetComponent<ScrollRect>().verticalNormalizedPosition = 1;
        }

        // a picture at the label's left (made the first time; hidden on items without one)
        void SetIcon(TextMeshProUGUI label, string path)
        {
            var t = label.transform.Find("Icon");
            if (path == null) { if (t) t.gameObject.SetActive(false); return; }
            Image img;
            if (t) img = t.GetComponent<Image>();
            else
            {
                img = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
                img.transform.SetParent(label.transform, false);
                img.raycastTarget = false; img.preserveAspect = true;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0.5f);
                rt.sizeDelta = new Vector2(IconSize, IconSize);
                rt.anchoredPosition = Vector2.zero;
            }
            img.sprite = Resources.Load<Sprite>(path);
            img.gameObject.SetActive(true);
        }

        // ---------------- swiping ----------------

        float Width => viewport.rect.width;

        void OnMoved(float dx)
        {
            if (turning || !pageView.activeSelf) return;
            drag = dx;
            Place();
        }

        // the page follows the finger, the next one (dragged left) or the one before (right) coming in beside it
        void Place()
        {
            cur.anchoredPosition = new Vector2(drag, 0);
            if (Mathf.Approximately(drag, 0)) { other.gameObject.SetActive(false); return; }
            int dir = drag < 0 ? 1 : -1;
            int p = (page + dir + Pages.Length) % Pages.Length;
            if (p != otherPage) { otherPage = p; Fill(other, p); }
            other.gameObject.SetActive(true);
            other.anchoredPosition = new Vector2(drag + dir * Width, 0);
        }

        void OnReleased(float dx, float speed)
        {
            if (turning || !pageView.activeSelf) return;
            drag = dx;
            int dir = 0;
            if (dx < 0 && (dx < -Width * turnShare || speed < -flingSpeed)) dir = 1;
            else if (dx > 0 && (dx > Width * turnShare || speed > flingSpeed)) dir = -1;
            float to = -dir * Width;
            turning = true;
            DOTween.To(() => drag, v => { drag = v; Place(); }, to, dir == 0 ? 0.15f : 0.22f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetId(this)
                .OnComplete(() =>
                {
                    turning = false;
                    if (dir == 0) { drag = 0; Place(); return; }
                    // the page beside is the one shown now
                    (cur, other) = (other, cur);
                    if (hintSeen) { PlayerPrefs.SetInt(SwipeLearned, 1); PlayerPrefs.Save(); }
                    Turned((page + dir + Pages.Length) % Pages.Length);
                });
        }

        void KillTurn()
        {
            DOTween.Kill(this);
            turning = false;
        }

        // ---------------- the swipe hint ----------------

        // in the empty room under the page's last item: the hand (the double arrow over it) slides to the left and back,
        // fades, waits, and again
        void StartHint()
        {
            if (hintAnim != null) return;
            hintSeen = true;
            hint.gameObject.SetActive(true);
            var content = Content(cur);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            RectTransform last = null;
            foreach (RectTransform c in content) if (c.gameObject.activeSelf) last = c;
            var vr = viewport.rect;
            // (a page that fills the window: low in it)
            float below = Mathf.Max(viewport.InverseTransformPoint(last.TransformPoint(new Vector3(0, last.rect.yMin))).y, vr.yMin + 300);
            hint.position = viewport.TransformPoint(new Vector3(vr.center.x, (below + vr.yMin) / 2));
            var g = hint.GetComponent<CanvasGroup>();
            const float Swing = 130;
            g.alpha = 0;
            float hy = hintHand.anchoredPosition.y;
            hintHand.anchoredPosition = new Vector2(Swing * 0.5f, hy);
            Tween Fade(float to) => DOTween.To(() => g.alpha, a => g.alpha = a, to, 0.25f);
            Tween Slide(float to) => DOTween.To(() => hintHand.anchoredPosition.x,
                x => hintHand.anchoredPosition = new Vector2(x, hy), to, 0.7f).SetEase(Ease.InOutSine);
            hintAnim = DOTween.Sequence().SetUpdate(true)
                .Append(Fade(1))
                .Append(Slide(-Swing))
                .Append(Slide(Swing * 0.5f))
                .Append(Slide(-Swing))
                .Append(Fade(0))
                .AppendInterval(1.0f)
                .AppendCallback(() => hintHand.anchoredPosition = new Vector2(Swing * 0.5f, hy))
                .SetLoops(-1);
        }

        void StopHint()
        {
            hintAnim?.Kill();
            hintAnim = null;
            if (hint) hint.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
            hintAnim?.Kill();
        }
    }
}
