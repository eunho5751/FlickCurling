using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>
    /// The shot clock's burning tip: sparks thrown off it and a wisp of smoke, in sheet units. Particles live in world
    /// space, so as the tip creeps along the fuse they are left behind where they were thrown.
    /// </summary>
    sealed class FuseFx
    {
        readonly Transform root;
        readonly ParticleSystem sparks, smoke;
        bool on;

        public FuseFx(Transform parent, Material spriteMat, int layer)
        {
            var go = new GameObject("FuseFx") { layer = layer };
            root = go.transform;
            root.SetParent(parent, false);
            var mat = new Material(spriteMat) { name = "FuseParticles", mainTexture = SoftDot(32) };

            smoke = Make("Smoke", mat, layer, 14);
            var m = smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2, 5);
            m.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4);
            m.startColor = Palette.Rgba(70, 60, 55, .4f);
            m.gravityModifier = -0.35f;   // drifts up the sheet (up the screen)
            var e = smoke.emission; e.rateOverTime = 8;
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
            var col = smoke.colorOverLifetime; col.enabled = true;
            col.color = Fade(Palette.Rgba(90, 80, 72, 1), Palette.Rgba(150, 140, 130, 1));
            var size = smoke.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 2.6f));

            sparks = Make("Sparks", mat, layer, 15);
            m = sparks.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(10, 28);
            m.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1, .95f, .6f), new Color(1, .62f, .2f));
            m.gravityModifier = 6;   // they fall back toward the launch end
            e = sparks.emission; e.rateOverTime = 40;
            sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f;
            col = sparks.colorOverLifetime; col.enabled = true;
            col.color = Fade(new Color(1, .9f, .5f), new Color(.9f, .25f, .05f));
            size = sparks.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.2f));
        }

        /// <summary>Puts the tip at <paramref name="pos"/> (sheet units); urgency 0..1 throws more sparks as time runs out.</summary>
        public void Burn(Vector3 pos, bool burning, float urgency)
        {
            if (burning)
            {
                root.localPosition = pos;
                var e = sparks.emission; e.rateOverTime = 35 + 75 * urgency * urgency;
                if (!on) { sparks.Play(); smoke.Play(); on = true; }
            }
            else if (on)
            {
                sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                on = false;
            }
        }

        ParticleSystem Make(string name, Material mat, int layer, int order)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 200;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingOrder = order;
            return ps;
        }

        static ParticleSystem.MinMaxGradient Fade(Color a, Color b)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.8f, .5f), new GradientAlphaKey(0, 1) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        static Texture2D SoftDot(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + .5f) / n * 2 - 1, dy = (y + .5f) / n * 2 - 1;
                    float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * n + x] = new Color(1, 1, 1, a * a * (3 - 2 * a));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
