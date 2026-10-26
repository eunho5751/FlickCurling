using System.IO;
using System.Linq;
using FlickCurling.Game;
using FlickCurling.Game.Online;
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
            // the new scene first: opening it unloads the assets nothing holds yet, and right after a script reload that
            // includes what was just loaded here (TMP's caches are empty then), which left the labels without their font
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bg = ImportSprite(BgPath, Vector4.zero);
            var woodTop = ImportSprite(TopPath, Vector4.zero);
            var woodBottom = ImportSprite(BottomPath, Vector4.zero);
            var styleSprites = System.Array.ConvertAll(Styles, st => ImportSprite(st.path, st.border));
            var logo = ImportSprite(LogoPath, Vector4.zero);
            var buttonFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.ButtonPath);   // Jua, Mochiy Pop One for Japanese

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
            var onlineSetup = BuildOnlineSetup(popupPrefab, canvasGo.transform);
            var shop = BuildShop(popupPrefab, canvasGo.transform, popup.GetComponent<Popup>());
            var leaderboard = BuildLeaderboard(popupPrefab, canvasGo.transform);
            var rulesGo = (GameObject)PrefabUtility.InstantiatePrefab(BuildRulesPrefab(popupPrefab), canvasGo.transform);
            rulesGo.SetActive(false);
            var settingsPrefab = BuildSettingsPrefab(popupPrefab);
            var settingsGo = (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab, canvasGo.transform);
            settingsGo.SetActive(false);
            var settingsPanel = settingsGo.GetComponent<SettingsPanel>();
            BuildLinkConflictPrefab(popupPrefab);   // loaded when needed, over the settings
            BuildNicknamePrefab(popupPrefab);   // loaded when a new player needs a nickname
            BuildDevicePrefab(popupPrefab);     // loaded when the account is signed in on another device
            BuildNetworkPrefab(popupPrefab);    // loaded while the network (or signing in) is down

            var menu = canvasGo.AddComponent<MainMenu>();
            var so = new SerializedObject(menu);
            so.FindProperty("safeArea").objectReferenceValue = safe;
            so.FindProperty("popup").objectReferenceValue = popup.GetComponent<Popup>();
            so.FindProperty("matchSetup").objectReferenceValue = matchSetup;
            so.FindProperty("onlineSetup").objectReferenceValue = onlineSetup;
            so.FindProperty("settings").objectReferenceValue = settingsPanel;
            so.FindProperty("shop").objectReferenceValue = shop;
            so.FindProperty("leaderboard").objectReferenceValue = leaderboard;
            so.FindProperty("rules").objectReferenceValue = rulesGo.GetComponent<RulesPopup>();
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

        // ---- the online match popup: the mode (random match) and the map as group toggles, and OK (which turns into the
        // search's elapsed time), laid out like the match setup popup ----

        static readonly Vector2 OnlineSetupSize = new Vector2(860, 764);

        static OnlineSetup BuildOnlineSetup(GameObject popupPrefab, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab, parent);
            go.name = "OnlineSetupPopup";
            var legendFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "mainmenu.online")[1];
            LocalizeEvents.BindText(title, Loc.Table, "mainmenu.online");
            var icon = go.transform.Find("Window/TitleRow/Icon").GetComponent<Image>();
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath("mainmenu.online"));
            icon.gameObject.SetActive(true);
            ((RectTransform)go.transform.Find("Window")).sizeDelta = OnlineSetupSize;

            var content = (RectTransform)go.transform.Find("Window/Content");
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = SetupGap; v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var modeRow = Row(content, "ModeRow");
            Legend(modeRow, legendFont, "matchsetup.mode");
            var mode = Toggle(modeRow, new[] { "online.random" }, 0);
            var mapRow = Row(content, "MapRow");
            Legend(mapRow, legendFont, "matchsetup.map");
            var map = Toggle(mapRow, new[] { "map.base" }, 0);

            var spacer = Child<RectTransform>(content, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var okRow = Child<RectTransform>(content, "OkRow");
            okRow.gameObject.AddComponent<LayoutElement>().preferredHeight = SetupOkH;
            var okGo = UiPrefabBuilder.Place(UiPrefabBuilder.PrimaryPath, okRow, "Ok");
            Place((RectTransform)okGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SetupOkW, SetupOkH));
            var ok = UiPrefabBuilder.SetLabel(okGo, "matchsetup.ok");

            var setup = go.AddComponent<OnlineSetup>();
            var so = new SerializedObject(setup);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("mode").objectReferenceValue = mode;
            so.FindProperty("map").objectReferenceValue = map;
            so.FindProperty("ok").objectReferenceValue = ok;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            return setup;
        }

        // ---- the settings popup: music, sound effects and aim sensitivity sliders (with their values) and the language
        // dropdown, one row each, from the shared prefabs (Popup, Slider, Dropdown) ----

        const float SettingsToMenuW = 460;
        const float SettingsW = 860, SettingsH = 1000, SettingsRowH = 92, SettingsLabelW = 230, SettingsValueW = 80;

        // ---- the shop popup: the products as cards hanging from nails, three in a row. The cards are made when it opens,
        // one per product of the shop catalog (Resources/ShopCatalog), from a card template here ----

        // the gap leaves room for the cards' tilt: two neighbours leaning toward each other never touch. The window shows two
        // full rows (six products) and the top of the third, a hint that the list goes on
        const float ShopGap = 34, ShopRowGap = 16, ShopPeek = 46;
        const float ShopW = 880, ShopH = 135 + 70 + 8 + 2 * UiPrefabBuilder.ShopCardH + ShopRowGap + ShopPeek;

        const float ShopTiltRoom = 16;   // a card tilted 2.8° reaches about 9 px past its cell

        static ShopPanel BuildShop(GameObject popupPrefab, Transform parent, Popup message)
        {
            EnsureShopCatalog();
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
            // a little wider than the cards, so a tilted card's corners are not clipped at the sides
            ((RectTransform)scroll.transform).offsetMin = new Vector2(-ShopTiltRoom, 0);
            ((RectTransform)scroll.transform).offsetMax = new Vector2(ShopTiltRoom, 0);
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

            var template = UiPrefabBuilder.Place(UiPrefabBuilder.ShopCardPath, content, "CardTemplate");
            template.SetActive(false);

            // iOS only (ShopPanel shows it, making the window that much taller): restore purchases, under the cards
            var restoreRow = Child<RectTransform>(area, "RestoreRow");
            restoreRow.anchorMin = new Vector2(0, 0); restoreRow.anchorMax = new Vector2(1, 0); restoreRow.pivot = new Vector2(0.5f, 0);
            restoreRow.anchoredPosition = Vector2.zero; restoreRow.sizeDelta = new Vector2(0, SetupOkH);
            var restoreGo = UiPrefabBuilder.Place(UiPrefabBuilder.SecondaryPath, restoreRow, "Restore");
            Place((RectTransform)restoreGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SettingsToMenuW, SetupOkH));
            var restore = UiPrefabBuilder.SetLabel(restoreGo, "shop.restore");
            restoreRow.gameObject.SetActive(false);

            var panel = go.AddComponent<ShopPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("cardTemplate").objectReferenceValue = template.GetComponent<ShopCard>();
            so.FindProperty("message").objectReferenceValue = message;
            so.FindProperty("restoreRow").objectReferenceValue = restoreRow.gameObject;
            so.FindProperty("restore").objectReferenceValue = restore;
            so.FindProperty("products").objectReferenceValue = scroll.transform;
            so.FindProperty("size").vector2Value = new Vector2(ShopW, ShopH);
            so.FindProperty("restoreRoom").floatValue = SetupOkH + 28;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            return panel;
        }

        // the shop's products (ScriptableObjects in Assets/Data/Shop) and their catalog, made once; kept as edited after
        const string ShopDataDir = "Assets/Data/Shop";

        static void EnsureShopCatalog()
        {
            string catalogPath = "Assets/Resources/" + ShopCatalog.ResourcePath + ".asset";
            if (AssetDatabase.LoadAssetAtPath<ShopCatalog>(catalogPath)) return;
            Directory.CreateDirectory(ShopDataDir);
            var removeAds = ScriptableObject.CreateInstance<ShopProduct>();
            removeAds.id = Purchases.RemoveAdsId;
            removeAds.type = UnityEngine.Purchasing.ProductType.NonConsumable;
            removeAds.displayName = new UnityEngine.Localization.LocalizedString(Loc.Table, "shop.item.removeAds");
            removeAds.purchasedMessage = new UnityEngine.Localization.LocalizedString(Loc.Table, "shop.removeAds.done");
            removeAds.icon = ImportSprite("Assets/Art/Menu/shop_item_noads.png", Vector4.zero, 512);
            AssetDatabase.CreateAsset(removeAds, ShopDataDir + "/RemoveAds.asset");
            var catalog = ScriptableObject.CreateInstance<ShopCatalog>();
            catalog.products.Add(removeAds);
            AssetDatabase.CreateAsset(catalog, catalogPath);
            AssetDatabase.SaveAssets();
        }

        // ---- the leaderboard popup: this player's rank, points and win rate in a box at the top (or why there are none, or
        // for a guest the Google button), then the column titles and the top 50 in the shared scroll view, one plate per
        // row; the medals and plates are Codex art in the menu's style ----

        const float BoardW = 900, BoardH = 1560, BoardMineH = 210, BoardHeadH = 54, BoardRowH = 100, BoardRowGap = 10;
        const float ColRank = 120, ColPoints = 150, ColRate = 130, ColPad = 26, MedalSize = 76;
        static readonly Color32 MineInk = new Color32(0x6b, 0x34, 0x08, 0xff);
        const string LbArt = "Assets/Art/Menu/lb_";

        static FlickCurling.Game.Online.LeaderboardPopup BuildLeaderboard(GameObject popupPrefab, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab, parent);
            go.name = "LeaderboardPopup";
            var legendFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            var valueFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.ButtonPath);
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "mainmenu.leaderboard")[1];
            LocalizeEvents.BindText(title, Loc.Table, "mainmenu.leaderboard");
            var icon = go.transform.Find("Window/TitleRow/Icon").GetComponent<Image>();
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath("mainmenu.leaderboard"));
            icon.gameObject.SetActive(true);
            ((RectTransform)go.transform.Find("Window")).sizeDelta = new Vector2(BoardW, BoardH);

            var content = (RectTransform)go.transform.Find("Window/Content");
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 14; v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            // this player, boxed like the account in the settings: three figures, or a line (and the Google button)
            var box = Child<Image>(content, "Mine");
            box.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Menu/account_box.png");
            box.type = Image.Type.Sliced; box.pixelsPerUnitMultiplier = 0.75f;
            box.gameObject.AddComponent<LayoutElement>().preferredHeight = BoardMineH;
            var stats = Child<RectTransform>(box.transform, "Stats");
            Stretch(stats); stats.offsetMin = new Vector2(40, 24); stats.offsetMax = new Vector2(-40, -24);
            var sh = stats.gameObject.AddComponent<HorizontalLayoutGroup>();
            sh.childControlWidth = sh.childControlHeight = true; sh.childForceExpandWidth = sh.childForceExpandHeight = true;
            TextMeshProUGUI Figure(string name, string key)
            {
                var col = Child<RectTransform>(stats, name);
                var cv = col.gameObject.AddComponent<VerticalLayoutGroup>();
                cv.spacing = 4; cv.childAlignment = TextAnchor.MiddleCenter;
                cv.childControlWidth = cv.childControlHeight = true; cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
                var label = Label(col, "Label", legendFont, 30, Ink);
                label.text = UiStrings.Entries.First(e => e[0] == key)[1];
                LocalizeEvents.BindText(label, Loc.Table, key);
                var value = Label(col, "Value", valueFont, 58, MineInk);
                value.text = "-";
                return value;
            }
            var myRank = Figure("Rank", "leaderboard.mine");
            var myPoints = Figure("Points", "leaderboard.points");
            var myRate = Figure("WinRate", "leaderboard.winRate");
            var other = Child<RectTransform>(box.transform, "Status");
            Stretch(other); other.offsetMin = new Vector2(40, 22); other.offsetMax = new Vector2(-40, -22);
            var ov = other.gameObject.AddComponent<VerticalLayoutGroup>();
            ov.spacing = 10; ov.childAlignment = TextAnchor.MiddleCenter;
            ov.childControlWidth = ov.childControlHeight = true; ov.childForceExpandWidth = false; ov.childForceExpandHeight = false;
            var myStatus = Label(other, "Text", legendFont, 32, Ink);
            myStatus.enableAutoSizing = true; myStatus.fontSizeMin = 22; myStatus.fontSizeMax = 32;
            myStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            myStatus.text = "";
            var googleGo = UiPrefabBuilder.Place(UiPrefabBuilder.GooglePath, other, "SignIn");
            var gle = googleGo.AddComponent<LayoutElement>(); gle.preferredWidth = GoogleButtonW; gle.preferredHeight = GoogleButtonH * 0.9f;
            var google = UiPrefabBuilder.SetLabel(googleGo, "account.googleLogin");

            // the column titles, over the rows' own columns
            var head = Child<RectTransform>(content, "Head");
            var hle = head.gameObject.AddComponent<LayoutElement>();
            hle.preferredHeight = BoardHeadH; hle.flexibleHeight = 0;   // (its row layout would ask for a share of the height)
            Columns(head, UiPrefabBuilder.ScrollbarW + UiPrefabBuilder.ScrollbarGap);
            foreach (var (name, key, w) in new[] { ("Rank", "leaderboard.rank", ColRank), ("Name", "leaderboard.name", -1f), ("Points", "leaderboard.points", ColPoints), ("WinRate", "leaderboard.winRate", ColRate) })
            {
                var t = Label(head, name, legendFont, 30, Ink);
                t.text = UiStrings.Entries.First(e => e[0] == key)[1];
                LocalizeEvents.BindText(t, Loc.Table, key);
                Column(t.gameObject, w);
                if (w < 0) t.alignment = TextAlignmentOptions.MidlineLeft;
            }

            // the rows, scrolling
            var scroll = UiPrefabBuilder.Place(UiPrefabBuilder.ScrollViewPath, content, "List");
            scroll.AddComponent<LayoutElement>().flexibleHeight = 1;
            // the rows always leave the scrollbar's room, shown or not, so their columns stay under the titles
            var sr = scroll.GetComponent<ScrollRect>();
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            ((RectTransform)sr.viewport).offsetMax = new Vector2(-(UiPrefabBuilder.ScrollbarW + UiPrefabBuilder.ScrollbarGap), 0);
            var rowsParent = (RectTransform)scroll.transform.Find("Viewport/Content");
            var rv = rowsParent.gameObject.AddComponent<VerticalLayoutGroup>();
            rv.spacing = BoardRowGap; rv.padding = new RectOffset(0, 0, 2, 8);
            rv.childControlWidth = rv.childControlHeight = true; rv.childForceExpandWidth = true; rv.childForceExpandHeight = false;
            var listStatus = Label(scroll.transform, "Status", legendFont, 32, AccountValueInk);
            Stretch(listStatus.rectTransform);
            listStatus.textWrappingMode = TextWrappingModes.Normal;
            listStatus.text = "";

            var row = BuildLeaderboardRow(rowsParent, valueFont);

            var popupComp = go.AddComponent<FlickCurling.Game.Online.LeaderboardPopup>();
            var so = new SerializedObject(popupComp);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("list").objectReferenceValue = rowsParent;
            so.FindProperty("rowTemplate").objectReferenceValue = row;
            so.FindProperty("listStatus").objectReferenceValue = listStatus;
            so.FindProperty("mine").objectReferenceValue = stats.gameObject;
            so.FindProperty("myRank").objectReferenceValue = myRank;
            so.FindProperty("myPoints").objectReferenceValue = myPoints;
            so.FindProperty("myRate").objectReferenceValue = myRate;
            so.FindProperty("myStatus").objectReferenceValue = myStatus;
            so.FindProperty("googleButton").objectReferenceValue = google;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            return popupComp;
        }

        // a row's columns: the rank (or medal), the nickname taking the rest, the points and the win rate
        static void Columns(RectTransform row, float rightPad)
        {
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset((int)ColPad, (int)(ColPad + rightPad), 0, 0); h.spacing = 12;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        }

        static void Column(GameObject go, float width)
        {
            var le = go.AddComponent<LayoutElement>();
            if (width < 0) { le.flexibleWidth = 1; le.minWidth = 0; } else le.preferredWidth = le.minWidth = width;
        }

        static FlickCurling.Game.Online.LeaderboardRow BuildLeaderboardRow(RectTransform parent, TMP_FontAsset font)
        {
            var plateSprite = ImportSprite(LbArt + "row.png", new Vector4(64, 56, 64, 56), 1200);
            var mineSprite = ImportSprite(LbArt + "row_mine.png", new Vector4(64, 56, 64, 56), 1200);
            var medals = new[] { "gold", "silver", "bronze" }.Select(m => ImportSprite(LbArt + "medal_" + m + ".png", Vector4.zero, 256)).ToArray();

            var plate = Child<Image>(parent, "RowTemplate");
            plate.sprite = plateSprite; plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = plateSprite.rect.height / BoardRowH;
            plate.raycastTarget = false;
            plate.gameObject.AddComponent<LayoutElement>().preferredHeight = BoardRowH;
            var rt = plate.rectTransform;
            Columns(rt, 0);

            var rankCell = Child<RectTransform>(rt, "Rank");
            Column(rankCell.gameObject, ColRank);
            var medal = Child<Image>(rankCell, "Medal");
            Place(medal.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MedalSize, MedalSize));
            medal.preserveAspect = true; medal.raycastTarget = false;
            var rank = Label(rankCell, "Number", font, 40, Ink);
            Stretch(rank.rectTransform);
            var name = Label(rt, "Name", font, 36, Ink);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Column(name.gameObject, -1);
            var points = Label(rt, "Points", font, 38, Ink);
            Column(points.gameObject, ColPoints);
            var rate = Label(rt, "WinRate", font, 34, Ink);
            Column(rate.gameObject, ColRate);

            var row = plate.gameObject.AddComponent<FlickCurling.Game.Online.LeaderboardRow>();
            var so = new SerializedObject(row);
            so.FindProperty("plate").objectReferenceValue = plate;
            so.FindProperty("medal").objectReferenceValue = medal;
            so.FindProperty("rank").objectReferenceValue = rank;
            so.FindProperty("nickname").objectReferenceValue = name;
            so.FindProperty("points").objectReferenceValue = points;
            so.FindProperty("winRate").objectReferenceValue = rate;
            so.FindProperty("plateSprite").objectReferenceValue = plateSprite;
            so.FindProperty("mineSprite").objectReferenceValue = mineSprite;
            var arr = so.FindProperty("medals");
            arr.arraySize = medals.Length;
            for (int i = 0; i < medals.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = medals[i];
            so.FindProperty("ink").colorValue = (Color)Ink;
            so.FindProperty("mineInk").colorValue = (Color)MineInk;
            so.ApplyModifiedPropertiesWithoutUndo();
            return row;
        }

        // ---- the game rules popup: the list of its pages (shared secondary buttons), and a page: "n / count", then its
        // items (a label over a short line) in a clipped viewport that swipes sideways; the list button (Codex art, the
        // close button's sibling) at the title bar's left end, and the swipe hint (the tutorial's hand and arrow) ----

        public const string RulesPrefabPath = "Assets/Resources/" + RulesPopup.ResourcePath + ".prefab";
        const float RulesW = 900, RulesListH = 1040, RulesPageH = 1660, RulesButtonW = 620, RulesButtonH = 108, RulesCounterH = 48;
        const float RulesHandW = 120, RulesHandH = 172, RulesArrowW = 250, RulesArrowH = 69;
        static readonly Color32 RulesAccent = new Color32(0xb0, 0x4a, 0x1c, 0xff);

        static GameObject BuildRulesPrefab(GameObject popupPrefab)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab);
            go.name = "RulesPopup";
            var legendFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.BodyPath);
            // titled like the menu button, with its icon; a page puts its own title there
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "mainmenu.rules")[1];
            var titleLoc = LocalizeEvents.BindText(title, Loc.Table, "mainmenu.rules");
            var icon = go.transform.Find("Window/TitleRow/Icon").GetComponent<Image>();
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath("mainmenu.rules"));
            icon.gameObject.SetActive(true);
            var win = (RectTransform)go.transform.Find("Window");
            win.sizeDelta = new Vector2(RulesW, RulesListH);

            // the list button: where the close button is, mirrored to the bar's left end
            var close = (RectTransform)win.Find("Close");
            var listImg = Child<Image>(win, "ListButton");
            var lrt = listImg.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0, 1); lrt.pivot = close.pivot;
            lrt.anchoredPosition = new Vector2(-close.anchoredPosition.x, close.anchoredPosition.y);
            lrt.sizeDelta = close.sizeDelta;
            listImg.sprite = ImportSprite("Assets/Art/Menu/popup_list.png", Vector4.zero, 256);
            listImg.preserveAspect = true;
            var listButton = listImg.gameObject.AddComponent<Button>();
            listButton.colors = close.GetComponent<Button>().colors;
            listImg.gameObject.SetActive(false);

            var content = (RectTransform)win.Find("Content");

            // the list: one button per page, down the middle
            var listView = Child<RectTransform>(content, "PageList");
            Stretch(listView);
            var lv = listView.gameObject.AddComponent<VerticalLayoutGroup>();
            lv.spacing = 24; lv.childAlignment = TextAnchor.MiddleCenter;
            lv.childControlWidth = lv.childControlHeight = true; lv.childForceExpandWidth = lv.childForceExpandHeight = false;
            var pageButtons = new Button[RulesPopup.PageCount];
            for (int i = 0; i < pageButtons.Length; i++)
            {
                var b = UiPrefabBuilder.Place(UiPrefabBuilder.SecondaryPath, listView, "Page_" + (i + 1));
                var le = b.AddComponent<LayoutElement>(); le.preferredWidth = RulesButtonW; le.preferredHeight = RulesButtonH;
                pageButtons[i] = UiPrefabBuilder.SetLabel(b, RulesPopup.TitleKey(i));
            }

            // a page: the count, then the page swiping in its viewport
            var pageView = Child<RectTransform>(content, "Page");
            Stretch(pageView);
            var counter = Label(pageView, "Counter", legendFont, 32, AccountValueInk);
            var crt = counter.rectTransform;
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = new Vector2(0, 4); crt.sizeDelta = new Vector2(0, RulesCounterH);
            counter.text = "1 / " + RulesPopup.PageCount;
            var viewport = Child<Image>(pageView, "Viewport");
            Stretch(viewport.rectTransform);
            viewport.rectTransform.offsetMax = new Vector2(0, -(RulesCounterH + 18));
            viewport.color = Color.clear;   // (a raycast target, for the swipe)
            viewport.gameObject.AddComponent<RectMask2D>();
            var swipe = viewport.gameObject.AddComponent<SwipeArea>();

            var template = Child<RectTransform>(viewport.transform, "PageTemplate");
            Stretch(template);
            var tv = template.gameObject.AddComponent<VerticalLayoutGroup>();
            tv.spacing = 34; tv.childAlignment = TextAnchor.UpperCenter; tv.padding = new RectOffset(0, 0, 6, 0);
            tv.childControlWidth = tv.childControlHeight = true; tv.childForceExpandWidth = true; tv.childForceExpandHeight = false;
            // the page's picture (RulesFigures draws it), its height set per page
            var figure = Child<RectTransform>(template, "Figure");
            figure.gameObject.AddComponent<LayoutElement>().preferredHeight = 330;
            var item = Child<RectTransform>(template, "Item");
            var iv = item.gameObject.AddComponent<VerticalLayoutGroup>();
            iv.spacing = 6;
            iv.childControlWidth = iv.childControlHeight = true; iv.childForceExpandWidth = true; iv.childForceExpandHeight = false;
            var key = Label(item, "Label", legendFont, 40, RulesAccent);
            key.alignment = TextAlignmentOptions.TopLeft;
            var line = Label(item, "Text", bodyFont, 37, Ink);
            line.alignment = TextAlignmentOptions.TopLeft;
            line.textWrappingMode = TextWrappingModes.Normal;
            line.lineSpacing = 8;
            item.gameObject.SetActive(false);

            // the swipe hint, over the first page's figure (placed there when shown): the double arrow, and the hand
            // sliding under it
            var hint = Child<RectTransform>(pageView, "SwipeHint");
            Place(hint, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(RulesArrowW + 2 * 130, 300));
            var hg = hint.gameObject.AddComponent<CanvasGroup>();
            hg.blocksRaycasts = false; hg.interactable = false; hg.alpha = 0;
            var arrow = Child<Image>(hint, "Arrow");
            arrow.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Tutorial/arrow_h.png");
            arrow.preserveAspect = true; arrow.raycastTarget = false;
            Place(arrow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(RulesArrowW, RulesArrowH));
            var hand = Child<Image>(hint, "Hand");
            hand.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Tutorial/hand.png");
            hand.preserveAspect = true; hand.raycastTarget = false;
            Place(hand.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(RulesHandW, RulesHandH));
            hand.rectTransform.pivot = new Vector2(0.38f, 1);   // the fingertip is the point
            hint.gameObject.SetActive(false);

            var rules = go.AddComponent<RulesPopup>();
            var so = new SerializedObject(rules);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("listView").objectReferenceValue = listView.gameObject;
            so.FindProperty("pageView").objectReferenceValue = pageView.gameObject;
            var arr = so.FindProperty("pageButtons");
            arr.arraySize = pageButtons.Length;
            for (int i = 0; i < pageButtons.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = pageButtons[i];
            so.FindProperty("listButton").objectReferenceValue = listButton;
            so.FindProperty("counter").objectReferenceValue = counter;
            so.FindProperty("viewport").objectReferenceValue = viewport.rectTransform;
            so.FindProperty("pageTemplate").objectReferenceValue = template;
            so.FindProperty("swipe").objectReferenceValue = swipe;
            so.FindProperty("hint").objectReferenceValue = hint;
            so.FindProperty("hintHand").objectReferenceValue = hand.rectTransform;
            so.FindProperty("titleLoc").objectReferenceValue = titleLoc;
            so.FindProperty("icon").objectReferenceValue = icon.sprite;
            so.FindProperty("sizeList").vector2Value = new Vector2(RulesW, RulesListH);
            so.FindProperty("sizePage").vector2Value = new Vector2(RulesW, RulesPageH);
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, RulesPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
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

            // in the game only: the game rules, and back to the main menu, at the bottom
            var spacer = Child<RectTransform>(content, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var rulesRow = Child<RectTransform>(content, "RulesRow");
            rulesRow.gameObject.AddComponent<LayoutElement>().preferredHeight = SetupOkH;
            var rulesGo = UiPrefabBuilder.Place(UiPrefabBuilder.SecondaryPath, rulesRow, "Rules");
            Place((RectTransform)rulesGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SettingsToMenuW, SetupOkH));
            var rules = UiPrefabBuilder.SetLabel(rulesGo, "mainmenu.rules");
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
            so.FindProperty("rulesRow").objectReferenceValue = rulesRow.gameObject;
            so.FindProperty("rules").objectReferenceValue = rules;
            so.FindProperty("toMenu").objectReferenceValue = toMenu;
            so.FindProperty("Icon").objectReferenceValue = icon.sprite;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, SettingsPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        public const string NicknamePrefabPath = "Assets/Resources/" + FlickCurling.Game.Account.NicknamePopup.ResourcePath + ".prefab";
        const float NickW = 860, NickH = 640;

        // the nickname a new player must choose: a message, the input field, the rules (and the count) under it, and OK;
        // no close button
        static void BuildNicknamePrefab(GameObject popupPrefab)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab);
            go.name = "NicknamePopup";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "nickname.title")[1];
            LocalizeEvents.BindText(title, Loc.Table, "nickname.title");
            ((RectTransform)go.transform.Find("Window")).sizeDelta = new Vector2(NickW, NickH);
            go.transform.Find("Window/Close").gameObject.SetActive(false);
            var content = (RectTransform)go.transform.Find("Window/Content");
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 22; v.childAlignment = TextAnchor.UpperCenter; v.padding = new RectOffset(0, 0, 10, 0);
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var message = Label(content, "Prompt", font, 36, Ink);
            message.textWrappingMode = TextWrappingModes.Normal;
            message.text = UiStrings.Entries.First(e => e[0] == "nickname.message")[1];
            LocalizeEvents.BindText(message, Loc.Table, "nickname.message");

            var inputGo = UiPrefabBuilder.Place(UiPrefabBuilder.InputPath, content, "Input");
            inputGo.AddComponent<LayoutElement>().preferredHeight = 104;
            var input = UiPrefabBuilder.SetPlaceholder(inputGo, "nickname.placeholder");

            // the rules on the left, the count on the right
            var row = Child<RectTransform>(content, "Rules");
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16; h.childAlignment = TextAnchor.UpperLeft; h.padding = new RectOffset(12, 12, 0, 0);
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
            var guide = Label(row, "Guide", font, 26, AccountValueInk);
            guide.alignment = TextAlignmentOptions.TopLeft; guide.textWrappingMode = TextWrappingModes.Normal;
            guide.text = UiStrings.Entries.First(e => e[0] == "nickname.guide")[1];
            var gle = guide.gameObject.AddComponent<LayoutElement>(); gle.flexibleWidth = 1; gle.preferredWidth = 0;
            var counter = Label(row, "Count", font, 26, AccountValueInk);
            counter.alignment = TextAlignmentOptions.TopRight;
            counter.text = "0/" + FlickCurling.Game.Account.Nickname.MaxLength;

            var spacer = Child<RectTransform>(content, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var okRow = Child<RectTransform>(content, "OkRow");
            okRow.gameObject.AddComponent<LayoutElement>().preferredHeight = SetupOkH;
            var okGo = UiPrefabBuilder.Place(UiPrefabBuilder.PrimaryPath, okRow, "Ok");
            Place((RectTransform)okGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SetupOkW, SetupOkH));
            var ok = UiPrefabBuilder.SetLabel(okGo, "nickname.ok");

            var panel = go.AddComponent<FlickCurling.Game.Account.NicknamePopup>();
            var so = new SerializedObject(panel);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("input").objectReferenceValue = input;
            so.FindProperty("guide").objectReferenceValue = guide;
            so.FindProperty("counter").objectReferenceValue = counter;
            so.FindProperty("ok").objectReferenceValue = ok;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(go, NicknamePrefabPath);
            Object.DestroyImmediate(go);
        }

        public const string DevicePrefabPath = "Assets/Resources/" + FlickCurling.Game.Account.DevicePopup.ResourcePath + ".prefab";

        // signed in on another device: the message and one button to sign in here again; no way to close it
        static void BuildDevicePrefab(GameObject popupPrefab)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab);
            go.name = "DevicePopup";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "device.title")[1];
            LocalizeEvents.BindText(title, Loc.Table, "device.title");
            ((RectTransform)go.transform.Find("Window")).sizeDelta = new Vector2(NickW, 640);
            go.transform.Find("Window/Close").gameObject.SetActive(false);
            var content = (RectTransform)go.transform.Find("Window/Content");
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 22; v.childAlignment = TextAnchor.UpperCenter; v.padding = new RectOffset(0, 0, 20, 0);
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var message = Label(content, "Message", font, 36, Ink);
            message.textWrappingMode = TextWrappingModes.Normal;
            message.alignment = TextAlignmentOptions.Center;
            message.text = UiStrings.Entries.First(e => e[0] == "device.message")[1];
            LocalizeEvents.BindText(message, Loc.Table, "device.message");

            var spacer = Child<RectTransform>(content, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var okRow = Child<RectTransform>(content, "OkRow");
            okRow.gameObject.AddComponent<LayoutElement>().preferredHeight = SetupOkH;
            var okGo = UiPrefabBuilder.Place(UiPrefabBuilder.PrimaryPath, okRow, "Reconnect");
            Place((RectTransform)okGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SetupOkW, SetupOkH));
            var ok = UiPrefabBuilder.SetLabel(okGo, "device.reconnect");

            var panel = go.AddComponent<FlickCurling.Game.Account.DevicePopup>();
            var so = new SerializedObject(panel);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("reconnect").objectReferenceValue = ok;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(go, DevicePrefabPath);
            Object.DestroyImmediate(go);
        }

        public const string NetworkPrefabPath = "Assets/Resources/" + FlickCurling.Game.Account.NetworkPopup.ResourcePath + ".prefab";

        // no network: the message only; no way to close it (it goes when the network is back)
        static void BuildNetworkPrefab(GameObject popupPrefab)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab);
            go.name = "NetworkPopup";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "network.title")[1];
            LocalizeEvents.BindText(title, Loc.Table, "network.title");
            ((RectTransform)go.transform.Find("Window")).sizeDelta = new Vector2(NickW, 420);
            go.transform.Find("Window/Close").gameObject.SetActive(false);
            var content = (RectTransform)go.transform.Find("Window/Content");
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = v.childForceExpandHeight = true;
            var message = Label(content, "Message", font, 40, Ink);
            message.textWrappingMode = TextWrappingModes.Normal;
            message.alignment = TextAlignmentOptions.Center;
            message.text = UiStrings.Entries.First(e => e[0] == "network.message")[1];
            LocalizeEvents.BindText(message, Loc.Table, "network.message");

            var panel = go.AddComponent<FlickCurling.Game.Account.NetworkPopup>();
            var so = new SerializedObject(panel);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(go, NetworkPrefabPath);
            Object.DestroyImmediate(go);
        }

        public const string LinkConflictPrefabPath = "Assets/Resources/" + FlickCurling.Game.Account.LinkConflictPopup.ResourcePath + ".prefab";
        const float LinkW = 860, LinkH = 900, LinkCardH = 196, LinkButtonW = 340;

        // the account conflict choice: a message, the account's record and this one in account boxes, and the two ways out
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
                var header = Text(box.transform, "Header", font, 28, Ink, headerKey);
                header.alignment = TextAlignmentOptions.MidlineLeft; header.fontStyle = FontStyles.Bold;
                var n = Text(box.transform, "Name", valueFont, 34, Ink); n.alignment = TextAlignmentOptions.MidlineLeft;
                var p = Text(box.transform, "Pid", valueFont, 22, AccountValueInk); p.alignment = TextAlignmentOptions.MidlineLeft;
                var d = Text(box.transform, "Date", valueFont, 22, AccountValueInk); d.alignment = TextAlignmentOptions.MidlineLeft;
                return (n, p, d);
            }
            var existing = Card("Existing", "link.existing");
            var current = Card("Current", "link.current");
            Child<RectTransform>(choice, "Gap").gameObject.AddComponent<LayoutElement>().preferredHeight = 6;
            // side by side: load (the account's record) on the left, overwrite (keep this one) on the right
            var buttons = Child<RectTransform>(choice, "Buttons");
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = SetupOkH;
            var bh = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            bh.spacing = 24; bh.childAlignment = TextAnchor.MiddleCenter;
            bh.childControlWidth = bh.childControlHeight = true; bh.childForceExpandWidth = bh.childForceExpandHeight = false;
            Button Side(string prefabPath, string name, string key)
            {
                var b = UiPrefabBuilder.Place(prefabPath, buttons, name);
                var le = b.AddComponent<LayoutElement>(); le.preferredWidth = LinkButtonW; le.preferredHeight = SetupOkH;
                return UiPrefabBuilder.SetLabel(b, key);
            }
            var load = Side(UiPrefabBuilder.PrimaryPath, "Load", "link.load");
            var keep = Side(UiPrefabBuilder.SecondaryPath, "Keep", "link.keep");

            var panel = go.AddComponent<FlickCurling.Game.Account.LinkConflictPopup>();
            var so = new SerializedObject(panel);
            so.FindProperty("popup").objectReferenceValue = go.GetComponent<Popup>();
            so.FindProperty("existingName").objectReferenceValue = existing.name;
            so.FindProperty("existingPid").objectReferenceValue = existing.pid;
            so.FindProperty("existingDate").objectReferenceValue = existing.date;
            so.FindProperty("currentName").objectReferenceValue = current.name;
            so.FindProperty("currentPid").objectReferenceValue = current.pid;
            so.FindProperty("currentDate").objectReferenceValue = current.date;
            so.FindProperty("load").objectReferenceValue = load;
            so.FindProperty("keep").objectReferenceValue = keep;
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
