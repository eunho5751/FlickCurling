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
            // released on the way out, and again on reaching edit mode in case the exit was missed (a quick stop right
            // after entering, with domain reload off, could leave the lock on and stall every compile after it)
            else if ((s == PlayModeStateChange.ExitingPlayMode || s == PlayModeStateChange.EnteredEditMode) && locked) { EditorApplication.UnlockReloadAssemblies(); locked = false; }
        }
    }
}
