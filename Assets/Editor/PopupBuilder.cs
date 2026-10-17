using System.IO;
using FlickCurling.Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Builds Assets/Prefabs/Popup.prefab, the main menu's shared popup: a dim backdrop, the 9-sliced panel whose wooden
    /// frame has a wide header bar along the top, the title written on that bar with a small round close button at its
    /// right end, and a content area with a plain message text. The art (Codex, hand-painted wood grain
    /// like the menu's wooden planks, calm and casual) lives in Assets/Art/Menu as popup_*.png. Sizes are in the menu's 1080 x 1920
    /// reference pixels.
    /// </summary>
    public static class PopupBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/Popup.prefab";
        const string PanelPath = "Assets/Art/Menu/popup_panel.png", ClosePath = "Assets/Art/Menu/popup_close.png";

        // 9-slice borders in source pixels (left, bottom, right, top) and how many source pixels make one reference pixel,
        // which sets how thick the rims show
        static readonly Vector4 PanelBorder = new Vector4(56, 56, 56, 112);   // the top border holds the header bar
        const float PanelPpu = 0.9f;

        // the header bar is 91 source pixels tall (outline included), so about 101 reference pixels at PanelPpu; the frame's
        // other bars about 38
        const float WindowW = 860, WindowH = 785, HeaderH = 101, SideW = 38, CloseSize = 64, TitleIconSize = 64;
        static readonly Color32 Ink = new Color32(0x4a, 0x2a, 0x12, 0xff);

        // the shop card's frame and its name and price plates stay unstretched (left, bottom, right, top); declared before
        // Controls, which reads it while static fields are set up in order
        public static readonly Vector4 ShopCardBorder = new Vector4(60, 112, 60, 90);

        // the controls that go on the popup (Codex, same plain style), each with its 9-slice border in source pixels (left,
        // bottom, right, top): a segmented toggle group (groove and the sliding selected segment), primary and secondary
        // buttons, and a slider (groove, fill, a go-stone handle)
        public static readonly (string path, Vector4 border)[] Controls =
        {
            ("Assets/Art/Menu/ui_toggle_track.png", new Vector4(54, 44, 54, 44)),
            ("Assets/Art/Menu/ui_toggle_on.png", new Vector4(28, 20, 28, 20)),
            ("Assets/Art/Menu/ui_button_primary.png", new Vector4(46, 40, 46, 40)),
            ("Assets/Art/Menu/ui_button_secondary.png", new Vector4(44, 36, 44, 36)),
            ("Assets/Art/Menu/ui_slider_track.png", new Vector4(26, 22, 26, 22)),
            ("Assets/Art/Menu/ui_slider_fill.png", new Vector4(16, 13, 16, 13)),
            ("Assets/Art/Menu/ui_slider_handle.png", Vector4.zero),
            ("Assets/Art/Menu/ui_dropdown.png", new Vector4(30, 30, 60, 30)),   // the right border holds the arrow's end cap
            ("Assets/Art/Menu/ui_dropdown_arrow.png", Vector4.zero),
            ("Assets/Art/Menu/ui_dropdown_list.png", new Vector4(40, 40, 40, 40)),
            ("Assets/Art/Menu/ui_dropdown_item.png", new Vector4(30, 25, 30, 25)),
            ("Assets/Art/Menu/shop_card.png", ShopCardBorder),
            ("Assets/Art/Menu/ui_scroll_track.png", new Vector4(0, 36, 0, 36)),
            ("Assets/Art/Menu/ui_scroll_handle.png", new Vector4(0, 34, 0, 34)),
            ("Assets/Art/Menu/shop_nail.png", Vector4.zero),
            ("Assets/Art/Menu/shop_coin.png", Vector4.zero),
            ("Assets/Art/Menu/account_box.png", new Vector4(34, 34, 34, 34)),   // the settings' account box
            // Sign in with Google (Google's branding guidelines): the white, gray-edged rounded button and the G logo
            ("Assets/Art/Menu/google_button.png", new Vector4(14, 14, 14, 14)),
            ("Assets/Art/Menu/google_g.png", Vector4.zero),
        };

        [MenuItem("FlickCurling/Import Popup Controls")]
        public static void ImportControls()
        {
            foreach (var c in Controls) MainMenuBuilder.ImportSprite(c.path, c.border, 512);
        }

        [MenuItem("FlickCurling/Build Popup Prefab")]
        public static GameObject Build()
        {
            ImportControls();
            var panel = MainMenuBuilder.ImportSprite(PanelPath, PanelBorder, 512);
            var close = MainMenuBuilder.ImportSprite(ClosePath, Vector4.zero, 256);
            var titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.ButtonPath);   // Jua, Mochiy Pop One for Japanese
            var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.DisplayPath);

            var root = new GameObject("Popup", typeof(RectTransform));
            MainMenuBuilder.Stretch(root.GetComponent<RectTransform>());
            var group = root.AddComponent<CanvasGroup>();

            // the dim backdrop over the whole screen; a tap on it closes the popup
            var dim = MainMenuBuilder.Child<Image>(root.transform, "Backdrop");
            MainMenuBuilder.Stretch(dim.rectTransform);
            dim.color = new Color(0.06f, 0.035f, 0.02f, 0.6f);
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;

            var win = MainMenuBuilder.Child<Image>(root.transform, "Window");
            MainMenuBuilder.Place(win.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(WindowW, WindowH));
            win.sprite = panel; win.type = Image.Type.Sliced; win.pixelsPerUnitMultiplier = PanelPpu;

            // the title is written on the header bar, centred, in the menu planks' dark brown, with an optional icon on its
            // left (the pair is centred together); it keeps clear of the close button at the bar's right end
            var row = MainMenuBuilder.Child<RectTransform>(win.transform, "TitleRow");
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1);
            row.offsetMin = new Vector2(SideW + CloseSize + 24, -HeaderH + 8); row.offsetMax = new Vector2(-(SideW + CloseSize + 24), -6);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14; h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
            var icon = MainMenuBuilder.Child<Image>(row, "Icon");
            icon.preserveAspect = true; icon.raycastTarget = false;
            var ile = icon.gameObject.AddComponent<LayoutElement>();
            ile.preferredWidth = ile.minWidth = TitleIconSize; ile.preferredHeight = TitleIconSize;
            icon.gameObject.SetActive(false);
            var title = MainMenuBuilder.Label(row, "Title", titleFont, 50, Ink);
            title.margin = new Vector4(0, 10, 0, 0);   // top margin: the bar's face sits a little low
            title.text = "Title";

            // the small close button inside the header bar, at its right end
            var closeImg = MainMenuBuilder.Child<Image>(win.transform, "Close");
            MainMenuBuilder.Place(closeImg.rectTransform, new Vector2(1, 1), new Vector2(-(SideW + CloseSize / 2 + 6) + 25, -(HeaderH / 2 + 2)), new Vector2(CloseSize, CloseSize));
            closeImg.sprite = close; closeImg.preserveAspect = true;
            var closeButton = closeImg.gameObject.AddComponent<Button>();
            var cb = closeButton.colors; cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f); cb.pressedColor = new Color(.82f, .82f, .82f); closeButton.colors = cb;

            // what the popup shows goes here, inside the frame and below the header bar
            var content = MainMenuBuilder.Child<RectTransform>(win.transform, "Content");
            MainMenuBuilder.Stretch(content);
            content.offsetMin = new Vector2(70, 70); content.offsetMax = new Vector2(-70, -(HeaderH + 34));
            var message = MainMenuBuilder.Label(content, "Message", bodyFont, 40, Ink);
            MainMenuBuilder.Stretch(message.rectTransform);
            message.textWrappingMode = TextWrappingModes.Normal;
            message.text = "";
            message.gameObject.SetActive(false);

            var popup = root.AddComponent<Popup>();
            var so = new SerializedObject(popup);
            so.FindProperty("group").objectReferenceValue = group;
            so.FindProperty("window").objectReferenceValue = win.rectTransform;
            so.FindProperty("title").objectReferenceValue = title;
            so.FindProperty("titleIcon").objectReferenceValue = icon;
            so.FindProperty("message").objectReferenceValue = message;
            so.FindProperty("content").objectReferenceValue = content;
            so.FindProperty("closeButton").objectReferenceValue = closeButton;
            so.FindProperty("backdrop").objectReferenceValue = dimButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("FlickCurling: built " + PrefabPath);
            return prefab;
        }
    }
}
