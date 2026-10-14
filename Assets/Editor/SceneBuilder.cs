using System.IO;
using FlickCurling.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Game.unity from scratch so the scene is reproducible.
    /// Batch mode: Unity -batchmode -quit -projectPath . -executeMethod FlickCurling.EditorTools.SceneBuilder.Build
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Game.unity";
        const string MatPath = "Assets/Art/Unlit.mat", ShapeMatPath = "Assets/Art/Shapes.mat";

        [MenuItem("FlickCurling/Build Game Scene")]
        public static void Build()
        {
            EnsureLayer(8, "Overlay");
            EnsureLayer(9, "Minimap");
            var mat = EnsureMaterial(MatPath, "Universal Render Pipeline/2D/Sprite-Unlit-Default");
            var shapeMat = EnsureMaterial(ShapeMatPath, "FlickCurling/VertexColor");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 120;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x20, 0x16, 0x0f, 0xff);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 50;
            camGo.transform.position = new Vector3(50, 111, -10);
            camGo.AddComponent<AudioListener>();

            var miniGo = new GameObject("Minimap Camera");
            var mini = miniGo.AddComponent<Camera>();
            mini.orthographic = true;
            mini.orthographicSize = 111;
            mini.clearFlags = CameraClearFlags.SolidColor;
            mini.backgroundColor = new Color32(0x5a, 0x34, 0x18, 0xff);
            mini.nearClipPlane = 0.1f; mini.farClipPlane = 50;
            mini.depth = 1;
            miniGo.transform.position = new Vector3(50, 111, -10);

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            var game = new GameObject("Game");
            var gc = game.AddComponent<GameController>();
            var so = new SerializedObject(gc);
            so.FindProperty("unlitMaterial").objectReferenceValue = mat;
            so.FindProperty("shapeMaterial").objectReferenceValue = shapeMat;
            so.FindProperty("mainCam").objectReferenceValue = cam;
            so.FindProperty("miniCam").objectReferenceValue = mini;
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            // portrait phone first; a portrait window on desktop
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;   // keeps play mode ticking while the editor is unfocused
            AssetDatabase.SaveAssets();
            Debug.Log("FlickCurling: built " + ScenePath);
        }

        static Material EnsureMaterial(string path, string shaderName)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat) return mat;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var shader = Shader.Find(shaderName);
            if (!shader) throw new System.InvalidOperationException("FlickCurling: shader not found: " + shaderName);
            mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void EnsureLayer(int index, string name)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            var p = layers.GetArrayElementAtIndex(index);
            if (p.stringValue == name) return;
            if (!string.IsNullOrEmpty(p.stringValue)) Debug.LogWarning($"FlickCurling: layer {index} was '{p.stringValue}', renaming to '{name}'");
            p.stringValue = name;
            tm.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
