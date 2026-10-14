using System.Collections.Generic;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>Generated sounds, as the prototype's Web Audio blips: band-passed noise bursts and a triangle chime.</summary>
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
            src.PlayOneShot(Blip(freq, q, len), Mathf.Min(1, vol) * 0.9f);
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
            src.PlayOneShot(clip, 1);
        }
    }
}
