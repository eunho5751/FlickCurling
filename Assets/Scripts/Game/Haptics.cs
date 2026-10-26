using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// Short vibrations for the game's hits and throws, stronger for harder ones (Android: the HapticsBridge library in
    /// Assets/Plugins/Android/Haptics.androidlib; elsewhere nothing yet). Off with the settings' vibration switch.
    /// </summary>
    public static class Haptics
    {
        const float MinGap = 0.04f;   // s: hits closer than this make one buzz
        static float last = -1;

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject activity;
        static AndroidJavaClass bridge;
#endif

        /// <summary>A buzz of <paramref name="strength"/> (0..1).</summary>
        public static void Pulse(float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (!GameSettings.Vibration || strength < 0.05f) return;
            float now = Time.unscaledTime;
            if (now - last < MinGap) return;
            last = now;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (bridge == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    bridge = new AndroidJavaClass("com.flickcurling.haptics.HapticsBridge");
                }
                bridge.CallStatic("pulse", activity, Mathf.RoundToInt(Mathf.Lerp(10, 40, strength)), Mathf.RoundToInt(Mathf.Lerp(50, 255, strength)));
            }
            catch (System.Exception e) { Debug.LogWarning("FlickCurling: vibration failed: " + e.Message); }
#endif
        }
    }
}
