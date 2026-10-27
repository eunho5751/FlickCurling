using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlickCurling.Game
{
    /// <summary>
    /// The background music, in every scene, with nothing to add to them: one object, made at start-up and kept across
    /// scenes, plays the menu loop (Resources/Music/menu) outside a match and the calmer match loop (Resources/Music/game)
    /// in the game scene, crossfading as the scene changes (a new match in the game scene keeps the loop going). Both are
    /// made by Tools/Music/compose.py. Follows the music volume in the settings; real time, so it plays on while the game
    /// stands still (the settings popup off line, the network popup).
    /// </summary>
    public sealed class Music : MonoBehaviour
    {
        const float Level = 0.6f;     // the loops at full music volume, under the sound effects
        const float Fade = 1.2f;      // seconds of a crossfade

        AudioSource[] srcs;
        int cur;                      // the source playing the loop wanted now; the other fades out
        float mix = 1;                // 0..1: how far the current one has faded in
        AudioClip menu, game;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("Music");
            DontDestroyOnLoad(go);
            go.AddComponent<Music>();
        }

        void Awake()
        {
            menu = Resources.Load<AudioClip>("Music/menu");
            game = Resources.Load<AudioClip>("Music/game");
            srcs = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                var s = srcs[i] = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = true; s.volume = 0;
            }
            SceneManager.activeSceneChanged += OnScene;
            Play(For(SceneManager.GetActiveScene()), false);
        }

        void OnDestroy() => SceneManager.activeSceneChanged -= OnScene;

        AudioClip For(Scene s) => s.name == MatchLaunch.GameScene ? game : menu;

        void OnScene(Scene _, Scene now) => Play(For(now), true);

        void Play(AudioClip clip, bool fade)
        {
            if (!clip || srcs[cur].clip == clip && srcs[cur].isPlaying) return;
            cur = 1 - cur;
            var s = srcs[cur];
            s.clip = clip; s.time = 0; s.Play();
            mix = fade ? 0 : 1;
            Apply();
        }

        void Update()
        {
            // (a frame's step capped: the frame a scene loads in can take a second and would skip the fade)
            if (mix < 1) mix = Mathf.Min(1, mix + Mathf.Min(Time.unscaledDeltaTime, 0.05f) / Fade);
            Apply();
        }

        void Apply()
        {
            float v = Level * GameSettings.MusicVolume;
            srcs[cur].volume = v * mix;
            var old = srcs[1 - cur];
            old.volume = v * (1 - mix);
            if (mix >= 1 && old.isPlaying) old.Stop();
        }
    }
}
