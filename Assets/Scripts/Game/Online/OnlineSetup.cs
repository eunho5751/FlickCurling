using System.Threading;
using Cysharp.Threading.Tasks;
using FlickCurling.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

namespace FlickCurling.Game.Online
{
    /// <summary>
    /// The popup the main menu's "Online Match" button opens (built by MainMenuBuilder on the shared Popup, like the
    /// offline one): the mode (random match only for now) and the map (the basic one), then OK, which starts looking for
    /// an opponent; while it looks, the button shows the time spent ("00:00"), and pressing it again or closing the
    /// popup stops. Once an opponent is found the match can no longer be cancelled: the Game scene opens on it (and if
    /// the two cannot be connected there, the menu comes back with this popup looking again).
    /// </summary>
    public sealed class OnlineSetup : MonoBehaviour
    {
        [SerializeField] Popup popup;
        [SerializeField] SegmentedToggle mode;
        [SerializeField] SegmentedToggle map;

        // the maps (modes), in the toggle's order
        static readonly string[] MapKeys = { "base", Maps.ItemKey };
        [SerializeField] Button ok;

        CancellationTokenSource search;
        float started;
        TextMeshProUGUI label;
        LocalizeStringEvent labelLoc;

        void Awake()
        {
            ok.onClick.AddListener(Toggle);
            popup.Closed += Stop;
            label = ok.GetComponentInChildren<TextMeshProUGUI>(true);
            labelLoc = ok.GetComponentInChildren<LocalizeStringEvent>(true);
        }

        void OnDestroy() => Stop();

        public void Open()
        {
            OnlineLobby.MeasureRegion();   // ahead of a match (in the background)
            mode.Select(0, false, false);
            map.Select(0, false, false);
            Stop();
            ok.interactable = true;
            popup.SetSize(860, 764);
            popup.Show();
        }

        /// <summary>Opens the popup already looking (back from a match that could not be connected).</summary>
        public void OpenSearching()
        {
            Open();
            Toggle();
            OnlineLobby.Trace("search started again (match not connected)");
        }

        void Toggle()
        {
            if (search != null) { Stop(); return; }
            search = new CancellationTokenSource();
            started = Time.unscaledTime;
            OnlineLobby.Trace("search started (online popup)");
            if (labelLoc) labelLoc.enabled = false;   // the time replaces "OK" until the search stops
            Tick();
            Find(search).Forget();
        }

        async UniTaskVoid Find(CancellationTokenSource cts)
        {
            OnlineLobby.SearchMap = MapKeys[Mathf.Clamp(map.Selected, 0, MapKeys.Length - 1)];
            bool found = await OnlineLobby.FindAsync(cts.Token, retry: true);
            if (!this || search != cts) return;
            search = null;
            if (!found) { Restore(); return; }
            ok.interactable = false;
            MatchLaunch.SetOnline();
            ScreenFader.LoadScene(MatchLaunch.GameScene);
        }

        void Stop()
        {
            if (OnlineLobby.Joining) return;   // a match found is no longer cancelled
            if (search == null) return;
            OnlineLobby.Trace("search cancelled (online popup)");
            search.Cancel();
            search = null;
            Restore();
        }

        void Restore()
        {
            if (labelLoc) { labelLoc.enabled = true; labelLoc.RefreshString(); }
        }

        void Update()
        {
            if (search != null) Tick();
        }

        void Tick()
        {
            int t = Mathf.FloorToInt(Time.unscaledTime - started);
            string text = $"{t / 60:00}:{t % 60:00}";
            if (label && label.text != text) label.text = text;
        }
    }
}
