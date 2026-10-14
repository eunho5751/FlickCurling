using System.Collections.Generic;
using UnityEngine;

namespace FlickCurling.Game
{
    /// <summary>Immediate-mode vertex-colour geometry in world units, rebuilt every frame (lines, rings, arcs, discs).</summary>
    public sealed class ShapeBatch
    {
        readonly Mesh mesh;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color32> cols = new List<Color32>();
        readonly List<int> tris = new List<int>();
        public readonly GameObject Go;

        public ShapeBatch(string name, Material mat, int sortingOrder, int layer)
        {
            Go = new GameObject(name) { layer = layer };
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            Go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = Go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingOrder = sortingOrder;
        }

        public void Clear() { verts.Clear(); cols.Clear(); tris.Clear(); }

        public void Commit()
        {
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 10));
        }

        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        public void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            Color32 c32 = col; cols.Add(c32); cols.Add(c32); cols.Add(c32);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        public void Rect(float x0, float y0, float x1, float y1, Color col) =>
            Quad(new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0), col);

        public void Line(Vector2 a, Vector2 b, float w, Color col)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            var n = new Vector2(-d.y, d.x) / len * (w / 2);
            Quad(a - n, a + n, b + n, b - n, col);
        }

        public void Dashed(Vector2 a, Vector2 b, float w, float dash, float gap, Color col)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            var u = d / len;
            for (float t = 0; t < len; t += dash + gap) Line(a + u * t, a + u * Mathf.Min(len, t + dash), w, col);
        }

        /// <summary>Stroke along a circle from angle a0 to a1 (radians, world, counter-clockwise positive).</summary>
        public void Arc(Vector2 c, float r, float w, float a0, float a1, Color col)
        {
            float span = a1 - a0;
            int n = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(span) / (2 * Mathf.PI) * 64));
            float ri = Mathf.Max(0, r - w / 2), ro = r + w / 2;
            Color32 c32 = col;
            int start = verts.Count;
            for (int i = 0; i <= n; i++)
            {
                float a = a0 + span * i / n;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                verts.Add(c + dir * ri); verts.Add(c + dir * ro);
                cols.Add(c32); cols.Add(c32);
            }
            for (int i = 0; i < n; i++)
            {
                int k = start + i * 2;
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 3); tris.Add(k); tris.Add(k + 3); tris.Add(k + 2);
            }
        }

        public void Ring(Vector2 c, float r, float w, Color col) => Arc(c, r, w, 0, 2 * Mathf.PI, col);

        public void Disc(Vector2 c, float r, Color col)
        {
            int n = 40, start = verts.Count;
            Color32 c32 = col;
            verts.Add(c); cols.Add(c32);
            for (int i = 0; i <= n; i++)
            {
                float a = 2 * Mathf.PI * i / n;
                verts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); cols.Add(c32);
            }
            for (int i = 0; i < n; i++) { tris.Add(start); tris.Add(start + i + 2); tris.Add(start + i + 1); }
        }

        public void RectStroke(float x0, float y0, float x1, float y1, float w, Color col)
        {
            Rect(x0 - w / 2, y0 - w / 2, x1 + w / 2, y0 + w / 2, col);
            Rect(x0 - w / 2, y1 - w / 2, x1 + w / 2, y1 + w / 2, col);
            Rect(x0 - w / 2, y0 + w / 2, x0 + w / 2, y1 - w / 2, col);
            Rect(x1 - w / 2, y0 + w / 2, x1 + w / 2, y1 - w / 2, col);
        }
    }
}
