using FlickCurling.Core;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The popup the main menu's "vs AI" and "Local" buttons open (built by MainMenuBuilder on the shared Popup): the mode
    /// (only the basic one for now) and, against the AI, its level. OK starts that match in the Game scene straight away.
    /// </summary>
    public sealed class MatchSetup : MonoBehaviour
    {
        [SerializeField] Popup popup;
        [SerializeField] LocalizeStringEvent title;
        [SerializeField] SegmentedToggle mode;
        [SerializeField] GameObject levelRow;
        [SerializeField] SegmentedToggle level;
        [SerializeField] Button ok;
        [SerializeField] Sprite iconAi, iconLocal;   // the menu buttons' icons, beside the title
        [SerializeField] Vector2 sizeAi = new Vector2(860, 785), sizeLocal = new Vector2(860, 609);

        // the modes, in the toggle's order, as the maps they play on
        static readonly string[] ModeMaps = { "base" };

        bool vsAi;

        void Awake() => ok.onClick.AddListener(StartMatch);

        public void Open(bool againstAi)
        {
            vsAi = againstAi;
            popup.SetIcon(againstAi ? iconAi : iconLocal);
            levelRow.SetActive(againstAi);
            mode.Select(0, false, false);   // the basic mode is picked each time
            var size = againstAi ? sizeAi : sizeLocal;
            popup.SetSize(size.x, size.y);
            popup.Show();
            // the title's key is switched once the popup is up: a LocalizeStringEvent on a hidden object does not refresh
            title.StringReference.SetReference(title.StringReference.TableReference, againstAi ? "matchsetup.titleAi" : "matchsetup.titleLocal");
            title.RefreshString();
        }

        void StartMatch()
        {
            ok.interactable = false;
            MatchLaunch.Set(vsAi, (AiLevel)level.Selected, ModeMaps[Mathf.Clamp(mode.Selected, 0, ModeMaps.Length - 1)]);
            SceneManager.LoadScene(MatchLaunch.GameScene);
        }
    }
}
