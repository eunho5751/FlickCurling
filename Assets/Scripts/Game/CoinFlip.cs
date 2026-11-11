using System;
using System.Collections.Generic;
using FlickCurling.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// The turn order coin in 3D: a gold coin with a little thickness, the black stone on one face and the white on the
    /// other, tossed by the physics engine onto an unseen floor, spinning up and bouncing to rest, seen from above by a
    /// camera of its own into a texture the HUD shows (<see cref="Hud.CoinToss"/>).
    /// Which side wins is decided before the throw (the match's seed: online, the same on both devices), and physics is
    /// not the same from one device to another, so the toss is worked out first in a physics scene of its own and then
    /// played back, with the winner's stone painted on the face that ended up on top.
    /// </summary>
    public sealed class CoinFlip : MonoBehaviour
    {
        const float Radius = 0.5f, Half = 0.04f;   // the coin: 1 across, 0.08 thick
        const int Segments = 48;
        const float Step = 1 / 120f, MaxTime = 8, RestTime = 0.25f;
        const int Tries = 8;                        // tosses tried for one that lies flat (should the nudges not do it)
        const int Nudges = 3;                       // a coin come to rest on its edge is pushed over, this many times at most
        const float NudgeImpulse = 0.004f;
        public const int TexturePx = 512;
        // far below the sheet, out of the game camera's sight
        static readonly Vector3 Origin = new Vector3(0, -5000, 0);
        static Mesh coinMesh, shadowMesh;

        Scene scene;
        RenderTexture rt;
        Camera cam;
        Transform coin, shadow;
        readonly List<Material> materials = new List<Material>();
        readonly List<(Vector3 p, Quaternion q)> frames = new List<(Vector3, Quaternion)>();
        readonly List<(int frame, float strength)> hits = new List<(int, float)>();
        float t;
        int nextHit;
        bool landed;
        Action<float> onHit;
        Action onLanded;

        /// <summary>How long the toss takes, from the throw to rest (seconds).</summary>
        public float Duration => frames.Count * Step;

        /// <summary>
        /// Tosses the coin into <paramref name="view"/> (its texture set here), landing on <paramref name="up"/>'s stone
        /// (the side as shown). <paramref name="hit"/> at each bounce (its strength 0..1), <paramref name="landed"/> once at
        /// rest. It goes with the view's object.
        /// </summary>
        public static CoinFlip Toss(RawImage view, Team up, int seed, Action<float> hit, Action landed)
        {
            var f = view.gameObject.AddComponent<CoinFlip>();
            f.onHit = hit; f.onLanded = landed;
            f.Build(view, up, seed);
            return f;
        }

        void Build(RawImage view, Team up, int seed)
        {
            scene = SceneManager.CreateScene("CoinToss" + GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));

            // the floor (unseen): only its collider
            var floor = new GameObject("Floor");
            SceneManager.MoveGameObjectToScene(floor, scene);
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(40, 1, 40);
            floor.transform.position = Origin + Vector3.down * 0.5f;

            // the coin: three parts (the top face, the bottom face, the rim), one material each
            var go = new GameObject("Coin");
            SceneManager.MoveGameObjectToScene(go, scene);
            coin = go.transform;
            go.AddComponent<MeshFilter>().sharedMesh = coinMesh ??= BuildCoin();
            var mr = go.AddComponent<MeshRenderer>();
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = coinMesh;
            col.convex = true;
            col.sharedMaterial = new PhysicsMaterial("Coin") { bounciness = 0.38f, dynamicFriction = 0.35f, staticFriction = 0.5f, bounceCombine = PhysicsMaterialCombine.Maximum };
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.01f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxAngularVelocity = 200;

            // worked out first: a toss that ends flat (one face up)
            var r = new System.Random(seed);
            for (int i = 0; i < Tries; i++) if (Simulate(rb, r)) break;
            bool topUp = Vector3.Dot(coin.rotation * Vector3.up, Vector3.up) > 0;
            // moved across so it comes to rest in the middle of the view
            var rest = frames[frames.Count - 1].p;
            for (int i = 0; i < frames.Count; i++) frames[i] = (frames[i].p - new Vector3(rest.x, 0, rest.z), frames[i].q);
            Destroy(rb);
            Destroy(col);

            // the winner's stone on the face that is up at the end
            var top = up; var bottom = Sheet.Other(up);
            if (!topUp) (top, bottom) = (bottom, top);
            var baseMat = Resources.Load<Material>("Coin/Coin");
            Material Mat(Texture tex, Color tint, float shine)
            {
                var m = new Material(baseMat) { mainTexture = tex };
                m.SetColor("_Tint", tint);
                m.SetFloat("_Shine", shine);
                materials.Add(m);
                return m;
            }
            mr.sharedMaterials = new[]
            {
                Mat(CoinArt.Face(top).texture, Color.white, 0.45f),
                Mat(CoinArt.Face(bottom).texture, Color.white, 0.45f),
                Mat(Texture2D.whiteTexture, new Color32(0xe8, 0xae, 0x3c, 0xff), 0.8f),
            };

            // its shadow on the floor, under it wherever it is
            var sh = new GameObject("Shadow");
            SceneManager.MoveGameObjectToScene(sh, scene);
            shadow = sh.transform;
            sh.AddComponent<MeshFilter>().sharedMesh = shadowMesh ??= BuildShadow();
            var shr = sh.AddComponent<MeshRenderer>();
            shr.sharedMaterial = Resources.Load<Material>("Coin/CoinShadow");
            shr.sortingOrder = -1;   // (drawn before the coin)
            mr.sortingOrder = 0;

            // the camera above, a little toward the viewer, looking down at where it comes to rest
            rt = new RenderTexture(TexturePx, TexturePx, 16, RenderTextureFormat.ARGB32) { name = "CoinToss", antiAliasing = 4 };
            var camGo = new GameObject("CoinCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.fieldOfView = 30;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 50;
            cam.targetTexture = rt;
            camGo.transform.position = Origin + new Vector3(0, 7.2f, -3.6f);
            camGo.transform.LookAt(Origin + new Vector3(0, 0.45f, 0));
            view.texture = rt;

            Show(0);
        }

        // one toss from the throw to rest, step by step (each step's pose kept, and the bounces); one that comes to rest
        // standing on its edge is pushed over at its top, one way or the other. True if it ends flat
        bool Simulate(Rigidbody rb, System.Random r)
        {
            float Rand(float a, float b) => a + (b - a) * (float)r.NextDouble();
            frames.Clear(); hits.Clear();
            var start = Origin + new Vector3(0, 0.3f, -0.8f);
            var rot = Quaternion.Euler(0, Rand(0, 360), 0);
            coin.SetPositionAndRotation(start, rot);
            rb.position = start; rb.rotation = rot;
            rb.linearVelocity = new Vector3(Rand(-0.25f, 0.25f), Rand(5.0f, 5.8f), Rand(0.5f, 0.9f));
            // flipped end over end (about the axis across the view), a little wobble
            rb.angularVelocity = new Vector3((r.Next(2) * 2 - 1) * Rand(22, 30), Rand(-2, 2), Rand(-3, 3));
            var ps = scene.GetPhysicsScene();
            float rest = 0, prevVy = 0;
            int nudges = 0;
            for (int i = 0; i < MaxTime / Step; i++)
            {
                ps.Simulate(Step);
                frames.Add((rb.position - Origin, rb.rotation));
                var v = rb.linearVelocity;
                if (prevVy < -0.6f && v.y > 0.05f) hits.Add((i, Mathf.Clamp01(-prevVy / 5)));
                prevVy = v.y;
                rest = v.sqrMagnitude < 0.0025f && rb.angularVelocity.sqrMagnitude < 0.01f ? rest + Step : 0;
                if (!(rest > RestTime || rb.IsSleeping())) continue;
                if (Flat(rb) || nudges == Nudges) break;
                nudges++; rest = 0;
                rb.WakeUp();
                var across = rb.rotation * Vector3.up;   // (on its edge: the faces look sideways)
                across.y = 0;
                rb.AddForceAtPosition(across.normalized * ((r.Next(2) * 2 - 1) * NudgeImpulse), rb.worldCenterOfMass + Vector3.up * Radius * 0.9f, ForceMode.Impulse);
            }
            return Flat(rb);
        }

        static bool Flat(Rigidbody rb) => Mathf.Abs(Vector3.Dot(rb.rotation * Vector3.up, Vector3.up)) > 0.95f;

        void Update()
        {
            if (frames.Count == 0) return;
            t += Time.deltaTime;
            Show(t);
        }

        // the coin (and its shadow) at time s of the toss
        void Show(float s)
        {
            float fi = s / Step;
            int i = Mathf.Clamp((int)fi, 0, frames.Count - 1), j = Mathf.Min(i + 1, frames.Count - 1);
            float a = Mathf.Clamp01(fi - (int)fi);
            var p = Vector3.Lerp(frames[i].p, frames[j].p, a);
            coin.SetPositionAndRotation(Origin + p, Quaternion.Slerp(frames[i].q, frames[j].q, a));
            shadow.position = Origin + new Vector3(p.x, 0.002f, p.z);
            shadow.localScale = Vector3.one * (1 + 0.18f * Mathf.Max(0, p.y));
            for (; nextHit < hits.Count && hits[nextHit].frame <= i; nextHit++) onHit?.Invoke(hits[nextHit].strength);
            if (!landed && i >= frames.Count - 1) { landed = true; onLanded?.Invoke(); }
        }

        void OnDestroy()
        {
            // the camera off its texture first (its scene goes only later, unloaded in the background)
            if (cam) { cam.targetTexture = null; cam.enabled = false; }
            if (rt) { rt.Release(); Destroy(rt); }
            foreach (var m in materials) Destroy(m);
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }

        // ---------------- the meshes ----------------

        // a short cylinder: submesh 0 the top face (+y), 1 the bottom face, 2 the rim; each face's picture upright as
        // seen from its own side
        static Mesh BuildCoin()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var top = new List<int>(); var bottom = new List<int>(); var rim = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                float y = side == 0 ? Half : -Half, sign = side == 0 ? 1 : -1;
                int c = v.Count;
                v.Add(new Vector3(0, y, 0)); n.Add(Vector3.up * sign); uv.Add(new Vector2(.5f, .5f));
                for (int k = 0; k <= Segments; k++)
                {
                    float a = k * Mathf.PI * 2 / Segments, x = Mathf.Cos(a), z = Mathf.Sin(a);
                    v.Add(new Vector3(x * Radius, y, z * Radius)); n.Add(Vector3.up * sign);
                    uv.Add(new Vector2(.5f + sign * x * .49f, .5f + z * .49f));
                }
                var tris = side == 0 ? top : bottom;
                for (int k = 0; k < Segments; k++)
                {
                    if (side == 0) { tris.Add(c); tris.Add(c + k + 2); tris.Add(c + k + 1); }
                    else { tris.Add(c); tris.Add(c + k + 1); tris.Add(c + k + 2); }
                }
            }
            int rimStart = v.Count;
            for (int k = 0; k <= Segments; k++)
            {
                float a = k * Mathf.PI * 2 / Segments, x = Mathf.Cos(a), z = Mathf.Sin(a);
                var nn = new Vector3(x, 0, z);
                v.Add(new Vector3(x * Radius, Half, z * Radius)); n.Add(nn); uv.Add(new Vector2((float)k / Segments, 1));
                v.Add(new Vector3(x * Radius, -Half, z * Radius)); n.Add(nn); uv.Add(new Vector2((float)k / Segments, 0));
            }
            for (int k = 0; k < Segments; k++)
            {
                int a0 = rimStart + k * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                rim.Add(a0); rim.Add(a1); rim.Add(b0);
                rim.Add(b0); rim.Add(a1); rim.Add(b1);
            }
            var m = new Mesh { name = "Coin", subMeshCount = 3 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.SetTriangles(top, 0); m.SetTriangles(bottom, 1); m.SetTriangles(rim, 2);
            m.RecalculateBounds();
            return m;
        }

        // a soft round shadow (vertex colours: dark in the middle, clear at the edge), lying on the floor
        static Mesh BuildShadow()
        {
            const int seg = 32;
            var v = new List<Vector3> { Vector3.zero };
            var c = new List<Color> { new Color(0.08f, 0.04f, 0.01f, 0.4f) };
            var tris = new List<int>();
            for (int k = 0; k <= seg; k++)
            {
                float a = k * Mathf.PI * 2 / seg;
                v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Radius * 1.15f);
                c.Add(new Color(0.08f, 0.04f, 0.01f, 0));
            }
            for (int k = 0; k < seg; k++) { tris.Add(0); tris.Add(k + 2); tris.Add(k + 1); }
            var m = new Mesh { name = "CoinShadow" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
