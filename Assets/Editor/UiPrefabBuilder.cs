using System.IO;
using System.Linq;
using FlickCurling.Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Builds the shared UI control prefabs in Assets/Prefabs/UI, on the popup art (PopupBuilder.Controls): a primary
    /// (coral) and a secondary (wooden) button, a group toggle (SegmentedToggle) and a slider. Screens use instances of
    /// them; <see cref="SetLabel"/> and <see cref="SetOptions"/> fill an instance in with localized texts. Also the screen
    /// fade (Resources/ScreenFader, loaded by ScreenFader itself).
    /// </summary>
    public static class UiPrefabBuilder
    {
        public const string Dir = "Assets/Prefabs/UI";
        public const string PrimaryPath = Dir + "/Button_Primary.prefab", SecondaryPath = Dir + "/Button_Secondary.prefab";
        public const string TogglePath = Dir + "/ToggleGroup.prefab", SliderPath = Dir + "/Slider.prefab", DropdownPath = Dir + "/Dropdown.prefab";
        public const string FaderPath = "Assets/Resources/" + ScreenFader.ResourcePath + ".prefab";   // loaded at run time

        // default sizes in the menu's reference pixels
        const float ButtonW = 380, ButtonH = 112, ToggleW = 720, ToggleH = 96, SliderW = 720, SliderH = 44, HandleSize = 76;
        static readonly Color32 Cream = new Color32(0xf6, 0xea, 0xd2, 0xff), Ink = new Color32(0x4a, 0x2a, 0x12, 0xff);

        static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Menu/" + name + ".png");

        [MenuItem("FlickCurling/Build UI Control Prefabs")]
        public static void Build()
        {
            PopupBuilder.ImportControls();
            Directory.CreateDirectory(Dir);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.ButtonPath);   // Jua, Mochiy Pop One for Japanese
            Save(MakeButton("Button_Primary", Art("ui_button_primary"), font), PrimaryPath);
            Save(MakeButton("Button_Secondary", Art("ui_button_secondary"), font), SecondaryPath);
            Save(MakeToggle(font), TogglePath);
            Save(MakeSlider(), SliderPath);
            Save(MakeDropdown(font), DropdownPath);
            Save(MakeFader(), FaderPath);
            Debug.Log("FlickCurling: built the UI control prefabs in " + Dir);
        }

        static void Save(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        static void Pressable(Selectable b)
        {
            var cb = b.colors; cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f); cb.pressedColor = new Color(.82f, .82f, .82f);
            cb.disabledColor = new Color(.7f, .7f, .7f, .8f); b.colors = cb;
        }

        // a raised button with a centred label (its face sits a little above the bottom lip)
        static GameObject MakeButton(string name, Sprite sprite, TMP_FontAsset font)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            img.rectTransform.sizeDelta = new Vector2(ButtonW, ButtonH);
            img.sprite = sprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = sprite.rect.height / ButtonH;
            Pressable(img.gameObject.AddComponent<Button>());
            var label = MainMenuBuilder.Label(img.transform, "Label", font, 48, Cream);
            MainMenuBuilder.Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(0, 8);
            label.enableAutoSizing = true; label.fontSizeMin = 28; label.fontSizeMax = 48;
            label.text = "Button";
            return img.gameObject;
        }

        // the groove with the sliding wooden knob and one option; SetOptions makes it as many as needed
        static GameObject MakeToggle(TMP_FontAsset font)
        {
            var track = new GameObject("ToggleGroup", typeof(RectTransform)).AddComponent<Image>();
            track.rectTransform.sizeDelta = new Vector2(ToggleW, ToggleH);
            var ts = Art("ui_toggle_track");
            track.sprite = ts; track.type = Image.Type.Sliced; track.pixelsPerUnitMultiplier = ts.rect.height / ToggleH;
            var inner = MainMenuBuilder.Child<RectTransform>(track.transform, "Inner");
            MainMenuBuilder.Stretch(inner); inner.offsetMin = new Vector2(8, 9); inner.offsetMax = new Vector2(-8, -9);
            var knob = MainMenuBuilder.Child<Image>(inner, "Knob");
            var ks = Art("ui_toggle_on");
            knob.sprite = ks; knob.type = Image.Type.Sliced; knob.pixelsPerUnitMultiplier = ks.rect.height / (ToggleH - 18);
            knob.raycastTarget = false;
            var krt = knob.rectTransform; krt.anchorMin = Vector2.zero; krt.anchorMax = Vector2.one; krt.offsetMin = krt.offsetMax = Vector2.zero;

            var hit = MainMenuBuilder.Child<Image>(inner, "Option0");
            hit.color = new Color(1, 1, 1, 0);   // only takes the tap
            var rt = hit.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            hit.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var label = MainMenuBuilder.Label(hit.transform, "Label", font, 40, Cream);
            MainMenuBuilder.Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(0, 4);
            label.text = "Option";

            var toggle = track.gameObject.AddComponent<SegmentedToggle>();
            Wire(toggle, krt, new[] { hit.GetComponent<Button>() }, new[] { label }, 0);
            return track.gameObject;
        }

        static void Wire(SegmentedToggle toggle, RectTransform knob, Button[] options, TextMeshProUGUI[] labels, int selected)
        {
            var so = new SerializedObject(toggle);
            so.FindProperty("knob").objectReferenceValue = knob;
            so.FindProperty("selected").intValue = selected;
            var ob = so.FindProperty("options"); ob.arraySize = options.Length;
            var lb = so.FindProperty("labels"); lb.arraySize = labels.Length;
            for (int i = 0; i < options.Length; i++)
            {
                ob.GetArrayElementAtIndex(i).objectReferenceValue = options[i];
                lb.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // a Unity slider: the groove, the honey fill and the go-stone handle
        static GameObject MakeSlider()
        {
            var root = new GameObject("Slider", typeof(RectTransform));
            ((RectTransform)root.transform).sizeDelta = new Vector2(SliderW, HandleSize);
            var bg = MainMenuBuilder.Child<Image>(root.transform, "Background");
            var tr = Art("ui_slider_track");
            bg.sprite = tr; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = tr.rect.height / SliderH;
            Band(bg.rectTransform, 0, 0);
            var area = MainMenuBuilder.Child<RectTransform>(root.transform, "Fill Area");
            Band(area, 6, 6);
            var fill = MainMenuBuilder.Child<Image>(area, "Fill");
            var fs = Art("ui_slider_fill");
            fill.sprite = fs; fill.type = Image.Type.Sliced; fill.pixelsPerUnitMultiplier = fs.rect.height / (SliderH - 12);
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1); fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var slide = MainMenuBuilder.Child<RectTransform>(root.transform, "Handle Slide Area");
            MainMenuBuilder.Stretch(slide); slide.offsetMin = new Vector2(HandleSize / 2, 0); slide.offsetMax = new Vector2(-HandleSize / 2, 0);
            var handle = MainMenuBuilder.Child<Image>(slide, "Handle");
            handle.sprite = Art("ui_slider_handle"); handle.preserveAspect = true;
            handle.rectTransform.anchorMin = new Vector2(0, 0.5f); handle.rectTransform.anchorMax = new Vector2(0, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(HandleSize, HandleSize);
            var s = root.AddComponent<Slider>();
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight; s.minValue = 0; s.maxValue = 1; s.value = 0.5f;
            Pressable(s);
            return root;
        }

        // a horizontal band of the slider's height across its middle, inset left and right
        static void Band(RectTransform rt, float left, float inset)
        {
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(-2 * left, SliderH - 2 * inset); rt.anchoredPosition = Vector2.zero;
        }

        // a TMP dropdown on the dropdown art: the field (the arrow in its wooden end cap) and the list below it, its rows
        // marked with the honey wood strip
        const float DropdownW = 480, DropdownH = 82, DropdownRowH = 72, DropdownPad = 16;   // the list's inner padding
        static GameObject MakeDropdown(TMP_FontAsset font)
        {
            var res = new TMP_DefaultControls.Resources { standard = Art("ui_dropdown_item"), background = Art("ui_dropdown_list"), dropdown = Art("ui_dropdown"), checkmark = null, mask = null };
            var go = TMP_DefaultControls.CreateDropdown(res);
            go.name = "Dropdown";
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(DropdownW, DropdownH);
            var dd = go.GetComponent<TMP_Dropdown>();
            var field = go.GetComponent<Image>();
            field.sprite = Art("ui_dropdown"); field.type = Image.Type.Sliced; field.pixelsPerUnitMultiplier = field.sprite.rect.height / DropdownH;
            Pressable(dd);

            // the chosen option, on the cream face
            var label = (TextMeshProUGUI)dd.captionText;
            label.font = font; label.fontSize = 40; label.color = Ink; label.alignment = TextAlignmentOptions.Left;
            label.rectTransform.offsetMin = new Vector2(28, 6); label.rectTransform.offsetMax = new Vector2(-(DropdownH + 10), -2);
            // the arrow in the end cap
            var arrow = go.transform.Find("Arrow").GetComponent<Image>();
            arrow.sprite = Art("ui_dropdown_arrow"); arrow.preserveAspect = true; arrow.color = Color.white;
            var art = arrow.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(1, 0.5f); art.pivot = new Vector2(0.5f, 0.5f);
            art.anchoredPosition = new Vector2(-DropdownH * 0.36f, 2); art.sizeDelta = new Vector2(DropdownH * 0.42f, DropdownH * 0.42f);

            // the list: the wooden-rimmed parchment, a little below the field
            var template = (RectTransform)go.transform.Find("Template");
            var list = template.GetComponent<Image>();
            list.sprite = Art("ui_dropdown_list"); list.type = Image.Type.Sliced; list.pixelsPerUnitMultiplier = 2;   // a thinner rim than the art
            template.offsetMin = new Vector2(0, template.offsetMin.y); template.anchoredPosition = new Vector2(0, -6);
            template.sizeDelta = new Vector2(0, DropdownRowH * 6);   // only a cap: the list shrinks to its rows
            // TMP sizes the list to its rows alone, so the viewport fills it and the rows keep clear of the rim themselves;
            // a rect mask clips them to it
            var viewport = (RectTransform)template.Find("Viewport");
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            Object.DestroyImmediate(viewport.GetComponent<Mask>());
            Object.DestroyImmediate(viewport.GetComponent<Image>());
            viewport.gameObject.AddComponent<RectMask2D>();
            var scrollbar = template.Find("Scrollbar");
            if (scrollbar) Object.DestroyImmediate(scrollbar.gameObject);
            template.GetComponent<ScrollRect>().verticalScrollbar = null;
            // the padding goes inside the content round the item: TMP keeps the item's distance to the content's edges
            // when it lays out the rows, so the list gets the same room above, below and beside them
            var content = (RectTransform)viewport.Find("Content");
            content.sizeDelta = new Vector2(0, DropdownRowH + 2 * DropdownPad);
            var item = (RectTransform)content.Find("Item");
            item.anchorMin = new Vector2(0, 0.5f); item.anchorMax = new Vector2(1, 0.5f); item.pivot = new Vector2(0.5f, 0.5f);
            item.anchoredPosition = Vector2.zero;
            item.sizeDelta = new Vector2(-2 * DropdownPad, DropdownRowH);
            var bg = item.Find("Item Background").GetComponent<Image>();
            bg.sprite = Art("ui_dropdown_item"); bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = bg.sprite.rect.height / (DropdownRowH - 6);
            bg.rectTransform.offsetMin = new Vector2(0, 3); bg.rectTransform.offsetMax = new Vector2(0, -3);
            var toggle = item.GetComponent<Toggle>();
            // the strip marks the hovered and the pressed row, and the chosen one through its checkmark
            var tc = toggle.colors; tc.normalColor = new Color(1, 1, 1, 0); tc.highlightedColor = new Color(1, 1, 1, 0.55f);
            tc.pressedColor = new Color(1, 1, 1, 0.8f); tc.selectedColor = new Color(1, 1, 1, 0.55f); toggle.colors = tc;
            var check = item.Find("Item Checkmark").GetComponent<Image>();
            check.sprite = Art("ui_dropdown_item"); check.type = Image.Type.Sliced; check.pixelsPerUnitMultiplier = bg.pixelsPerUnitMultiplier;
            check.rectTransform.anchorMin = Vector2.zero; check.rectTransform.anchorMax = Vector2.one;
            check.rectTransform.offsetMin = new Vector2(0, 3); check.rectTransform.offsetMax = new Vector2(0, -3);
            check.rectTransform.SetAsFirstSibling();
            var itemLabel = item.Find("Item Label").GetComponent<TextMeshProUGUI>();
            itemLabel.font = font; itemLabel.fontSize = 38; itemLabel.color = Ink; itemLabel.alignment = TextAlignmentOptions.Left;
            itemLabel.rectTransform.offsetMin = new Vector2(24, 2); itemLabel.rectTransform.offsetMax = new Vector2(-16, 0);
            return go;
        }

        // the screen fade: its own overlay canvas above everything, one dark cover that also blocks every touch
        static GameObject MakeFader()
        {
            var go = new GameObject("ScreenFader");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = ScreenFader.SortingOrder;
            go.AddComponent<GraphicRaycaster>();
            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0; group.blocksRaycasts = false;
            var cover = MainMenuBuilder.Child<Image>(go.transform, "Cover");
            MainMenuBuilder.Stretch(cover.rectTransform);
            cover.color = ScreenFader.Cover;
            var fader = go.AddComponent<ScreenFader>();
            var so = new SerializedObject(fader);
            so.FindProperty("group").objectReferenceValue = group;
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        // ---- filling in instances ----

        /// <summary>Instantiates a control prefab under <paramref name="parent"/>.</summary>
        public static GameObject Place(string prefabPath, Transform parent, string name = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), parent);
            if (name != null) go.name = name;
            return go;
        }

        const float OptionIconSize = 52;

        // puts an icon on the label's left: the two in a centred row across the option
        static void WithIcon(Transform option, TextMeshProUGUI label, Sprite sprite)
        {
            var row = MainMenuBuilder.Child<RectTransform>(option, "Row");
            MainMenuBuilder.Stretch(row); row.offsetMin = new Vector2(8, 4); row.offsetMax = new Vector2(-8, 0);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10; h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
            var icon = MainMenuBuilder.Child<Image>(row, "Icon");
            icon.sprite = sprite; icon.preserveAspect = true; icon.raycastTarget = false;
            var le = icon.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = OptionIconSize; le.preferredHeight = OptionIconSize;
            label.transform.SetParent(row, false);
        }

        /// <summary>The button's label, localized by <paramref name="key"/> of the UI table.</summary>
        public static Button SetLabel(GameObject button, string key)
        {
            var label = button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            label.text = UiStrings.Entries.First(e => e[0] == key)[1];
            LocalizeEvents.BindText(label, Loc.Table, key);   // Editor And Runtime
            return button.GetComponent<Button>();
        }

        /// <summary>One equal option per key (localized), <paramref name="selected"/> picked; the prefab's option is the template.
        /// With <paramref name="icons"/>, each label has its icon on the left, the pair centred in the option.</summary>
        public static SegmentedToggle SetOptions(GameObject toggleGo, string[] keys, int selected, Sprite[] icons = null)
        {
            var inner = toggleGo.transform.Find("Inner");
            var template = inner.Find("Option0").gameObject;
            var options = new Button[keys.Length];
            var labels = new TextMeshProUGUI[keys.Length];
            // every option is a fresh copy of the bare template (so none carries another option's localization, and they
            // can be rearranged: a prefab instance's own children cannot); the template itself is put away
            var opts = new GameObject[keys.Length];
            for (int i = 0; i < keys.Length; i++) opts[i] = Object.Instantiate(template, inner);
            template.name = "OptionTemplate";
            template.SetActive(false);
            for (int i = 0; i < keys.Length; i++)
            {
                var opt = opts[i];
                opt.name = "Option" + i;
                var rt = (RectTransform)opt.transform;
                rt.anchorMin = new Vector2((float)i / keys.Length, 0); rt.anchorMax = new Vector2((float)(i + 1) / keys.Length, 1);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                var label = opt.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                label.text = UiStrings.Entries.First(e => e[0] == keys[i])[1];
                label.color = i == selected ? Cream : Ink;
                LocalizeEvents.BindText(label, Loc.Table, keys[i]);
                if (icons != null && i < icons.Length && icons[i] != null) WithIcon(opt.transform, label, icons[i]);
                options[i] = opt.GetComponent<Button>(); labels[i] = label;
            }
            var knob = (RectTransform)inner.Find("Knob");
            knob.anchorMin = new Vector2((float)selected / keys.Length, 0); knob.anchorMax = new Vector2((float)(selected + 1) / keys.Length, 1);
            var toggle = toggleGo.GetComponent<SegmentedToggle>();
            Wire(toggle, knob, options, labels, selected);
            return toggle;
        }
    }
}
