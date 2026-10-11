using UnityEngine;
using WormSim;

namespace Burrow
{
    // Shared drawing for a worm: a translucent tube skin along a midline, and its neurons as
    // soft glowing points placed inside that body by their anatomical position (axial along the
    // animal, lateral across it), coloured by live activity. Used in the field and in bubbles.
    public class TubeSkin
    {
        public readonly Mesh Mesh;
        readonly int _points, _sides;
        readonly Vector3[] _v; readonly Vector3[] _n; readonly Color[] _c; readonly int[] _t;
        bool _indexed;

        public TubeSkin(int points, int sides)
        {
            _points = points; _sides = sides;
            _v = new Vector3[points * sides]; _n = new Vector3[points * sides]; _c = new Color[points * sides];
            _t = new int[(points - 1) * sides * 6];
            int k = 0;
            for (int i = 0; i < points - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * sides + s, b = i * sides + (s + 1) % sides, c = (i + 1) * sides + s, d = (i + 1) * sides + (s + 1) % sides;
                    _t[k++] = a; _t[k++] = c; _t[k++] = b; _t[k++] = b; _t[k++] = c; _t[k++] = d;
                }
            Mesh = new Mesh { name = "tube" }; Mesh.MarkDynamic();
        }

        // Fills the mesh from `mid` (count points). Radius tapers: blunt head, thin tail.
        public void Update(Vector3[] mid, float radius, Color tint, float alpha)
        {
            for (int i = 0; i < _points; i++)
            {
                var p = mid[i];
                var tdir = (i < _points - 1 ? mid[i + 1] - p : p - mid[i - 1]);
                if (tdir.sqrMagnitude < 1e-10f) tdir = Vector3.right; tdir.Normalize();
                var n = Vector3.Cross(tdir, Vector3.forward); if (n.sqrMagnitude < 1e-6f) n = Vector3.up; n.Normalize();
                var bn = Vector3.Cross(tdir, n);
                float taper = i < 6 ? Mathf.Lerp(0.6f, 1f, i / 6f) : Mathf.Lerp(1f, 0.3f, Mathf.Pow((i - 6f) / (_points - 7f), 1.3f));
                float r = radius * taper;
                for (int s = 0; s < _sides; s++)
                {
                    float a = s / (float)_sides * 6.2831853f;
                    var off = n * Mathf.Cos(a) + bn * Mathf.Sin(a);
                    _v[i * _sides + s] = p + off * r; _n[i * _sides + s] = off;
                    var c = tint; c.a = alpha; _c[i * _sides + s] = c;
                }
            }
            Mesh.vertices = _v; Mesh.normals = _n; Mesh.colors = _c;
            if (!_indexed) { Mesh.triangles = _t; _indexed = true; }
            Mesh.RecalculateBounds();
        }

        // Frame at point i: tangent and the two normals, for placing neurons inside the body.
        public static void Frame(Vector3[] mid, int count, int i, out Vector3 t, out Vector3 n, out Vector3 bn)
        {
            var p = mid[i];
            t = (i < count - 1 ? mid[i + 1] - p : p - mid[i - 1]);
            if (t.sqrMagnitude < 1e-10f) t = Vector3.right; t.Normalize();
            n = Vector3.Cross(t, Vector3.forward); if (n.sqrMagnitude < 1e-6f) n = Vector3.up; n.Normalize();
            bn = Vector3.Cross(t, n);
        }
    }

    public class NeuronGlow
    {
        public readonly Mesh Mesh;
        readonly int _n;
        readonly Vector3[] _v; readonly Color[] _c; readonly Vector2[] _uv;
        readonly float[] _axial, _lateral, _ring;     // anatomy: along, across, and a fixed angle round the body
        readonly bool[] _muscle;
        readonly float[] _grow;                       // per-neuron size: class and firing, in x-ray colours

