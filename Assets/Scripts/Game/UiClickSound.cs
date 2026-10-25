using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The click every button makes when pressed, in every scene, with nothing to add to the buttons: one object, made at
    /// start-up and kept across scenes, looks under each new press (mouse or touch) and plays a soft wooden "tok" if the
    /// topmost UI there is an interactable button, toggle or dropdown (dropdown rows included), and the same click, a little
    /// softer, for a press on the background (no UI under it at all: the sheet, the menu's board). Sliders and other UI
    /// stay quiet. A <see cref="NoClickSound"/> on a button turns it off for that one. Follows the sound effect volume.
    /// </summary>
    public sealed class UiClickSound : MonoBehaviour
    {
        const int Rate = 44100;
        AudioSource src;
        AudioClip clip;
        const float BackgroundVolume = 0.7f;   // of a button's click
        static readonly List<RaycastResult> hits = new List<RaycastResult>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("UiClickSound");
            DontDestroyOnLoad(go);
            go.AddComponent<UiClickSound>();
        }

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            clip = MakeClick();
        }

        // a soft wooden tok: a quickly falling low tone under a short bright tick
        static AudioClip MakeClick()
        {
            int n = (int)(Rate * 0.07f);
            var data = new float[n];
            var r = new System.Random(3);
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float f = 520 + 380 * Mathf.Exp(-t / 0.012f);
                float tone = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t / 0.018f);
                lp += 0.35f * ((float)(r.NextDouble() * 2 - 1) - lp);
                float tick = lp * Mathf.Exp(-t / 0.004f);
                data[i] = (0.32f * tone + 0.22f * tick) * Mathf.Min(1, t / 0.0015f);
            }
            var c = AudioClip.Create("ui_click", n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        void Update()
        {
            foreach (var p in NewPresses())
            {
                if (PressesButton(p)) { src.PlayOneShot(clip, GameSettings.SfxVolume); break; }
                if (!OverUi(p)) { src.PlayOneShot(clip, GameSettings.SfxVolume * BackgroundVolume); break; }
            }
        }

        static readonly List<Vector2> presses = new List<Vector2>();

        /// <summary>Where presses began this frame (screen px): every new touch, else a mouse button going down.</summary>
        public static List<Vector2> NewPresses()
        {
            presses.Clear();
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var t in ts.touches)
                    if (t.press.wasPressedThisFrame) presses.Add(t.position.ReadValue());
            if (presses.Count == 0 && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                presses.Add(Mouse.current.position.ReadValue());
            return presses;
        }

        /// <summary>Whether any UI that takes presses is under this screen point.</summary>
        public static bool OverUi(Vector2 screen)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = screen }, hits);
            return hits.Count > 0;
        }

        static bool PressesButton(Vector2 screen)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = screen }, hits);
            if (hits.Count == 0) return false;
            // the topmost thing hit is what the press goes to (a screen fade's cover, a dim backdrop, ...)
            var s = hits[0].gameObject.GetComponentInParent<Selectable>();
            if (s == null || !s.IsInteractable() || s.GetComponent<NoClickSound>()) return false;
            return s is Button || s is Toggle || s is TMP_Dropdown || s is Dropdown;
        }
    }
}
