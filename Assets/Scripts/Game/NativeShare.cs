using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// Hands a line of text to the phone's own share sheet (Android: an ACTION_SEND chooser, so the player picks the
    /// messenger). Elsewhere (the editor) the text goes to the clipboard instead.
    /// </summary>
    public static class NativeShare
    {
        public static void Text(string text, string title = "")
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent");
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                intent.Call<AndroidJavaObject>("setType", "text/plain");
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);
                using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, title);
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("startActivity", chooser);
                return;
            }
            catch (System.Exception e) { Debug.LogWarning("FlickCurling: sharing failed: " + e.Message); }
#endif
            GUIUtility.systemCopyBuffer = text;
        }
    }
}
