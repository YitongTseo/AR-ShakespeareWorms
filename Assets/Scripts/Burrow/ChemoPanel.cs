using System.Collections.Generic;
using UnityEngine;
using WormSim;

namespace Burrow
{
    // The chemosensory state of one worm, on a card beside its bubble (the viewer's chemo
    // panel): a row per chemosensory pair, a bar for its left and its right neuron, each bar
    // stacked from the words currently feeding that neuron. A full bar is a saturated neuron.
    // Words keep one colour everywhere (the same as their smell line) and a fixed alphabetical
    // order, so the segments hold still while the worm moves.
    public class ChemoPanel : MonoBehaviour
    {
        const float PadX = 0.005f, Pad = 0.005f, LabelW = 0.026f, Gap = 0.003f, BarW = 0.040f;
        const float RowH = 0.0065f, BarH = 0.005f, HeadH = 0.009f, ColH = 0.005f;
        public const float W = PadX * 2 + LabelW + (Gap + BarW) * 2;
        public const float H = Pad * 2 + HeadH + ColH + RowH * WmConst.NPc;
        const float InlineChar = 0.0009f, InlineCharW = 0.0018f;       // TextMesh size / rough advance of a segment's word
        const int MaxInline = 40;

        MindBubble _bubble; TextWorld _w;
        Transform _root;
        Mesh _mesh;
        readonly List<Vector3> _v = new List<Vector3>(); readonly List<Color> _c = new List<Color>();
        readonly List<Vector2> _uv = new List<Vector2>(); readonly List<int> _t = new List<int>();
        readonly List<TextMesh> _inline = new List<TextMesh>();
        readonly string[] _key = new string[WormSenses.Max]; readonly int[] _order = new int[WormSenses.Max];
        readonly float[] _hue = new float[WormSenses.Max];

        public static ChemoPanel Create(MindBubble bubble, TextWorld w)
        {
            var go = new GameObject("chemo"); go.transform.SetParent(bubble.transform, false);
            var p = go.AddComponent<ChemoPanel>();
            p._bubble = bubble; p._w = w;
            p.Build();
            return p;
        }

        void Build()
        {
            _root = new GameObject("card").transform; _root.SetParent(transform, false);
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(back.GetComponent<Collider>());
            back.SetParent(_root, false); back.localScale = new Vector3(W, H, 1f); back.localPosition = new Vector3(0f, 0f, 0.001f);
            back.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Burrow/Panel")) { color = new Color(0f, 0.01f, 0.02f, 0.72f) };

            // bars: solid fills, blended (not additive) and drawn before the text that sits on them
            _mesh = new Mesh { name = "chemo bars" }; _mesh.MarkDynamic();
            var bars = new GameObject("bars"); bars.transform.SetParent(_root, false);
            bars.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mat = new Material(Shader.Find("Burrow/Dash")) { renderQueue = 2995 };
            mat.SetFloat("_Period", 0f); mat.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            bars.AddComponent<MeshRenderer>().sharedMaterial = mat;

            float left = -W * 0.5f + PadX, top = H * 0.5f - Pad;
            var a = _bubble.Agent.World.Asset;
            Text($"chemosensory · {_bubble.Dome.Mind.name}", new Vector3(left, top - HeadH * 0.45f, 0f), 0.0012f, TextAnchor.MiddleLeft, new Color(0.55f, 1f, 0.55f, 1f));
            var faint = new Color(1f, 1f, 1f, 0.45f);
            Text("L", new Vector3(BarX(0) + BarW * 0.5f, top - HeadH - ColH * 0.5f, 0f), 0.0009f, TextAnchor.MiddleCenter, faint);
            Text("R", new Vector3(BarX(1) + BarW * 0.5f, top - HeadH - ColH * 0.5f, 0f), 0.0009f, TextAnchor.MiddleCenter, faint);
            for (int i = 0; i < WmConst.NPc; i++)
            {
                string l = a.Neurons[a.ChemoPairL[i]];
                Text($"PC{i} · {l.Substring(0, l.Length - 1)}", new Vector3(left, RowY(i), 0f), 0.00095f, TextAnchor.MiddleLeft, Overlays.Hsl(Overlays.PairHue(i), 0.55f, 0.72f, 0.9f));
            }
            _root.gameObject.SetActive(false);
        }

        static float BarX(int side) => -W * 0.5f + PadX + LabelW + Gap + side * (BarW + Gap);
        static float RowY(int i) => H * 0.5f - Pad - HeadH - ColH - (i + 0.5f) * RowH;

