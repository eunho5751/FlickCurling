using FlickCurling.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The popup the main menu's "Offline Match" button opens (built by MainMenuBuilder on the shared Popup): the opponent
    /// (the AI or a second player on this device), the mode (only the basic one for now) and, against the AI, its level,
    /// all as group toggles. OK fades over to the Game scene, which starts that match.
    /// </summary>
    public sealed class MatchSetup : MonoBehaviour
    {
        [SerializeField] Popup popup;
        [SerializeField] SegmentedToggle opponent;   // 0 the AI, 1 local two players
        [SerializeField] SegmentedToggle mode;
        [SerializeField] GameObject levelRow;
        [SerializeField] SegmentedToggle level;
        [SerializeField] Button ok;
        [SerializeField] Vector2 sizeAi = new Vector2(860, 940), sizeLocal = new Vector2(860, 764);

        // the modes, in the toggle's order, as the maps they play on
        static readonly string[] ModeMaps = { "base" };

        bool VsAi => opponent.Selected == 0;

        void Awake()
        {
            ok.onClick.AddListener(StartMatch);
            opponent.Changed += _ => Fit();
        }

        public void Open()
        {
            // each time: against the AI, the basic mode
            opponent.Select(0, false, false);
            mode.Select(0, false, false);
            Fit();
            popup.Show();
        }

        // the level row only against the AI; the window grows or shrinks to match
        void Fit()
        {
            levelRow.SetActive(VsAi);
            var size = VsAi ? sizeAi : sizeLocal;
            popup.SetSize(size.x, size.y);
        }

        void StartMatch()
        {
            ok.interactable = false;
            MatchLaunch.Set(VsAi, (AiLevel)level.Selected, ModeMaps[Mathf.Clamp(mode.Selected, 0, ModeMaps.Length - 1)]);
            ScreenFader.LoadScene(MatchLaunch.GameScene);
        }
    }
}
