using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The settings popup shared by the main menu and the game (the prefab Resources/SettingsPopup, built by
    /// MainMenuBuilder on the shared Popup): music and sound effect volume sliders, the aim sensitivity slider (levels 1 to
    /// 5), vibration on / off and the language dropdown, plus, in the game, "game rules" (the rules popup over this one) and "back to the main
    /// menu" buttons at the bottom. Every change is
    /// saved and applied at once (GameSettings, Unity Localization).
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        public const string ResourcePath = "SettingsPopup";

        [SerializeField] Popup popup;
        [SerializeField] Slider music, sfx, sens;
        [SerializeField] TextMeshProUGUI musicValue, sfxValue, sensValue;
        [SerializeField] TMP_Dropdown language;
        [SerializeField] SegmentedToggle vibration;   // on, off
        [SerializeField] GameObject toMenuRow;
        [SerializeField] Button toMenu;
        [SerializeField] GameObject rulesRow;
        [SerializeField] Button rules;
        [SerializeField] Button terms, privacy;
        [SerializeField] Vector2 sizeMenu = new Vector2(860, 1180), sizeGame = new Vector2(860, 1500);
        [Tooltip("The settings gear, for buttons that open this popup")]
        public Sprite Icon;

        /// <summary>The "back to the main menu" button was pressed (the popup has closed).</summary>
        public event Action ToMenu;

        public bool IsOpen => gameObject.activeSelf;

        bool ready;

        // once, before the first opening (the prefab may arrive inactive, so Awake can come late)
        void Init()
        {
            if (ready) return;
            ready = true;
            language.ClearOptions();
            language.AddOptions(Loc.NativeNames.ToList());
            music.onValueChanged.AddListener(v => { GameSettings.MusicVolume = v; Show(); });
            sfx.onValueChanged.AddListener(v => { GameSettings.SfxVolume = v; Show(); });
            sens.onValueChanged.AddListener(v => { GameSettings.SensLevel = Mathf.RoundToInt(v) - 1; Show(); });
            language.onValueChanged.AddListener(i => Loc.Select(Loc.Codes[i]));
            vibration.Changed += i => { GameSettings.Vibration = i == 0; if (i == 0) Haptics.Pulse(0.5f); };   // (a taste of it)
            toMenu.onClick.AddListener(() => { popup.Hide(); ToMenu?.Invoke(); });
            rules.onClick.AddListener(OpenRules);
            terms.onClick.AddListener(() => OpenPage(GameConfig.Load().termsUrl));
            privacy.onClick.AddListener(() => OpenPage(GameConfig.Load().privacyUrl));
        }

        // the terms of service or the privacy policy, in the browser (the address is GameConfig's)
        static void OpenPage(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { Debug.LogWarning("Settings: no address set for that page in GameConfig"); return; }
            Application.OpenURL(url.Trim());
        }

        RulesPopup rulesPopup;

        // the shared rules popup (Resources/RulesPopup), made on first use beside this one and opened over it
        void OpenRules()
        {
            if (!rulesPopup) rulesPopup = Instantiate(Resources.Load<RulesPopup>(RulesPopup.ResourcePath), transform.parent);
            rulesPopup.Open();
        }

        /// <summary>Opens the popup; <paramref name="inGame"/> adds the "back to the main menu" button.</summary>
        public void Open(bool inGame = false)
        {
            Init();
            // the saved values, without writing them back
            music.SetValueWithoutNotify(GameSettings.MusicVolume);
            sfx.SetValueWithoutNotify(GameSettings.SfxVolume);
            sens.SetValueWithoutNotify(GameSettings.SensLevel + 1);
            language.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(Loc.Codes, Loc.Code)));
            vibration.Select(GameSettings.Vibration ? 0 : 1, false, false);
            toMenuRow.SetActive(inGame);
            rulesRow.SetActive(inGame);
            // signing in (linking an account, maybe switching players) only from the main menu, never mid-game
            var account = GetComponentInChildren<Account.AccountPanel>(true);
            if (account) account.LinkAllowed = !inGame;
            var size = inGame ? sizeGame : sizeMenu;
            popup.SetSize(size.x, size.y);
            Show();
            popup.Show();
        }

        /// <summary>The way-out button's label (string table key): "back to the main menu" unless changed (the tutorial's
        /// "skip the tutorial").</summary>
        public void SetMenuKey(string key)
        {
            var loc = toMenu.GetComponentInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true);
            if (loc) { loc.StringReference.TableEntryReference = key; loc.RefreshString(); }
            else { var t = toMenu.GetComponentInChildren<TextMeshProUGUI>(true); if (t) t.text = Loc.T(key); }
        }

        void Show()
        {
            musicValue.text = Mathf.RoundToInt(music.value * 100).ToString();
            sfxValue.text = Mathf.RoundToInt(sfx.value * 100).ToString();
            sensValue.text = Mathf.RoundToInt(sens.value).ToString();
        }
    }
}
