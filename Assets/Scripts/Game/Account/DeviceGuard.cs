using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.Subscriptions;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models.Data.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FlickCurling.Game.Account
{
    /// <summary>
    /// One device per account, the one that signed in last (the module's DeviceGuard). Signing in claims the account for
    /// this device; when another device claims it (a push message, or found on coming back from the background), this
    /// one steps aside: an online match is left without leaving its session (the other device plays it on), the game
    /// goes back to the main menu, and a popup that cannot be closed (Resources/DevicePopup) offers to sign in here
    /// again, which in turn moves the other device aside.
    /// </summary>
    public sealed class DeviceGuard : MonoBehaviour
    {
        const string DeviceKey = "device", MessageType = "deviceClaimed";

        /// <summary>This run of the app.</summary>
        public static string DeviceId => Online.OnlineLobby.DeviceId;
        /// <summary>Another device has the account now.</summary>
        public static bool Displaced { get; private set; }

        static DeviceGuard instance;
        string claimedFor = "";
        ISubscriptionEvents subscription;
        string subscribedFor = "";
        DevicePopup popup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Displaced = false; instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("DeviceGuard");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DeviceGuard>();
        }

        void OnEnable()
        {
            AccountService.Changed += OnAccount;
            Online.OnlineLobby.TakenOver += StepAside;   // the same player came into the match from another device
            OnAccount();
        }

        void OnDisable()
        {
            AccountService.Changed -= OnAccount;
            Online.OnlineLobby.TakenOver -= StepAside;
        }

        // signed in (or a different player now): claim the account for this device
        void OnAccount()
        {
            if (AccountService.Status != AccountService.State.Ready || Displaced) return;
            string player = AccountService.PlayerId;
            if (string.IsNullOrEmpty(player) || player == claimedFor) return;
            claimedFor = player;
            _ = ClaimAsync();
        }

        async Task ClaimAsync()
        {
            await SubscribeAsync();
            try { await CloudCodeService.Instance.CallModuleEndpointAsync(Online.OnlineApi.Module, "ClaimDevice", new Dictionary<string, object> { ["deviceId"] = DeviceId }); }
            catch (Exception e) { Debug.LogWarning("FlickCurling: claiming the device failed: " + e.Message); }
        }

        async Task SubscribeAsync()
        {
            string player = AccountService.PlayerId;
            if (subscription != null && subscribedFor == player) return;
            if (subscription != null)
            {
                try { await subscription.UnsubscribeAsync(); } catch (Exception) { }
                subscription = null;
            }
            var callbacks = new SubscriptionEventCallbacks();
            // whatever the message says, the note on the server decides (two devices claiming at once both hear a push)
            callbacks.MessageReceived += e => { if (e.MessageType == MessageType) _ = CheckAsync(); };
            try
            {
                subscription = await CloudCodeService.Instance.SubscribeToPlayerMessagesAsync(callbacks);
                subscribedFor = player;
            }
            catch (Exception e) { Debug.LogWarning("FlickCurling: device messages unavailable: " + e.Message); }
        }

        // back from the background: another device may have signed in meanwhile
        void OnApplicationPause(bool paused) { if (!paused) _ = CheckAsync(); }

        async Task CheckAsync()
        {
            if (Displaced || AccountService.Status != AccountService.State.Ready || string.IsNullOrEmpty(claimedFor)) return;
            string current;
            try
            {
                var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { DeviceKey },
                    new LoadOptions(new ProtectedReadAccessClassOptions()));
                current = items.TryGetValue(DeviceKey, out var item) ? item.Value.GetAs<string>() : null;
            }
            catch (Exception e) { Debug.LogWarning("FlickCurling: checking the device failed: " + e.Message); return; }
            if (!string.IsNullOrEmpty(current) && current != DeviceId) StepAside();
        }

        void StepAside()
        {
            if (Displaced) return;
            Displaced = true;
            Online.OnlineLobby.StepOut();
            // back to the main menu (from a match, online or not); the popup is on its own canvas, over every scene
            if (SceneManager.GetActiveScene().name != "MainMenu") ScreenFader.LoadScene("MainMenu");
            ShowPopup();
        }

        void ShowPopup()
        {
            if (popup) return;
            var prefab = Resources.Load<DevicePopup>(DevicePopup.ResourcePath);
            if (!prefab) return;
            var go = new GameObject("DeviceCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 900;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            popup = Instantiate(prefab, go.transform, false);
            popup.Run(Reclaim, () => Destroy(go));
        }

        // sign in here again: this device has the account, the other one steps aside
        async Task<bool> Reclaim()
        {
            Displaced = false;
            claimedFor = AccountService.PlayerId;
            await ClaimAsync();
            return true;
        }
    }
}
