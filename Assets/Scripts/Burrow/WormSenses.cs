using UnityEngine;
using WormSim;

namespace Burrow
{
    // What one worm smells right now: every edible word within FoodSenseRadius of its nose, the
    // 12 chemosensory channels that word drives, and how it splits between the left and right
    // neuron of each pair. The arithmetic is WmWorld.ComputeSmells, re-run for display with its
    // own scratch every frame (the sim only samples on brain ticks, twice a second). It reads
    // the world and never feeds it.
    public class WormSenses
    {
        public const int Max = 48;
        public int Count;
        public readonly WmLine[] Line = new WmLine[Max];
        public readonly int[] WordIdx = new int[Max];
        public readonly float[] DistF = new float[Max], DirL = new float[Max];
        public readonly float[] Pca = new float[Max * WmConst.NPc];
        readonly WmChemo _chemo = new WmChemo();
        readonly double[] _pca = new double[WmConst.NPc];

        public WmWord Word(int k) => Line[k].Words[WordIdx[k]];

        // What word k adds to the left or right neuron of pair `pc` (before the per-neuron cap at 1).
        public float Value(int k, int pc, bool left) => Pca[k * WmConst.NPc + pc] * DistF[k] * (left ? DirL[k] : 1f - DirL[k]);

        public float Strongest(int k, bool left)
        {
            float m = 0f;
            for (int i = 0; i < WmConst.NPc; i++) m = Mathf.Max(m, Pca[k * WmConst.NPc + i]);
            return m * DistF[k] * (left ? DirL[k] : 1f - DirL[k]);
        }

        public void Sample(WmWorld world)
        {
            Count = 0;
            var a = world.Asset; var body = world.Body;
            double wx = body.TargetX, wy = body.TargetY;
            foreach (var line in world.Scroller.Lines)
            {
                if (!line.Edible) continue;
                for (int wi = 0; wi < line.Words.Length; wi++)
                {
                    var word = line.Words[wi];
                    if (word.Eaten) continue;
                    double dx = word.X - wx, dy = word.Y - wy;
                    double d = System.Math.Sqrt(dx * dx + dy * dy);
                    if (d > WmConst.FoodSenseRadius) continue;
                    if (!_chemo.Embed(a, word.Tok, world.Recent, world.NRecent, _pca)) continue;
                    double angleDiff = WmMath.PyMod(System.Math.Atan2(dy, dx) - body.FacingDir + WmConst.Pi, 2.0 * WmConst.Pi) - WmConst.Pi;
                    int k = Count++;
                    Line[k] = line; WordIdx[k] = wi;
                    DistF[k] = (float)(1.0 - d / WmConst.FoodSenseRadius);
                    DirL[k] = (float)(0.5 + 0.5 * System.Math.Sin(angleDiff));
                    for (int i = 0; i < WmConst.NPc; i++) Pca[k * WmConst.NPc + i] = (float)_pca[i];
                    if (Count == Max) return;
                }
            }
        }
    }

    // The senses drawn in the text world: for every smelled word a dashed line to each side of
    // the worm's nose (left and right amphid), as strong as that side smells it, and a soft dot
    // on the word. Lines take the word's own colour; the dashes drift toward the worm.
    public class SenseLines
    {
        public readonly Mesh Lines, Dots;
        readonly Vector3[] _lv = new Vector3[WormSenses.Max * 8], _dv = new Vector3[WormSenses.Max * 4];
        readonly Color[] _lc = new Color[WormSenses.Max * 8], _dc = new Color[WormSenses.Max * 4];
        readonly Vector2[] _luv = new Vector2[WormSenses.Max * 8];

        public SenseLines()
        {
            Lines = new Mesh { name = "smell lines" }; Lines.MarkDynamic();
            Dots = new Mesh { name = "smelled words" }; Dots.MarkDynamic();
            var duv = new Vector2[_dv.Length];
            for (int i = 0; i < WormSenses.Max; i++)
            { duv[i * 4] = new Vector2(0, 0); duv[i * 4 + 1] = new Vector2(1, 0); duv[i * 4 + 2] = new Vector2(1, 1); duv[i * 4 + 3] = new Vector2(0, 1); }
            Lines.vertices = _lv; Lines.colors = _lc; Lines.uv = _luv; Lines.triangles = QuadTris(WormSenses.Max * 2);
            Dots.vertices = _dv; Dots.colors = _dc; Dots.uv = duv; Dots.triangles = QuadTris(WormSenses.Max);
        }

