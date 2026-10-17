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
    /// 5) and the language dropdown, plus, in the game, a "back to the main menu" button at the bottom. Every change is
    /// saved and applied at once (GameSettings, Unity Localization).
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        public const string ResourcePath = "SettingsPopup";

        [SerializeField] Popup popup;
        [SerializeField] Slider music, sfx, sens;
        [SerializeField] TextMeshProUGUI musicValue, sfxValue, sensValue;
        [SerializeField] TMP_Dropdown language;
        [SerializeField] GameObject toMenuRow;
        [SerializeField] Button toMenu;
        [SerializeField] Vector2 sizeMenu = new Vector2(860, 820), sizeGame = new Vector2(860, 1010);
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
            toMenu.onClick.AddListener(() => { popup.Hide(); ToMenu?.Invoke(); });
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
            toMenuRow.SetActive(inGame);
            var size = inGame ? sizeGame : sizeMenu;
            popup.SetSize(size.x, size.y);
            Show();
            popup.Show();
        }

        void Show()
        {
            musicValue.text = Mathf.RoundToInt(music.value * 100).ToString();
            sfxValue.text = Mathf.RoundToInt(sfx.value * 100).ToString();
            sensValue.text = Mathf.RoundToInt(sens.value).ToString();
        }
    }
}
