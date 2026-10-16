using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The main menu's settings popup (built by MainMenuBuilder on the shared Popup): music and sound effect volume
    /// sliders, the aim sensitivity slider (levels 1 to 5) and the language dropdown. Every change is saved and applied at
    /// once (GameSettings, Unity Localization).
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        [SerializeField] Popup popup;
        [SerializeField] Slider music, sfx, sens;
        [SerializeField] TextMeshProUGUI musicValue, sfxValue, sensValue;
        [SerializeField] TMP_Dropdown language;

        void Awake()
        {
            language.ClearOptions();
            language.AddOptions(Loc.NativeNames.ToList());
            music.onValueChanged.AddListener(v => { GameSettings.MusicVolume = v; Show(); });
            sfx.onValueChanged.AddListener(v => { GameSettings.SfxVolume = v; Show(); });
            sens.onValueChanged.AddListener(v => { GameSettings.SensLevel = Mathf.RoundToInt(v) - 1; Show(); });
            language.onValueChanged.AddListener(i => Loc.Select(Loc.Codes[i]));
        }

        public void Open()
        {
            // the saved values, without writing them back
            music.SetValueWithoutNotify(GameSettings.MusicVolume);
            sfx.SetValueWithoutNotify(GameSettings.SfxVolume);
            sens.SetValueWithoutNotify(GameSettings.SensLevel + 1);
            language.SetValueWithoutNotify(Mathf.Max(0, System.Array.IndexOf(Loc.Codes, Loc.Code)));
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
