using System.IO;
using System.Linq;
using FlickCurling.Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/MainMenu.unity from scratch: the board art, the logo over its house, the six mode buttons (layout
    /// only; the buttons do nothing yet) and a hidden instance of the shared popup (PopupBuilder). The art (flat, casual) was generated with Codex (imagegen) and lives in
    /// Assets/Art/Menu.
    /// </summary>
    public static class MainMenuBuilder
    {
        public const string ScenePath = "Assets/Scenes/MainMenu.unity";
        const string BgPath = "Assets/Art/Menu/menu_bg.png", TopPath = "Assets/Art/Menu/menu_bg_top.png", BottomPath = "Assets/Art/Menu/menu_bg_bottom.png", LogoPath = "Assets/Art/Menu/menu_logo.png";

        // where the house sits in the board art, as a fraction of its height from the top; the logo is centred on it
        const float HouseY = 0.25f, LogoW = 820;

        // each button's icon (Codex), at the left end of the plank: its box and its centre's distance from that end
        const float IconSize = 84, IconInset = 78;

        // the button art's rim, in source pixels, kept unstretched by 9-slicing (left, bottom, right, top)
        // the button art: light wooden planks in a few shapes (Codex), each with its 9-slice border (left, bottom, right, top, in source pixels) and the
        // label colour; they take turns down the column so neighbouring buttons never match
        static readonly (string path, Vector4 border, Color32 text)[] Styles =
        {
            ("Assets/Art/Menu/menu_plank_1.png", new Vector4(120, 50, 120, 50), new Color32(0x4a, 0x2a, 0x12, 0xff)),   // straight
            ("Assets/Art/Menu/menu_plank_2.png", new Vector4(120, 50, 120, 50), new Color32(0x4a, 0x2a, 0x12, 0xff)),   // hand-cut ends
            ("Assets/Art/Menu/menu_plank_3.png", new Vector4(140, 60, 140, 60), new Color32(0x4a, 0x2a, 0x12, 0xff)),   // wavy, a chipped corner
        };

        // reference layout: a 1080 x 1920 portrait phone
        const float RefW = 1080, RefH = 1920, ButtonW = 760, ButtonH = 124, ButtonGap = 30, ButtonsBottom = 70;

        static readonly string[] Keys =
            { "mainmenu.offline", "mainmenu.online", "mainmenu.leaderboard", "mainmenu.rules", "mainmenu.shop", "mainmenu.settings" };

        [MenuItem("FlickCurling/Build Main Menu Scene")]
        public static void Build()
        {
            var bg = ImportSprite(BgPath, Vector4.zero);
            var woodTop = ImportSprite(TopPath, Vector4.zero);
            var woodBottom = ImportSprite(BottomPath, Vector4.zero);
            var styleSprites = System.Array.ConvertAll(Styles, st => ImportSprite(st.path, st.border));
            var logo = ImportSprite(LogoPath, Vector4.zero);
            var buttonFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.ButtonPath);   // Jua, Mochiy Pop One for Japanese

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x1a, 0x11, 0x0b, 0xff);
            camGo.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<AudioListener>();   // the menu's sounds (button clicks) need ears

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;   // the whole reference layout always fits
            canvasGo.AddComponent<GraphicRaycaster>();

            // the whole menu is one 9:16 frame kept in the middle of the screen: the house, logo and buttons always keep
            // the same size and spacing. The 3:4 board art is the frame's height (wide enough for a tablet, the sides are
            // only plain wood); on a taller phone the board carries on above and below the frame, with thin strips of the
            // art's own top and bottom edges stretched upward and downward (the grain runs that way, so they join cleanly)
            var safe = Child<RectTransform>(canvasGo.transform, "SafeArea");
            Stretch(safe);
            var frame = Child<RectTransform>(safe, "Frame");   // centred in the safe area
            Place(frame, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(RefW, RefH));
            float bgW = RefH * bg.rect.width / bg.rect.height;
            var above = Child<Image>(frame, "WoodAbove");
            Edge(above.rectTransform, 1, bgW); above.sprite = woodTop; above.raycastTarget = false;
            var below = Child<Image>(frame, "WoodBelow");
            Edge(below.rectTransform, 0, bgW); below.sprite = woodBottom; below.raycastTarget = false;
            var bgImg = Child<Image>(frame, "Background");
            Place(bgImg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(bgW, RefH));
            bgImg.sprite = bg; bgImg.raycastTarget = false;
            var shade = Child<Image>(frame, "Shade");
            Place(shade.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(bgW, RefH + 2 * Overflow));
            shade.color = new Color(0.08f, 0.05f, 0.03f, 0.12f); shade.raycastTarget = false;


            // the logo sits on the house in the board art
            var logoImg = Child<Image>(bgImg.transform, "Logo");
            logoImg.sprite = logo; logoImg.preserveAspect = true; logoImg.raycastTarget = false;
            Place(logoImg.rectTransform, new Vector2(0.5f, 1 - HouseY), Vector2.zero, new Vector2(LogoW, LogoW * logo.rect.height / logo.rect.width));

            // the button column, in the calm lower half of the frame below the house
            var list = Child<RectTransform>(frame, "Buttons");
            float listH = Keys.Length * ButtonH + (Keys.Length - 1) * ButtonGap;
            Place(list, new Vector2(0.5f, 0), new Vector2(0, ButtonsBottom + listH / 2), new Vector2(ButtonW, listH));
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = ButtonGap; v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var entries = new MainMenu.Entry[Keys.Length];
            for (int i = 0; i < Keys.Length; i++)
            {
                var img = Child<Image>(list, "Btn_" + Keys[i].Substring("mainmenu.".Length));
                int style = i % Styles.Length;
                var btn = styleSprites[style];
                img.sprite = btn; img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = btn.rect.height / ButtonH;   // the rim keeps the art's proportions at this height
                var le = img.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = le.minHeight = ButtonH;
                var b = img.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                var cb = b.colors; cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f); cb.pressedColor = new Color(.82f, .82f, .82f); b.colors = cb;
                var label = Label(img.transform, "Label", buttonFont, 50, Styles[style].text);
                Stretch(label.rectTransform);
                // localized by Unity Localization: the label follows the selected language by itself (and can be
                // previewed in the editor); the Korean text is also saved so it reads before the table loads
                label.text = UiStrings.Entries.First(e => e[0] == Keys[i])[1];
                LocalizeEvents.BindText(label, Loc.Table, Keys[i]);   // Editor And Runtime
                // the second round of planks is mirrored, so no two buttons look the same; the label flips back
                bool mirrored = (i / Styles.Length) % 2 == 1;
                if (mirrored) { img.rectTransform.localScale = new Vector3(-1, 1, 1); label.rectTransform.localScale = new Vector3(-1, 1, 1); }

                // the icon at the left end, the label centred on the plank; on a mirrored plank the icon is pinned to
                // the far end and flipped back, so it still shows on the left the right way round
                var icon = Child<Image>(img.transform, "Icon");
                icon.sprite = ImportSprite(IconPath(Keys[i]), Vector4.zero, 256);
                icon.preserveAspect = true; icon.raycastTarget = false;
                var irt = icon.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(mirrored ? 1 : 0, 0.5f); irt.pivot = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = new Vector2(mirrored ? -IconInset : IconInset, 3);
                irt.sizeDelta = new Vector2(IconSize, IconSize);
                if (mirrored) irt.localScale = new Vector3(-1, 1, 1);
                label.rectTransform.offsetMin = new Vector2(0, 6);   // the face sits a little above the bottom lip
                label.raycastTarget = false;
                entries[i] = new MainMenu.Entry { Key = Keys[i], Label = label, Button = b };
            }

            // the shared popup, over everything (its backdrop covers the whole screen) and hidden until a menu opens it
            UiPrefabBuilder.Build();
            var popupPrefab = PopupBuilder.Build();
            var popup = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab, canvasGo.transform);
            popup.SetActive(false);
            var matchSetup = BuildMatchSetup(popupPrefab, canvasGo.transform, buttonFont);
            var shop = BuildShop(popupPrefab, canvasGo.transform);
            var settingsPrefab = BuildSettingsPrefab(popupPrefab);
            var settingsGo = (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab, canvasGo.transform);
            settingsGo.SetActive(false);
            var settingsPanel = settingsGo.GetComponent<SettingsPanel>();
            BuildLinkConflictPrefab(popupPrefab);   // loaded when needed, over the settings

            var menu = canvasGo.AddComponent<MainMenu>();
            var so = new SerializedObject(menu);
            so.FindProperty("safeArea").objectReferenceValue = safe;
            so.FindProperty("popup").objectReferenceValue = popup.GetComponent<Popup>();
            so.FindProperty("matchSetup").objectReferenceValue = matchSetup;
            so.FindProperty("settings").objectReferenceValue = settingsPanel;
            so.FindProperty("shop").objectReferenceValue = shop;
            var arr = so.FindProperty("entries");
            arr.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Key").stringValue = entries[i].Key;
                e.FindPropertyRelative("Label").objectReferenceValue = entries[i].Label;
                e.FindPropertyRelative("Button").objectReferenceValue = entries[i].Button;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            // listed in the build after the game scene; the game still starts as before until the menu is wired up
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("FlickCurling: built " + ScenePath);
        }

        // ---- the match setup popup (offline match): the mode (against the AI / local two players, each with its icon),
        // against the AI its level, and the map as group toggles, and OK, all from the shared prefabs (Popup, ToggleGroup,
        // Button_Primary) ----

        const float SetupLegendH = 52, SetupToggleH = 96, SetupGap = 22, SetupOkW = 380, SetupOkH = 112;
        static readonly Color32 Ink = new Color32(0x4a, 0x2a, 0x12, 0xff);

        static MatchSetup BuildMatchSetup(GameObject popupPrefab, Transform parent, TMP_FontAsset buttonFont)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab, parent);
            go.name = "MatchSetupPopup";
            var legendFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            // titled like the menu button, with its icon
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "mainmenu.offline")[1];
            LocalizeEvents.BindText(title, Loc.Table, "mainmenu.offline");
            var icon = go.transform.Find("Window/TitleRow/Icon").GetComponent<Image>();
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath("mainmenu.offline"));
            icon.gameObject.SetActive(true);

            var content = (RectTransform)go.transform.Find("Window/Content");
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = SetupGap; v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var modeRow = Row(content, "ModeRow");
            Legend(modeRow, legendFont, "matchsetup.mode");
            var mode = Toggle(modeRow, new[] { "matchsetup.vsAi", "matchsetup.vsLocal" }, 0,
                new[] { AssetDatabase.LoadAssetAtPath<Sprite>(IconAi), AssetDatabase.LoadAssetAtPath<Sprite>(IconLocal) });
            // the level right under the mode it belongs to, then the map
            var levelRow = Row(content, "LevelRow");
            Legend(levelRow, legendFont, "matchsetup.level");
            var level = Toggle(levelRow, new[] { "level.easy", "level.normal", "level.hard" }, 1);
            var mapRow = Row(content, "MapRow");
            Legend(mapRow, legendFont, "matchsetup.map");
            var map = Toggle(mapRow, new[] { "map.base" }, 0);

            // OK, centred under the toggles; the space above it soaks up the rest of the window
            var spacer = Child<RectTransform>(content, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var okRow = Child<RectTransform>(content, "OkRow");
            okRow.gameObject.AddComponent<LayoutElement>().preferredHeight = SetupOkH;
            var okGo = UiPrefabBuilder.Place(UiPrefabBuilder.PrimaryPath, okRow, "Ok");
            Place((RectTransform)okGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SetupOkW, SetupOkH));
            var ok = UiPrefabBuilder.SetLabel(okGo, "matchsetup.ok");

            var setup = go.AddComponent<MatchSetup>();
            var so = new SerializedObject(setup);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("mode").objectReferenceValue = mode;
            so.FindProperty("map").objectReferenceValue = map;
            so.FindProperty("levelRow").objectReferenceValue = levelRow.gameObject;
            so.FindProperty("level").objectReferenceValue = level;
            so.FindProperty("ok").objectReferenceValue = ok;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            return setup;
        }

        // ---- the settings popup: music, sound effects and aim sensitivity sliders (with their values) and the language
        // dropdown, one row each, from the shared prefabs (Popup, Slider, Dropdown) ----

        const float SettingsToMenuW = 460;
        const float SettingsW = 860, SettingsH = 1000, SettingsRowH = 92, SettingsLabelW = 230, SettingsValueW = 80;

        // ---- the shop popup: the products as cards hanging from nails, three in a row (three test products for now) ----

        // the gap leaves room for the cards' tilt: two neighbours leaning toward each other never touch. The window shows two
        // full rows (six products) and the top of the third, a hint that the list goes on
        const float ShopGap = 34, ShopRowGap = 16, ShopPeek = 46;
        const float ShopW = 880, ShopH = 135 + 70 + 8 + 2 * UiPrefabBuilder.ShopCardH + ShopRowGap + ShopPeek;

        static Popup BuildShop(GameObject popupPrefab, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab, parent);
            go.name = "ShopPopup";
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "mainmenu.shop")[1];
            LocalizeEvents.BindText(title, Loc.Table, "mainmenu.shop");
            var icon = go.transform.Find("Window/TitleRow/Icon").GetComponent<Image>();
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath("mainmenu.shop"));
            icon.gameObject.SetActive(true);
            ((RectTransform)go.transform.Find("Window")).sizeDelta = new Vector2(ShopW, ShopH);

            // the products scroll (the shared ScrollView) when there are more than fit
            var area = (RectTransform)go.transform.Find("Window/Content");
            var scroll = UiPrefabBuilder.Place(UiPrefabBuilder.ScrollViewPath, area, "Products");
            Stretch((RectTransform)scroll.transform);
            // no scrollbar here: the cut-off row below says there is more
            var sr = scroll.GetComponent<ScrollRect>();
            sr.verticalScrollbar = null;
            scroll.transform.Find("Scrollbar").gameObject.SetActive(false);
            var content = (RectTransform)scroll.transform.Find("Viewport/Content");
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(UiPrefabBuilder.ShopCardW, UiPrefabBuilder.ShopCardH);
            grid.spacing = new Vector2(ShopGap, ShopRowGap);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            grid.padding = new RectOffset(0, 0, 8, 12);

            foreach (var (key, art, price) in new[] { ("shop.item.coins", "shop_item_coins", 100), ("shop.item.goldStone", "shop_item_goldstone", 500), ("shop.item.chest", "shop_item_chest", 1200) })
            {
                var card = UiPrefabBuilder.Place(UiPrefabBuilder.ShopCardPath, content, "Card_" + art.Substring("shop_item_".Length));
                UiPrefabBuilder.SetShopCard(card, key, ImportSprite("Assets/Art/Menu/" + art + ".png", Vector4.zero, 512), price);
            }
            go.SetActive(false);
            return go.GetComponent<Popup>();
        }

        public const string SettingsPrefabPath = "Assets/Resources/" + SettingsPanel.ResourcePath + ".prefab";   // the game loads it too

        static GameObject BuildSettingsPrefab(GameObject popupPrefab)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab);
            go.name = "SettingsPopup";
            var legendFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "settings.button")[1];
            LocalizeEvents.BindText(title, Loc.Table, "settings.button");
            // the menu button's gear beside the title
            var icon = go.transform.Find("Window/TitleRow/Icon").GetComponent<Image>();
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath("mainmenu.settings"));
            icon.gameObject.SetActive(true);
            ((RectTransform)go.transform.Find("Window")).sizeDelta = new Vector2(SettingsW, SettingsH);

            var content = (RectTransform)go.transform.Find("Window/Content");
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 18; v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            // the account first: PID, nickname, Google sign-in (or that it is linked), and a status line
            BuildAccountRow(content, legendFont);
            var (music, musicValue) = SliderRow(content, legendFont, "settings.music", 0, 1, false);
            var (sfx, sfxValue) = SliderRow(content, legendFont, "settings.sfx", 0, 1, false);
            var (sens, sensValue) = SliderRow(content, legendFont, "settings.aimSens", 1, GameSettings.SensLevels, true);
            var langRow = SettingsRow(content, "Row_language", legendFont, "settings.language");
            var dd = UiPrefabBuilder.Place(UiPrefabBuilder.DropdownPath, langRow, "Language");
            var dle = dd.AddComponent<LayoutElement>(); dle.flexibleWidth = 1; dle.preferredHeight = 82;
            var lang = dd.GetComponent<TMP_Dropdown>();

            // in the game only: back to the main menu, at the bottom
            var spacer = Child<RectTransform>(content, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var toMenuRow = Child<RectTransform>(content, "ToMenuRow");
            toMenuRow.gameObject.AddComponent<LayoutElement>().preferredHeight = SetupOkH;
            var toMenuGo = UiPrefabBuilder.Place(UiPrefabBuilder.SecondaryPath, toMenuRow, "ToMenu");
            Place((RectTransform)toMenuGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SettingsToMenuW, SetupOkH));
            var toMenu = UiPrefabBuilder.SetLabel(toMenuGo, "settings.toMenu");

            var panel = go.AddComponent<SettingsPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("music").objectReferenceValue = music; so.FindProperty("musicValue").objectReferenceValue = musicValue;
            so.FindProperty("sfx").objectReferenceValue = sfx; so.FindProperty("sfxValue").objectReferenceValue = sfxValue;
            so.FindProperty("sens").objectReferenceValue = sens; so.FindProperty("sensValue").objectReferenceValue = sensValue;
            so.FindProperty("language").objectReferenceValue = lang;
            so.FindProperty("toMenuRow").objectReferenceValue = toMenuRow.gameObject;
            so.FindProperty("toMenu").objectReferenceValue = toMenu;
            so.FindProperty("Icon").objectReferenceValue = icon.sprite;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, SettingsPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        public const string LinkConflictPrefabPath = "Assets/Resources/" + FlickCurling.Game.Account.LinkConflictPopup.ResourcePath + ".prefab";
        const float LinkW = 860, LinkH = 1010, LinkCardH = 196, LinkButtonW = 560;

        // the account conflict choice: a message, the account's record and this one in account boxes, and the two ways
        // out; then a confirmation page (its message filled at run time) with confirm and back
        static void BuildLinkConflictPrefab(GameObject popupPrefab)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab);
            go.name = "LinkConflictPopup";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            var valueFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.ButtonPath);
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "link.title")[1];
            LocalizeEvents.BindText(title, Loc.Table, "link.title");
            ((RectTransform)go.transform.Find("Window")).sizeDelta = new Vector2(LinkW, LinkH);
            var content = (RectTransform)go.transform.Find("Window/Content");

            RectTransform Page(string name, float spacing)
            {
                var page = Child<RectTransform>(content, name);
                Stretch(page);
                var v = page.gameObject.AddComponent<VerticalLayoutGroup>();
                v.spacing = spacing; v.childAlignment = TextAnchor.UpperCenter;
                v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
                return page;
            }
            TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset f, float size, Color32 color, string key = null)
            {
                var t = Label(parent, name, f, size, color);
                t.textWrappingMode = TextWrappingModes.Normal;
                if (key != null) { t.text = UiStrings.Entries.First(e => e[0] == key)[1]; LocalizeEvents.BindText(t, Loc.Table, key); }
                else t.text = "-";
                return t;
            }
            Button Wide(Transform parent, string prefabPath, string name, string key)
            {
                var row = Child<RectTransform>(parent, name + "Row");
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = SetupOkH;
                var b = UiPrefabBuilder.Place(prefabPath, row, name);
                Place((RectTransform)b.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(LinkButtonW, SetupOkH));
                return UiPrefabBuilder.SetLabel(b, key);
            }

            // the choice
            var choice = Page("Choice", 18);
            Text(choice, "Message", font, 34, Ink, "link.message");
            (TextMeshProUGUI name, TextMeshProUGUI pid, TextMeshProUGUI date) Card(string name, string headerKey)
            {
                var box = Child<Image>(choice, name);
                box.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Menu/account_box.png");
                box.type = Image.Type.Sliced; box.pixelsPerUnitMultiplier = 0.75f;
                box.gameObject.AddComponent<LayoutElement>().preferredHeight = LinkCardH;
                var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
                v.padding = new RectOffset(AccountPadL, AccountPadR - 20, 28, 24); v.spacing = 2; v.childAlignment = TextAnchor.MiddleLeft;
                v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
                var header = Text(box.transform, "Header", font, 28, Ink, headerKey); header.alignment = TextAlignmentOptions.MidlineLeft;
                var n = Text(box.transform, "Name", valueFont, 34, Ink); n.alignment = TextAlignmentOptions.MidlineLeft;
                var p = Text(box.transform, "Pid", valueFont, 22, AccountValueInk); p.alignment = TextAlignmentOptions.MidlineLeft;
                var d = Text(box.transform, "Date", valueFont, 22, AccountValueInk); d.alignment = TextAlignmentOptions.MidlineLeft;
                return (n, p, d);
            }
            var existing = Card("Existing", "link.existing");
            var current = Card("Current", "link.current");
            Child<RectTransform>(choice, "Gap").gameObject.AddComponent<LayoutElement>().preferredHeight = 6;
            var load = Wide(choice, UiPrefabBuilder.PrimaryPath, "Load", "link.load");
            var keep = Wide(choice, UiPrefabBuilder.SecondaryPath, "Keep", "link.keep");

            // are you sure
            var confirmPage = Page("Confirm", 22);
            var spacer = Child<RectTransform>(confirmPage, "Top"); spacer.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            var message = Text(confirmPage, "Message", font, 34, Ink);
            message.gameObject.AddComponent<LayoutElement>().minHeight = 300;
            var confirm = Wide(confirmPage, UiPrefabBuilder.PrimaryPath, "Confirm", "link.confirm");
            var back = Wide(confirmPage, UiPrefabBuilder.SecondaryPath, "Back", "link.back");
            confirmPage.gameObject.SetActive(false);

            var panel = go.AddComponent<FlickCurling.Game.Account.LinkConflictPopup>();
            var so = new SerializedObject(panel);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("choicePage").objectReferenceValue = choice.gameObject;
            so.FindProperty("confirmPage").objectReferenceValue = confirmPage.gameObject;
            so.FindProperty("existingName").objectReferenceValue = existing.name;
            so.FindProperty("existingPid").objectReferenceValue = existing.pid;
            so.FindProperty("existingDate").objectReferenceValue = existing.date;
            so.FindProperty("currentName").objectReferenceValue = current.name;
            so.FindProperty("currentPid").objectReferenceValue = current.pid;
            so.FindProperty("currentDate").objectReferenceValue = current.date;
            so.FindProperty("confirmMessage").objectReferenceValue = message;
            so.FindProperty("load").objectReferenceValue = load;
            so.FindProperty("keep").objectReferenceValue = keep;
            so.FindProperty("confirm").objectReferenceValue = confirm;
            so.FindProperty("back").objectReferenceValue = back;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(go, LinkConflictPrefabPath);
            Object.DestroyImmediate(go);
        }

        const float AccountStatusH = 30, GoogleButtonW = 520, GoogleButtonH = 96;
        const float AccountLabelSize = 30, AccountValueSize = 26;
        static readonly Color32 AccountValueInk = new Color32(0x86, 0x61, 0x3d, 0xff);   // lighter than the labels
        const int AccountTextGap = 12;   // the empty line space under the nickname's letters, counted in the gap above the button
        // the box's padding (left, right, top, bottom), the right one wider so long values wrap clear of the rim, and the
        // gap above the button, which matches the one under it to the rim's inner edge (the rim is about AccountRim thick)
        const int AccountPadL = 66, AccountPadR = 84, AccountPadT = 46, AccountPadB = 44, AccountRim = 12;

        // the account, boxed so it reads as one thing: the PID, the nickname, then "Sign in with Google" (or, in its
        // place, that the account is linked); under the box a status line while connecting or after a failure. The box
        // takes the height of what is in it, so a value that wraps makes it taller.
        static void BuildAccountRow(RectTransform parent, TMP_FontAsset font)
        {
            var row = Child<RectTransform>(parent, "Account");
            var rv = row.gameObject.AddComponent<VerticalLayoutGroup>();
            rv.spacing = 6; rv.childAlignment = TextAnchor.UpperCenter;
            rv.childControlWidth = rv.childControlHeight = true; rv.childForceExpandWidth = true; rv.childForceExpandHeight = false;

            var box = Child<Image>(row, "Box");
            box.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Menu/account_box.png");
            box.type = Image.Type.Sliced; box.pixelsPerUnitMultiplier = 0.75f;
            var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(AccountPadL, AccountPadR, AccountPadT, AccountPadB);
            v.spacing = AccountPadB - AccountRim - AccountTextGap; v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            // PID and nickname, set the same way: the label in bold Noto Sans, a little larger, then the value in Jua,
            // wrapping when long, centred on the label's height
            var info = Child<RectTransform>(box.transform, "Info");
            var iv = info.gameObject.AddComponent<VerticalLayoutGroup>();
            iv.spacing = 8; iv.childControlWidth = iv.childControlHeight = true; iv.childForceExpandWidth = true; iv.childForceExpandHeight = false;
            var valueFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.ButtonPath);
            TextMeshProUGUI Line(string name, string key)
            {
                var line = Child<RectTransform>(info, name);
                var h = line.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 14; h.childAlignment = TextAnchor.MiddleLeft;
                h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
                var label = Label(line, "Label", font, AccountLabelSize, Ink);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.text = UiStrings.Entries.First(e => e[0] == key)[1];
                LocalizeEvents.BindText(label, Loc.Table, key);
                var value = Label(line, "Value", valueFont, AccountValueSize, AccountValueInk);
                value.alignment = TextAlignmentOptions.MidlineLeft;
                value.textWrappingMode = TextWrappingModes.Normal;
                // asks for no width of its own, only the rest of the line, so the label always keeps its full width
                var vle = value.gameObject.AddComponent<LayoutElement>(); vle.preferredWidth = 0; vle.flexibleWidth = 1;
                value.text = "-";
                return value;
            }
            var pid = Line("Pid", "account.pid");
            var nickname = Line("Nickname", "account.nickname");

            // Google: the sign-in button, or in its place that the account is linked
            var slot = Child<RectTransform>(box.transform, "Google");
            var sle = slot.gameObject.AddComponent<LayoutElement>(); sle.preferredHeight = sle.minHeight = GoogleButtonH;
            var button = UiPrefabBuilder.Place(UiPrefabBuilder.GooglePath, slot, "SignIn");
            Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(GoogleButtonW, GoogleButtonH));
            var signIn = UiPrefabBuilder.SetLabel(button, "account.googleLogin");
            var linked = Label(slot, "Linked", font, 34, new Color32(0x3f, 0x6e, 0x2a, 0xff));
            Stretch(linked.rectTransform);
            linked.enableAutoSizing = true; linked.fontSizeMin = 20; linked.fontSizeMax = 34;
            linked.text = UiStrings.Entries.First(e => e[0] == "account.googleLinked")[1];
            LocalizeEvents.BindText(linked, Loc.Table, "account.googleLinked");
            linked.gameObject.SetActive(false);

            var status = Label(row, "Status", font, 24, new Color32(0xa3, 0x4a, 0x2a, 0xff));
            status.enableAutoSizing = true; status.fontSizeMin = 18; status.fontSizeMax = 24;
            status.gameObject.AddComponent<LayoutElement>().preferredHeight = AccountStatusH;
            status.text = "";

            var panel = row.gameObject.AddComponent<FlickCurling.Game.Account.AccountPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("pid").objectReferenceValue = pid;
            so.FindProperty("nickname").objectReferenceValue = nickname;
            so.FindProperty("status").objectReferenceValue = status;
            so.FindProperty("googleButton").objectReferenceValue = signIn;
            so.FindProperty("googleLinked").objectReferenceValue = linked.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // a settings row: the label on the left, then the control
        static RectTransform SettingsRow(RectTransform parent, string name, TMP_FontAsset font, string key)
        {
            var row = Child<RectTransform>(parent, name);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = SettingsRowH;
            var t = Label(row, "Label", font, 38, Ink);
            t.alignment = TextAlignmentOptions.Left;
            t.enableAutoSizing = true; t.fontSizeMin = 26; t.fontSizeMax = 38;
            t.text = UiStrings.Entries.First(e => e[0] == key)[1];
            LocalizeEvents.BindText(t, Loc.Table, key);
            var le = t.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = le.minWidth = SettingsLabelW; le.preferredHeight = SettingsRowH;
            return row;
        }

        static (Slider, TextMeshProUGUI) SliderRow(RectTransform parent, TMP_FontAsset font, string key, float min, float max, bool whole)
        {
            var row = SettingsRow(parent, "Row_" + key.Substring("settings.".Length), font, key);
            var go = UiPrefabBuilder.Place(UiPrefabBuilder.SliderPath, row, "Slider");
            var le = go.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.preferredHeight = 76;
            var s = go.GetComponent<Slider>();
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole; s.value = max;
            var value = Label(row, "Value", font, 36, Ink);
            value.alignment = TextAlignmentOptions.Right;
            var vle = value.gameObject.AddComponent<LayoutElement>(); vle.preferredWidth = vle.minWidth = SettingsValueW; vle.preferredHeight = SettingsRowH;
            return (s, value);
        }

        static RectTransform Row(RectTransform parent, string name)
        {
            var row = Child<RectTransform>(parent, name);
            var v = row.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6; v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return row;
        }

        static void Legend(RectTransform row, TMP_FontAsset font, string key)
        {
            var t = Label(row, "Legend", font, 36, Ink);
            t.alignment = TextAlignmentOptions.Left;
            t.text = UiStrings.Entries.First(e => e[0] == key)[1];
            LocalizeEvents.BindText(t, Loc.Table, key);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = SetupLegendH;
        }

        // a group toggle (the shared prefab) with one option per key, filling the row's width
        static SegmentedToggle Toggle(RectTransform row, string[] keys, int selected, Sprite[] icons = null)
        {
            var go = UiPrefabBuilder.Place(UiPrefabBuilder.TogglePath, row, "Toggle");
            go.AddComponent<LayoutElement>().preferredHeight = SetupToggleH;
            return UiPrefabBuilder.SetOptions(go, keys, selected, icons);
        }

        // the modes' icons (the old vs AI and local menu buttons' art)
        const string IconAi = "Assets/Art/Menu/icon_ai.png", IconLocal = "Assets/Art/Menu/icon_local.png";

        static string IconPath(string key) => "Assets/Art/Menu/icon_" + key.Substring("mainmenu.".Length) + ".png";

        internal static Sprite ImportSprite(string path, Vector4 border, int maxSize = 2048)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) throw new System.InvalidOperationException("FlickCurling: missing art " + path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.maxTextureSize = maxSize;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteBorder = border;
            ti.SetTextureSettings(settings);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        internal static T Child<T>(Transform parent, string name) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return typeof(T) == typeof(RectTransform) ? (T)(Component)go.GetComponent<RectTransform>() : go.AddComponent<T>();
        }

        internal static TextMeshProUGUI Label(Transform parent, string name, TMP_FontAsset font, float size, Color color)
        {
            var t = Child<TextMeshProUGUI>(parent, name);
            t.font = font; t.fontSize = size; t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        // how far the wood carries on past the frame, enough for the tallest phones
        const float Overflow = 2400;

        // a strip of wood running from the frame's top (side 1) or bottom (side 0) edge outward
        static void Edge(RectTransform rt, int side, float width)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, side);
            rt.pivot = new Vector2(0.5f, 1 - side);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(width, Overflow);
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        internal static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }
    }
}
