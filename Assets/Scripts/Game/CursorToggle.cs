using UnityEngine;
using UnityEngine.InputSystem;

namespace FlickCurling.Game
{
    /// <summary>
    /// F8 hides the mouse pointer over the game (and shows it again), for recording or capturing the screen: one object,
    /// made at start-up and kept across scenes, in the editor and on desktop builds only (phones have no pointer). Clicks
    /// still work with it hidden.
    /// </summary>
    public sealed class CursorToggle : MonoBehaviour
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("CursorToggle");
            DontDestroyOnLoad(go);
            go.AddComponent<CursorToggle>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) Cursor.visible = !Cursor.visible;
        }

        // (the editor keeps the pointer hidden after play mode otherwise)
        void OnDestroy() => Cursor.visible = true;
#endif
    }
}
