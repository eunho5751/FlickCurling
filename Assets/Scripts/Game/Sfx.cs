using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>Generated sounds, as the prototype's Web Audio blips: band-passed noise bursts and a triangle chime, plus
    /// soft synth cues (turn start, pop-up, end over, a void shot, the clock running low).</summary>
    public sealed class Sfx
    {
        const int Rate = 44100;
        readonly AudioSource src;
        readonly float[] noise;
        readonly Dictionary<(int, float, float), AudioClip> blips = new Dictionary<(int, float, float), AudioClip>();
        readonly Dictionary<int, AudioClip> chimes = new Dictionary<int, AudioClip>();
        float lastSound;
        public bool On = true;

        public Sfx(GameObject host)
        {
            src = host.AddComponent<AudioSource>();
            src.playOnAwake = false;
            var r = new System.Random(5);
            int n = (int)(Rate * 0.08f);
            noise = new float[n];
            for (int i = 0; i < n; i++) noise[i] = (float)(r.NextDouble() * 2 - 1) * Mathf.Pow(1 - (float)i / n, 3);
        }

        // ---- synth cues ----

        static AudioClip Make(string name, float len, Func<float, float> f)
        {
            int n = (int)(Rate * len);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f((float)i / Rate);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // a soft bell: a sine with a faint inharmonic partial, a 5 ms attack and an exponential decay over len
        static float Bell(float t, float f, float len)
        {
            if (t < 0 || t > len) return 0;
            float env = Mathf.Min(1, t / 0.005f) * Mathf.Pow(0.001f, t / len);
            return env * (Mathf.Sin(2 * Mathf.PI * f * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * f * 2.76f * t) * Mathf.Pow(0.01f, t / len));
        }

        // a mellow square-ish tone (odd harmonics, rolled off) whose pitch slides from f0 to f1
        static float Reed(float t, float f0, float f1, float len)
        {
            if (t < 0 || t > len) return 0;
            float u = t / len, ph = 2 * Mathf.PI * (f0 * t + (f1 - f0) * t * u / 2);
            float env = Mathf.Min(1, t / 0.01f) * (u < 0.7f ? 1 : (1 - u) / 0.3f);
            return env * (Mathf.Sin(ph) + Mathf.Sin(3 * ph) / 5 + Mathf.Sin(5 * ph) / 12);
        }

        AudioClip turnClip, popClip, endClip, voidClip, warnClip;

        void Play(ref AudioClip clip, Func<AudioClip> make)
        {
            if (!On) return;
            if (clip == null) clip = make();
            src.PlayOneShot(clip, GameSettings.SfxVolume);
        }

        /// <summary>A turn begins: two rising bell notes.</summary>
        public void TurnStart() => Play(ref turnClip, () => Make("turn", 0.6f, t => 0.22f * (Bell(t, 784, 0.35f) + Bell(t - 0.11f, 1175, 0.45f))));

        /// <summary>A card pops up: a quick upward blip.</summary>
        public void Pop() => Play(ref popClip, () => Make("pop", 0.12f, t =>
        {
            float u = Mathf.Min(1, t / 0.07f);
            return 0.35f * Mathf.Sin(2 * Mathf.PI * (380 * t + 300 * t * u)) * Mathf.Min(1, t / 0.004f) * Mathf.Pow(0.01f, t / 0.12f);
        }));

        /// <summary>An end is over: a rising arpeggio, C E G C.</summary>
        public void EndOver() => Play(ref endClip, () => Make("end", 1.1f, t =>
            0.18f * (Bell(t, 523, 0.4f) + Bell(t - 0.09f, 659, 0.4f) + Bell(t - 0.18f, 784, 0.45f) + Bell(t - 0.27f, 1046, 0.8f))));

        /// <summary>The shot does not count (short of the min line, past the back line, a lost cushion, out of time): a
        /// falling "wah-wah".</summary>
        public void Void() => Play(ref voidClip, () => Make("void", 0.6f, t => 0.2f * (Reed(t, 392, 370, 0.18f) + Reed(t - 0.2f, 294, 247, 0.38f))));

        /// <summary>The clock is running low: two quick high beeps.</summary>
        public void Warn() => Play(ref warnClip, () => Make("warn", 0.25f, t =>
        {
            float a = t < 0.07f ? t : t > 0.13f && t < 0.2f ? t - 0.13f : -1;
            return a < 0 ? 0 : 0.28f * Mathf.Sin(2 * Mathf.PI * 1318 * t) * Mathf.Min(1, a / 0.004f) * Mathf.Min(1, (0.07f - a) / 0.01f);
        }));

        AudioClip Blip(int freq, float q, float len)
        {
            var key = (freq, q, len);
            if (blips.TryGetValue(key, out var clip)) return clip;
            int n = (int)(Rate * (len + 0.02f));
            var data = new float[n];
            // RBJ band-pass (constant 0 dB peak), like a BiquadFilterNode of type "bandpass"
            float w0 = 2 * Mathf.PI * freq / Rate, alpha = Mathf.Sin(w0) / (2 * q), cos = Mathf.Cos(w0);
            float a0 = 1 + alpha, b0 = alpha / a0, b2 = -alpha / a0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
            float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < n; i++)
            {
                float x = i < noise.Length ? noise[i] : 0;
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                float t = (float)i / Rate;
                float env = t < len ? Mathf.Pow(0.001f, t / len) : 0;   // exponential ramp from 1 to 0.001 over len
                data[i] = y * env;
            }
            clip = AudioClip.Create($"blip{freq}", n, 1, Rate, false);
            clip.SetData(data, 0);
            blips[key] = clip;
            return clip;
        }

        public void Blip(float vol, int freq, float q = 6, float len = 0.06f)
        {
            if (!On || vol < 0.02f) return;
            float now = Time.realtimeSinceStartup;
            if (now - lastSound < 0.018f) return;
            lastSound = now;
            src.PlayOneShot(Blip(freq, q, len), Mathf.Min(1, vol) * 0.9f * GameSettings.SfxVolume);
        }

        public void Chime(int k)
        {
            if (!On) return;
            if (!chimes.TryGetValue(k, out var clip))
            {
                float f = 660 * Mathf.Pow(1.26f, k - 1);
                int n = (int)(Rate * 0.4f);
                var data = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    float ph = t * f % 1;
                    float tri = 4 * Mathf.Abs(ph - 0.5f) - 1;
                    float env = t < 0.01f ? 0.0001f * Mathf.Pow(0.12f / 0.0001f, t / 0.01f) : (t < 0.35f ? 0.12f * Mathf.Pow(0.0001f / 0.12f, (t - 0.01f) / 0.34f) : 0);
                    data[i] = tri * env;
                }
                clip = AudioClip.Create($"chime{k}", n, 1, Rate, false);
                clip.SetData(data, 0);
                chimes[k] = clip;
            }
            src.PlayOneShot(clip, GameSettings.SfxVolume);
        }
    }
}
