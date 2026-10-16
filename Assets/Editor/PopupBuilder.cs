using System.IO;
using FlickCurling.Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Builds Assets/Prefabs/Popup.prefab, the main menu's shared popup: a dim backdrop, the 9-sliced panel, a title plate
    /// over its top edge, a round close button and a content area with a plain message text. The art (Codex, hand-painted wood grain
    /// like the menu's wooden planks, calm and casual) lives in Assets/Art/Menu as popup_*.png. Sizes are in the menu's 1080 x 1920
    /// reference pixels.
    /// </summary>
    public static class PopupBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/Popup.prefab";
        const string PanelPath = "Assets/Art/Menu/popup_panel.png", TitlePath = "Assets/Art/Menu/popup_title.png", ClosePath = "Assets/Art/Menu/popup_close.png";

        // 9-slice borders in source pixels (left, bottom, right, top) and how many source pixels make one reference pixel,
        // which sets how thick the rims show
        static readonly Vector4 PanelBorder = new Vector4(72, 72, 72, 72), TitleBorder = new Vector4(90, 50, 90, 50);
        const float PanelPpu = 0.9f;

        const float WindowW = 860, WindowH = 760, TitleW = 560, TitleH = 120, CloseSize = 110;
        static readonly Color32 Cream = new Color32(0xf6, 0xea, 0xd2, 0xff), Ink = new Color32(0x4a, 0x2a, 0x12, 0xff);

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
            var plate = MainMenuBuilder.ImportSprite(TitlePath, TitleBorder, 1024);
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

            // the title plate sits across the panel's top edge
            var plateImg = MainMenuBuilder.Child<Image>(win.transform, "TitlePlate");
            MainMenuBuilder.Place(plateImg.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(TitleW, TitleH));
            plateImg.sprite = plate; plateImg.type = Image.Type.Sliced; plateImg.pixelsPerUnitMultiplier = plate.rect.height / TitleH;
            plateImg.raycastTarget = false;
            var title = MainMenuBuilder.Label(plateImg.transform, "Title", titleFont, 50, Cream);
            MainMenuBuilder.Stretch(title.rectTransform);
            title.rectTransform.offsetMin = new Vector2(30, 8); title.rectTransform.offsetMax = new Vector2(-30, 0);   // the face sits above the bottom lip
            title.enableAutoSizing = true; title.fontSizeMin = 30; title.fontSizeMax = 50;
            title.text = "Title";

            // the close button over the top right corner
            var closeImg = MainMenuBuilder.Child<Image>(win.transform, "Close");
            MainMenuBuilder.Place(closeImg.rectTransform, new Vector2(1, 1), new Vector2(-22, -22), new Vector2(CloseSize, CloseSize));
            closeImg.sprite = close; closeImg.preserveAspect = true;
            var closeButton = closeImg.gameObject.AddComponent<Button>();
            var cb = closeButton.colors; cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f); cb.pressedColor = new Color(.82f, .82f, .82f); closeButton.colors = cb;

            // what the popup shows goes here, inside the frame and below the title plate
            var content = MainMenuBuilder.Child<RectTransform>(win.transform, "Content");
            MainMenuBuilder.Stretch(content);
            content.offsetMin = new Vector2(70, 70); content.offsetMax = new Vector2(-70, -110);
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
