using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The main menu screen: the logo over the house and the mode buttons on the board art (built by MainMenuBuilder).
    /// "Offline Match" opens the match setup popup and "Settings" the settings popup; the other buttons press but do nothing yet. Each label is localized by its own LocalizeStringEvent
    /// (table "UI"); this keeps the content inside the safe area.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string Key;
            public TextMeshProUGUI Label;
            public Button Button;
        }

        [SerializeField] RectTransform safeArea;
        [SerializeField] Entry[] entries;
        [SerializeField] Popup popup;
        [SerializeField] MatchSetup matchSetup;
        [SerializeField] SettingsPanel settings;
        [SerializeField] Popup shop;

        /// <summary>The menu's shared popup window (hidden until shown).</summary>
        public Popup Popup => popup;

        void Awake()
        {
            // offline match opens the match setup popup, settings its popup; the others do nothing yet
            foreach (var e in entries)
            {
                if (e.Key == "mainmenu.offline") e.Button.onClick.AddListener(matchSetup.Open);
                else if (e.Key == "mainmenu.shop") e.Button.onClick.AddListener(() => shop.Show());
                else if (e.Key == "mainmenu.settings") e.Button.onClick.AddListener(() => settings.Open());
            }
        }

        Rect lastSafe;
        Vector2Int lastScreen;

        void Update()
        {
            var r = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (r == lastSafe && size == lastScreen) return;
            lastSafe = r; lastScreen = size;
            if (size.x <= 0 || size.y <= 0) return;
            // kept inside the screen: the editor's device simulator can report a safe area from another device
            r = Rect.MinMaxRect(Mathf.Max(0, r.xMin), Mathf.Max(0, r.yMin), Mathf.Min(size.x, r.xMax), Mathf.Min(size.y, r.yMax));
            if (r.width <= 0 || r.height <= 0) r = new Rect(0, 0, size.x, size.y);
            safeArea.anchorMin = new Vector2(r.xMin / size.x, r.yMin / size.y);
            safeArea.anchorMax = new Vector2(r.xMax / size.x, r.yMax / size.y);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }
    }
}
