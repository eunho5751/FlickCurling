using FlickCurling.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The popup the main menu's "Offline Match" button opens (built by MainMenuBuilder on the shared Popup): the mode
    /// (against the AI or two players on this device), against the AI its level, and the map (only the basic one for now),
    /// all as group toggles. OK fades over to the Game scene, which starts that match.
    /// </summary>
    public sealed class MatchSetup : MonoBehaviour
    {
        [SerializeField] Popup popup;
        [SerializeField] SegmentedToggle mode;   // 0 against the AI, 1 local two players
        [SerializeField] SegmentedToggle map;
        [SerializeField] GameObject levelRow;
        [SerializeField] SegmentedToggle level;
        [SerializeField] Button ok;
        [SerializeField] Vector2 sizeAi = new Vector2(860, 940), sizeLocal = new Vector2(860, 764);

        // the maps, in the toggle's order
        static readonly string[] MapKeys = { "base", Maps.ItemKey };

        bool VsAi => mode.Selected == 0;

        void Awake()
        {
            ok.onClick.AddListener(StartMatch);
            mode.Changed += _ => Fit();
        }

        public void Open()
        {
            // each time: against the AI, the basic map
            mode.Select(0, false, false);
            map.Select(0, false, false);
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
            MatchLaunch.Set(VsAi, (AiLevel)level.Selected, MapKeys[Mathf.Clamp(map.Selected, 0, MapKeys.Length - 1)]);
            ScreenFader.LoadScene(MatchLaunch.GameScene);
        }
    }
}