        TextMesh Text(string s, Vector3 at, float size, TextAnchor anchor, Color color)
        {
            var t = new GameObject("t").AddComponent<TextMesh>();
            t.transform.SetParent(_root, false); t.transform.localPosition = at + new Vector3(0f, 0f, -0.0005f);
            t.font = BurrowText.Font; t.fontSize = 40; t.characterSize = size; t.anchor = anchor; t.color = color; t.text = s;
            t.GetComponent<MeshRenderer>().sharedMaterial = BurrowText.Material;
            return t;
        }

        void Quad(float x, float y, float w, float h, Color c)
        {
            int b = _v.Count;
            _v.Add(new Vector3(x, y - h * 0.5f, 0f)); _v.Add(new Vector3(x + w, y - h * 0.5f, 0f));
            _v.Add(new Vector3(x + w, y + h * 0.5f, 0f)); _v.Add(new Vector3(x, y + h * 0.5f, 0f));
            for (int i = 0; i < 4; i++) { _c.Add(c); _uv.Add(new Vector2(0f, 0.5f)); }
            _t.Add(b); _t.Add(b + 2); _t.Add(b + 1); _t.Add(b); _t.Add(b + 3); _t.Add(b + 2);
        }

        void Rebuild(WormSenses s, WmAsset a)
        {
            _v.Clear(); _c.Clear(); _uv.Clear(); _t.Clear();
            int n = s.Count, used = 0;
            for (int k = 0; k < n; k++)
            {
                string text = a.TokText[s.Word(k).Tok];
                _key[k] = Overlays.WordKey(text); _order[k] = k; _hue[k] = Overlays.WordHue(text);
            }
            System.Array.Sort(_key, _order, 0, n, System.StringComparer.Ordinal);
            for (int i = 0; i < WmConst.NPc; i++)
                for (int side = 0; side < 2; side++)
                {
                    float x = BarX(side), y = RowY(i), total = 0f;
                    Quad(x, y, BarW, BarH, new Color(1f, 1f, 1f, 0.015f));
                    for (int k = 0; k < n; k++) total += s.Value(k, i, side == 0);
                    if (total <= 0f) continue;
                    float fill = Mathf.Min(1f, total) * BarW;
                    for (int j = 0; j < n; j++)
                    {
                        int k = _order[j];
                        float seg = s.Value(k, i, side == 0) / total * fill;
                        if (seg <= 0f) continue;
                        Quad(x, y, seg, BarH, Overlays.Hsl(_hue[k], 0.75f, 0.55f, 0.95f));
                        string text = a.TokText[s.Word(k).Tok];
                        if (used < MaxInline && seg > text.Length * InlineCharW + 0.002f)
                        {
                            if (used == _inline.Count) _inline.Add(Text("", Vector3.zero, InlineChar, TextAnchor.MiddleLeft, new Color(0f, 0f, 0f, 0.75f)));
                            var t = _inline[used++];
                            t.gameObject.SetActive(true);
                            if (t.text != text) t.text = text;
                            t.transform.localPosition = new Vector3(x + 0.001f, y, -0.0005f);
                        }
                        x += seg;
                    }
                }
            for (int i = used; i < _inline.Count; i++) _inline[i].gameObject.SetActive(false);
            _mesh.Clear();
            _mesh.SetVertices(_v); _mesh.SetColors(_c); _mesh.SetUVs(0, _uv); _mesh.SetTriangles(_t, 0);
        }

        void LateUpdate()
        {
            var agent = _bubble.Agent;
            bool show = Overlays.Chemo && agent != null;
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;
            Rebuild(agent.Senses, agent.World.Asset);

            // faces the viewer, on the side of the bubble away from the text world: outward for the
            // bubbles beside it, above the name for the ones along its top (or carried elsewhere)
            var cam = Camera.main; if (cam == null) return;
            var at = _bubble.transform.position;
            _root.rotation = Quaternion.LookRotation(at - cam.transform.position, Vector3.up);
            var inBox = _w.transform.InverseTransformPoint(at);
            float r = _bubble.Radius * _bubble.transform.lossyScale.x, k = _bubble.transform.lossyScale.x;
            if (Mathf.Abs(inBox.x) > (float)WmConst.WorldW * _w.Scale * 0.45f)
            {
                float side = Mathf.Sign(Vector3.Dot(at - _w.transform.position, _root.right));
                _root.position = at + _root.right * side * (r + (W * 0.5f + 0.012f) * k);
            }
            else _root.position = at + Vector3.up * (r + (0.035f + 0.045f + H * 0.5f) * k);
        }
    }
}
