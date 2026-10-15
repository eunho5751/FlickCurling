using UnityEditor;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// The running game lives in plain C# objects that a domain reload wipes, so a script change picked up in Play mode
    /// left the game throwing every frame with dead controls. Reloads are held while playing and run on exit instead.
    /// </summary>
    [InitializeOnLoad]
    static class PlayModeReloadGuard
    {
        static bool locked;

        static PlayModeReloadGuard() => EditorApplication.playModeStateChanged += OnPlayMode;

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && !locked) { EditorApplication.LockReloadAssemblies(); locked = true; }
            else if (s == PlayModeStateChange.ExitingPlayMode && locked) { EditorApplication.UnlockReloadAssemblies(); locked = false; }
        }
    }
}
