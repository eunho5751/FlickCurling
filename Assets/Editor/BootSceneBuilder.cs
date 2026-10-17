using System.Linq;
using FlickCurling.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Boot.unity: a camera clearing to the screen fade's cover colour (so the hand-over to the main
    /// menu's fade-in is seamless) and the Boot object, and puts the scene first in the build so the app starts there.
    /// </summary>
    public static class BootSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Boot.unity";

        [MenuItem("FlickCurling/Build Boot Scene")]
        public static void Build()
        {
            var previous = EditorSceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ScreenFader.Cover;
            cam.orthographic = true;
            new GameObject("Boot").AddComponent<Boot>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            // first in the build, then the main menu it goes to; the others keep their order after them
            const string menuPath = "Assets/Scenes/" + Boot.NextScene + ".unity";
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath && s.path != menuPath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            scenes.Insert(1, new EditorBuildSettingsScene(menuPath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();

            if (!string.IsNullOrEmpty(previous) && previous != ScenePath) EditorSceneManager.OpenScene(previous);
            Debug.Log("FlickCurling: built " + ScenePath + " (first in the build)");
        }
    }
}
