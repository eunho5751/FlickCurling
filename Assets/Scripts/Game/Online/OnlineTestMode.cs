#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FlickCurling.Game.Online
{
    /// <summary>
    /// Editor only: online matches without the matchmaker. With it on (FlickCurling > Online Test > Use Test Session),
    /// "find a match" puts every editor player (Multiplayer Play Mode's virtual players included) into one fixed session,
    /// so two of them play each other.
    /// </summary>
    public static class OnlineTestMode
    {
        const string Key = "FlickCurling.OnlineTestMode";
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Key, false);
            set => EditorPrefs.SetBool(Key, value);
        }

        /// <summary>One session per project folder, so other projects' tests stay apart.</summary>
        public static string SessionId => "flickcurling-test-" + (Mathf.Abs(Application.dataPath.GetHashCode()) % 100000);
    }
}
#endif
