using FlickCurling.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// FlickCurling > Tutorial: forget that the tutorial was done (this device, and the signed-in player's Cloud Save
    /// note: at once in Play mode, else at the next start's check), and play from the Boot scene, which is where the
    /// tutorial is decided (Play from the main menu scene skips it).
    /// </summary>
    public static class TutorialTestMenu
    {
        const string Root = "FlickCurling/Tutorial/";

        [MenuItem(Root + "Reset Tutorial")]
        static void Reset()
        {
            TutorialProgress.ResetForTesting();
            Debug.Log("FlickCurling: the tutorial is not done any more (this device" + (Application.isPlaying ? " and the player's Cloud Save" : "; the player's Cloud Save note goes at the next start") + ")");
        }

        [MenuItem(Root + "Play From Boot")]
        static void PlayFromBoot()
        {
            if (Application.isPlaying) return;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Boot.unity");
            EditorApplication.playModeStateChanged += Restore;
            EditorApplication.isPlaying = true;
        }

        [MenuItem(Root + "Play From Boot", true)]
        static bool CanPlayFromBoot() => !Application.isPlaying;

        // the start scene only for that one Play
        static void Restore(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= Restore;
            EditorSceneManager.playModeStartScene = null;
        }
    }
}
