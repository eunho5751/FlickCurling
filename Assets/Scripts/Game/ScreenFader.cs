using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// A full-screen fade to the table's dark brown and back, shared by every scene (the prefab Resources/ScreenFader, built
    /// by UiPrefabBuilder; it lives across scene loads). While a fade runs nothing takes a touch: its overlay blocks the UI
    /// and <see cref="Busy"/> tells the game's own input to hold off. <see cref="LoadScene"/> fades out, loads, fades in.
    /// </summary>
    public sealed class ScreenFader : MonoBehaviour
    {
        public const string ResourcePath = "ScreenFader";

        [SerializeField] CanvasGroup group;
        [SerializeField] float duration = 0.35f;

        static ScreenFader instance;
        int running;
        readonly List<Action> whenClear = new List<Action>();

        public static ScreenFader Instance
        {
            get
            {
                if (instance) return instance;
                var prefab = Resources.Load<ScreenFader>(ResourcePath);
                instance = prefab ? Instantiate(prefab) : Fallback();
                instance.name = "ScreenFader";
                DontDestroyOnLoad(instance.gameObject);
                instance.group.alpha = 0; instance.group.blocksRaycasts = false;
                return instance;
            }
        }

        /// <summary>A fade is running (or the screen is held covered): touches are not for the game now.</summary>
        public static bool Busy => instance && (instance.running > 0 || instance.group.alpha > 0.001f);

        /// <summary>Runs <paramref name="action"/> now, or once the screen is clear again if a fade is under way.</summary>
        public static void WhenClear(Action action)
        {
            if (!Busy) action();
            else instance.whenClear.Add(action);
        }

        /// <summary>Fades out, loads the scene and fades in on it.</summary>
        public static void LoadScene(string scene) => Instance.LoadAsync(scene).Forget();

        async UniTaskVoid LoadAsync(string scene)
        {
            running++;
            try
            {
                await FadeOut();
                await SceneManager.LoadSceneAsync(scene);
                await Settle();   // the new scene's set-up frames stay covered
                await FadeIn();
            }
            finally { running--; Flush(); }
        }

        /// <summary>Covers the screen; it stays covered (and touches stay blocked) until <see cref="FadeIn"/>.</summary>
        public UniTask FadeOut() => Fade(1);

        /// <summary>Uncovers the screen; touches come back when it is clear.</summary>
        public async UniTask FadeIn()
        {
            await Fade(0);
            Flush();
        }

        // a freshly loaded scene spends its first frames building (one can take most of a second); the fade waits until a
        // few frames in a row come quickly, so it then runs smoothly over what the scene will look like
        async UniTask Settle()
        {
            int quick = 0;
            for (int i = 0; i < 120 && quick < 3; i++)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
                quick = Time.unscaledDeltaTime < 0.05f ? quick + 1 : 0;
            }
        }

        async UniTask Fade(float to)
        {
            running++;
            try
            {
                group.blocksRaycasts = true;
                float from = group.alpha;
                // each frame moves the fade by at most a 30th of a second, so a slow frame (a scene loading) slows it down
                // rather than making it jump
                for (float t = 0; t < duration; t += Mathf.Min(Time.unscaledDeltaTime, 1 / 30f))
                {
                    group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / duration));
                    await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
                }
                group.alpha = to;
                group.blocksRaycasts = to > 0;
            }
            finally { running--; }
        }

        void Flush()
        {
            if (Busy || whenClear.Count == 0) return;
            var list = whenClear.ToArray();
            whenClear.Clear();
            foreach (var a in list) a();
        }

        // without the prefab: the same overlay made in code
        static ScreenFader Fallback()
        {
            var go = new GameObject("ScreenFader");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = SortingOrder;
            go.AddComponent<GraphicRaycaster>();
            var img = new GameObject("Cover", typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            var rt = img.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.color = Cover;
            var f = go.AddComponent<ScreenFader>();
            f.group = go.AddComponent<CanvasGroup>();
            return f;
        }

        public const int SortingOrder = 32000;
        public static readonly Color Cover = new Color32(0x1a, 0x11, 0x0b, 0xff);   // the table's dark brown
    }
}
