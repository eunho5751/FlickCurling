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
            { "mainmenu.ai", "mainmenu.local", "mainmenu.online", "mainmenu.leaderboard", "mainmenu.rules", "mainmenu.settings" };

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

            var menu = canvasGo.AddComponent<MainMenu>();
            var so = new SerializedObject(menu);
            so.FindProperty("safeArea").objectReferenceValue = safe;
            so.FindProperty("popup").objectReferenceValue = popup.GetComponent<Popup>();
            so.FindProperty("matchSetup").objectReferenceValue = matchSetup;
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

        // ---- the match setup popup (AI match / local): mode and AI level as group toggles, and OK, all from the shared
        // prefabs (Popup, ToggleGroup, Button_Primary) ----

        const float SetupLegendH = 52, SetupToggleH = 96, SetupGap = 22, SetupOkW = 380, SetupOkH = 112;
        static readonly Color32 Ink = new Color32(0x4a, 0x2a, 0x12, 0xff);

        static MatchSetup BuildMatchSetup(GameObject popupPrefab, Transform parent, TMP_FontAsset buttonFont)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(popupPrefab, parent);
            go.name = "MatchSetupPopup";
            var legendFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);
            var title = go.transform.Find("Window/TitleRow/Title").GetComponent<TextMeshProUGUI>();
            title.text = UiStrings.Entries.First(e => e[0] == "matchsetup.titleAi")[1];
            var titleEvent = LocalizeEvents.BindText(title, Loc.Table, "matchsetup.titleAi");   // switched to the local title at runtime

            var content = (RectTransform)go.transform.Find("Window/Content");
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = SetupGap; v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var modeRow = Row(content, "ModeRow");
            Legend(modeRow, legendFont, "matchsetup.mode");
            var mode = Toggle(modeRow, new[] { "matchsetup.modeBasic" }, 0);
            var levelRow = Row(content, "LevelRow");
            Legend(levelRow, legendFont, "matchsetup.level");
            var level = Toggle(levelRow, new[] { "level.easy", "level.normal", "level.hard" }, 1);

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
            so.FindProperty("title").objectReferenceValue = titleEvent;
            so.FindProperty("mode").objectReferenceValue = mode;
            so.FindProperty("levelRow").objectReferenceValue = levelRow.gameObject;
            so.FindProperty("level").objectReferenceValue = level;
            so.FindProperty("ok").objectReferenceValue = ok;
            so.FindProperty("iconAi").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath("mainmenu.ai"));
            so.FindProperty("iconLocal").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath("mainmenu.local"));
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            return setup;
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
        static SegmentedToggle Toggle(RectTransform row, string[] keys, int selected)
        {
            var go = UiPrefabBuilder.Place(UiPrefabBuilder.TogglePath, row, "Toggle");
            go.AddComponent<LayoutElement>().preferredHeight = SetupToggleH;
            return UiPrefabBuilder.SetOptions(go, keys, selected);
        }

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
