using FlickCurling.Game.Online;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Builds Resources/OnlineMatchSync.prefab and Resources/OnlinePlayerState.prefab (the networked objects of an online
    /// match) and holds the online test menu.
    /// </summary>
    public static class OnlineBuilder
    {
        const string PrefabPath = "Assets/Resources/" + OnlineMatchSync.ResourcePath + ".prefab";
        const string CardPath = "Assets/Resources/" + OnlinePlayerState.ResourcePath + ".prefab";
        const string TestMenu = "FlickCurling/Online Test/Use Test Session (no matchmaker)";

        [MenuItem("FlickCurling/Build Online Prefab")]
        public static void Build()
        {
            // the records panel's art (Codex): the card is 9-sliced around its frame
            MainMenuBuilder.ImportSprite("Assets/Resources/Online/vs_card.png", new Vector4(48, 48, 48, 48), 1024);
            foreach (var n in new[] { "vs_emblem", "stone_black", "stone_white" })
                MainMenuBuilder.ImportSprite("Assets/Resources/Online/" + n + ".png", Vector4.zero, 1024);

            var go = new GameObject(OnlineMatchSync.ResourcePath);
            var no = go.AddComponent<NetworkObject>();
            go.AddComponent<OnlineMatchSync>();
            // owned by whoever owns the session, so it stays when either player leaves
            var so = new SerializedObject(no);
            so.FindProperty("Ownership").intValue = (int)NetworkObject.OwnershipStatus.SessionOwner;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log("Built " + PrefabPath);

            // each player's card: owned by its device and gone with it (not handed to the other)
            var card = new GameObject(OnlinePlayerState.ResourcePath);
            var cno = card.AddComponent<NetworkObject>();
            card.AddComponent<OnlinePlayerState>();
            var cso = new SerializedObject(cno);
            cso.FindProperty("Ownership").intValue = (int)NetworkObject.OwnershipStatus.None;
            cso.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(card, CardPath);
            Object.DestroyImmediate(card);
            Debug.Log("Built " + CardPath);
        }

        [MenuItem(TestMenu)]
        static void ToggleTest() => OnlineTestMode.Enabled = !OnlineTestMode.Enabled;

        [MenuItem(TestMenu, true)]
        static bool ToggleTestCheck()
        {
            Menu.SetChecked(TestMenu, OnlineTestMode.Enabled);
            return true;
        }
    }
}
