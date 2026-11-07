using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// Whether Google Play has a newer build of the game to install (asked in the background, from the online popup), and
    /// Play's own full-screen "update now" for it, through the Java bridge in Assets/Plugins/Android/AppUpdate.androidlib.
    /// Online matches need the newest build (the two devices must simulate alike); offline ones never ask. Only an app
    /// installed from Google Play can be told of an update: anywhere else (the editor, a build put on by hand) there is none.
    /// </summary>
    public static class AppUpdate
    {
        public enum State { Unknown, None, Available }

        /// <summary>What Play said last (Unknown until it has answered, or if it could not tell).</summary>
        public static State Status { get; private set; }

        static bool checking;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Status = State.Unknown; checking = false; }

        /// <summary>Asks Play again, in the background (<see cref="Status"/> once it answers).</summary>
        public static async void Check()
        {
            if (checking) return;
            checking = true;
            try
            {
                int r = await CallAsync("check");
                Status = r > 0 ? State.Available : r == 0 ? State.None : State.Unknown;
            }
            finally { checking = false; }
        }

        /// <summary>
        /// Play's update screen. True if the update was taken (the app restarts on the new build); false if the player
        /// turned it down. If Play could not run it, the game's store page opens instead.
        /// </summary>
        public static async Task<bool> StartAsync()
        {
            int r = await CallAsync("start");
            if (r == 0) Check();   // (turned down, or none left to take: asked again)
            if (r < 0) Application.OpenURL("market://details?id=" + Application.identifier);
            return r > 0;
        }

        // the bridge's answer: 1, 0 or -1 (see AppUpdateBridge); awaited on Unity's thread, so it comes back there
        static Task<int> CallAsync(string method)
        {
#if UNITY_EDITOR
            return Task.FromResult(UnityEditor.EditorPrefs.GetBool(SimulateKey) ? (method == "check" ? 1 : 0) : 0);
#elif UNITY_ANDROID
            var done = new TaskCompletionSource<int>();
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var bridge = new AndroidJavaClass("com.flickcurling.update.AppUpdateBridge");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                bridge.CallStatic(method, activity, new Listener(done));
            }
            catch (Exception e)
            {
                Debug.LogWarning("FlickCurling: asking Google Play for an update failed: " + e.Message);
                done.TrySetResult(-1);
            }
            return done.Task;
#else
            return Task.FromResult(0);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // the bridge answers on one of its threads; the task carries the answer back to Unity's
        sealed class Listener : AndroidJavaProxy
        {
            readonly TaskCompletionSource<int> done;
            public Listener(TaskCompletionSource<int> done) : base("com.flickcurling.update.AppUpdateBridge$Listener") => this.done = done;
            void onResult(int code) => done.TrySetResult(code);
        }
#endif

#if UNITY_EDITOR
        // the editor has no Play store: this menu toggle plays "an update is there" to see the online popup's update view
        const string SimulateKey = "FlickCurling.SimulateAppUpdate";
        const string SimulateMenu = "FlickCurling/Online/Simulate An App Update";

        [UnityEditor.MenuItem(SimulateMenu)]
        static void ToggleSimulate()
        {
            UnityEditor.EditorPrefs.SetBool(SimulateKey, !UnityEditor.EditorPrefs.GetBool(SimulateKey));
            Status = State.Unknown;
        }

        [UnityEditor.MenuItem(SimulateMenu, true)]
        static bool ToggleSimulateCheck()
        {
            UnityEditor.Menu.SetChecked(SimulateMenu, UnityEditor.EditorPrefs.GetBool(SimulateKey));
            return true;
        }
#endif
    }
}