        public NeuronGlow(WmAsset a, int seed)
        {
            _n = a.NNeurons;
            _v = new Vector3[_n * 4]; _c = new Color[_n * 4]; _uv = new Vector2[_n * 4];
            _axial = new float[_n]; _lateral = new float[_n]; _ring = new float[_n]; _muscle = new bool[_n]; _grow = new float[_n];
            var tris = new int[_n * 6];
            var rng = new System.Random(seed);
            for (int i = 0; i < _n; i++)
            {
                if (a.NeuronAxial != null && a.NeuronAxial[i] >= 0f) { _axial[i] = a.NeuronAxial[i]; _lateral[i] = a.NeuronLateral[i]; }
                else { _axial[i] = (float)rng.NextDouble(); _lateral[i] = (float)(rng.NextDouble() * 2 - 1); }
                _ring[i] = (float)(rng.NextDouble() * 6.2831853);
                _muscle[i] = a.IsMuscle[i] != 0; _grow[i] = 1f;
                tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 2; tris[i * 6 + 2] = i * 4 + 1;
                tris[i * 6 + 3] = i * 4; tris[i * 6 + 4] = i * 4 + 3; tris[i * 6 + 5] = i * 4 + 2;
                _uv[i * 4] = new Vector2(0, 0); _uv[i * 4 + 1] = new Vector2(1, 0); _uv[i * 4 + 2] = new Vector2(1, 1); _uv[i * 4 + 3] = new Vector2(0, 1);
            }
            Mesh = new Mesh { name = "neurons", vertices = _v, uv = _uv, triangles = tris, colors = _c };
            Mesh.MarkDynamic();
        }

        // Places every neuron inside the body: along the midline by axial position, across it by
        // lateral, and round the tube on its own angle, so the points fill the thickness.
        public void Place(Vector3[] mid, int count, float bodyRadius, float spriteSize, Vector3 toViewer)
        {
            var right = Vector3.Cross(Vector3.up, toViewer).normalized; var up = Vector3.Cross(toViewer, right).normalized;
            for (int i = 0; i < _n; i++)
            {
                float fi = _axial[i] * (count - 1); int i0 = Mathf.Clamp((int)fi, 0, count - 2); float f = fi - i0;
                var p = Vector3.Lerp(mid[i0], mid[i0 + 1], f);
                TubeSkin.Frame(mid, count, i0, out _, out var n, out var bn);
                float rr = bodyRadius * 0.7f * Mathf.Abs(_lateral[i]) + bodyRadius * 0.15f;
                p += (n * Mathf.Cos(_ring[i]) + bn * Mathf.Sin(_ring[i])) * rr * 0.5f + n * _lateral[i] * bodyRadius * 0.55f;
                float s = spriteSize * (_muscle[i] ? 0.6f : 1f) * _grow[i];
                _v[i * 4] = p - right * s - up * s; _v[i * 4 + 1] = p + right * s - up * s;
                _v[i * 4 + 2] = p + right * s + up * s; _v[i * 4 + 3] = p - right * s + up * s;
            }
            Mesh.vertices = _v; Mesh.RecalculateBounds();
        }

        // With `classes` the neurons take the viewer's x-ray colours (chemosensory cyan, sensory
        // blue, motor orange, interneurons pale blue) and swell as they charge; without, the
        // worm's own tint, which is what tells the bubbles apart.
        public void Recolor(WmBrain brain, Color tint, NeuronClass[] classes = null)
        {
            var dim = new Color(0.25f, 0.35f, 0.6f, 0.35f);
            for (int i = 0; i < _n; i++)
            {
                float act = Mathf.Clamp01((float)(brain.Activity(i) / WmConst.FireThreshold));
                Color c;
                if (classes != null)
                {
                    NeuronClasses.Look(classes[i], out float hue, out float rest, out float size);
                    c = Color.Lerp(Overlays.Hsl(hue, 0.85f, 0.6f, rest), Overlays.Hsl(hue, 1f, 0.6f, 1f), act);
                    _grow[i] = size * (1f + 0.8f * act);
                }
                else
                {
                    c = Color.Lerp(dim, tint, act); c.a = 0.4f + 0.6f * act;
                    if (_muscle[i]) c *= 0.6f;
                }
                _c[i * 4] = _c[i * 4 + 1] = _c[i * 4 + 2] = _c[i * 4 + 3] = c;
            }
            Mesh.colors = _c;
        }
    }
}
