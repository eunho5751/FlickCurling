using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// The game needs the network. Every CheckInterval seconds it checks that the internet is really there (not just a
    /// network interface: a captive Wi-Fi page fails too, see <see cref="IsOnlineAsync"/>); while it is not, or while
    /// signing in to UGS has failed, a popup that cannot be closed (Resources/NetworkPopup) dims every scene and the game
    /// clock stops (timeScale 0, put back after). Once the network is back a failed sign-in is tried again; the popup goes
    /// when both are fine (the main menu then asks a new player's nickname, as on any sign-in).
    /// </summary>
    public sealed class NetworkGuard : MonoBehaviour
    {
        // the address Android checks its own connection with: an empty 204 answer, anywhere (a login page answers otherwise)
        const string CheckUrl = "https://connectivitycheck.gstatic.com/generate_204";
        const int CheckTimeout = 2;        // seconds
        const float CheckInterval = 3;     // seconds

        /// <summary>The internet was there at the last check.</summary>
        public static bool Online { get; private set; } = true;
        /// <summary>The popup is up: nothing under it takes input, and the game clock stands still.</summary>
        public static bool Blocking => instance && instance.popup;
        /// <summary>The popup has gone (the network is back): the game goes on from where it stopped.</summary>
        public static event System.Action Resumed;

        static NetworkGuard instance;
        NetworkPopup popup;
        GameObject canvas;
        float next, savedTimeScale = 1;
        bool checking;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; Online = true; Resumed = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("NetworkGuard");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NetworkGuard>();
        }

        void Update()
        {
            if (checking || Time.unscaledTime < next) return;
            next = Time.unscaledTime + CheckInterval;
            CheckAsync();
        }

        async void CheckAsync()
        {
            checking = true;
            try
            {
                Online = await IsOnlineAsync();
                if (!this) return;
                // back on the network after a failed sign-in (the first one, at start, included): signed in again
                if (Online && AccountService.Status == AccountService.State.Failed) await AccountService.StartAsync();
                if (!this) return;
                bool blocked = !Online || AccountService.Status == AccountService.State.Failed;
                if (blocked) Show(); else Hide();
            }
            finally
            {
                checking = false;
                next = Time.unscaledTime + CheckInterval;   // (counted from the end of this check)
            }
        }

        /// <summary>Whether the internet is there: some network interface first (no request without one), then an empty
        /// 204 answer from <see cref="CheckUrl"/> within <see cref="CheckTimeout"/> seconds.</summary>
        public static async Task<bool> IsOnlineAsync()
        {
            if (Application.internetReachability == NetworkReachability.NotReachable) return false;
            using var req = UnityWebRequest.Get(CheckUrl);
            req.timeout = CheckTimeout;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            return req.result == UnityWebRequest.Result.Success && req.responseCode == 204;
        }

        void Show()
        {
            if (popup) return;
            var prefab = Resources.Load<NetworkPopup>(NetworkPopup.ResourcePath);
            if (!prefab) return;
            canvas = new GameObject("NetworkCanvas", typeof(RectTransform));
            canvas.transform.SetParent(transform, false);
            var c = canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 950;   // over everything, the device popup too
            var scaler = canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            canvas.AddComponent<GraphicRaycaster>();
            popup = Instantiate(prefab, canvas.transform, false);
            popup.Show();
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0;
        }

        void Hide()
        {
            if (!popup) return;
            Time.timeScale = savedTimeScale;
            var go = canvas;
            popup.Hide(() => Destroy(go));
            popup = null; canvas = null;
            Resumed?.Invoke();
        }
    }
}