        static int[] QuadTris(int quads)
        {
            var t = new int[quads * 6];
            for (int i = 0; i < quads; i++)
            { t[i * 6] = i * 4; t[i * 6 + 1] = i * 4 + 2; t[i * 6 + 2] = i * 4 + 1; t[i * 6 + 3] = i * 4; t[i * 6 + 4] = i * 4 + 3; t[i * 6 + 5] = i * 4 + 2; }
            return t;
        }

        // Everything in TextWorld local space, like the midline. `cam` is the viewer there.
        public void Update(WormSenses s, TextWorld tw, WmAsset a, Vector3[] mid, float bodyRadius, Vector3 cam)
        {
            var heading = mid[0] - mid[3]; heading.z = 0f;
            heading = heading.sqrMagnitude > 1e-12f ? heading.normalized : Vector3.right;
            var left = new Vector3(-heading.y, heading.x, 0f) * (bodyRadius * 0.5f);
            int nl = 0, nd = 0;
            for (int k = 0; k < s.Count; k++)
            {
                var word = s.Word(k);
                if (word.Eaten || word.Y < 0 || word.Y > WmConst.WorldH) continue;
                float yn = (float)(word.Y / WmConst.WorldH);
                float fade = Mathf.Clamp01(yn / 0.08f) * Mathf.Clamp01((1f - yn) / 0.08f);     // as the word itself fades
                var at = tw.ToLocal(word.X, word.Y, tw.WordDepth(s.Line[k], s.WordIdx[k]));
                float hue = Overlays.WordHue(a.TokText[word.Tok]);
                float strongest = 0f;
                for (int side = 0; side < 2; side++)
                {
                    float v = Mathf.Min(1f, s.Strongest(k, side == 0));
                    strongest = Mathf.Max(strongest, v);
                    if (v < 0.02f) continue;
                    var from = mid[0] + (side == 0 ? left : -left);
                    var dir = at - from; float len = dir.magnitude;
                    var across = Vector3.Cross(dir, cam - (from + at) * 0.5f).normalized * ((1.2f + 2.5f * v) * 0.0008f);
                    var c = Overlays.Hsl(hue, 0.9f, 0.6f, Mathf.Sqrt(v) * fade);     // sqrt: faint smells still read
                    int q = nl++ * 4;
                    _lv[q] = from - across; _lv[q + 1] = from + across; _lv[q + 2] = at + across; _lv[q + 3] = at - across;
                    _luv[q] = new Vector2(0f, 0f); _luv[q + 1] = new Vector2(0f, 1f); _luv[q + 2] = new Vector2(len, 1f); _luv[q + 3] = new Vector2(len, 0f);
                    _lc[q] = _lc[q + 1] = _lc[q + 2] = _lc[q + 3] = c;
                }
                if (strongest < 0.02f) continue;
                var toViewer = (cam - at).normalized;
                var right = Vector3.Cross(Vector3.up, toViewer).normalized; var up = Vector3.Cross(toViewer, right);
                float r = (2f + 3f * strongest) * 0.0022f;
                int d = nd++ * 4;
                _dv[d] = at - right * r - up * r; _dv[d + 1] = at + right * r - up * r; _dv[d + 2] = at + right * r + up * r; _dv[d + 3] = at - right * r + up * r;
                _dc[d] = _dc[d + 1] = _dc[d + 2] = _dc[d + 3] = Overlays.Hsl(hue, 0.9f, 0.6f, Mathf.Sqrt(strongest) * 0.8f * fade);
            }
            for (int i = nl * 4; i < _lv.Length; i++) _lv[i] = Vector3.zero;
            for (int i = nd * 4; i < _dv.Length; i++) _dv[i] = Vector3.zero;
            Lines.vertices = _lv; Lines.colors = _lc; Lines.uv = _luv; Lines.RecalculateBounds();
            Dots.vertices = _dv; Dots.colors = _dc; Dots.RecalculateBounds();
        }
    }
}
