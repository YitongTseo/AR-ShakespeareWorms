using System.Collections.Generic;
using UnityEngine;
using Speech;
using WormSim;

namespace Burrow
{
    // One mind living in the shared TextWorld: its simulation, its body drawn as a tube that
    // burrows through the volume's depth, and its voice at the body's position.
    public class WormAgent : MonoBehaviour
    {
        public BMind Mind { get; private set; }
        public WmWorld World { get; private set; }
        public Color Tint { get; private set; }
        public WormVoice Voice { get; private set; }
        public MindBubble Bubble;
        public float Z { get; private set; }                     // current depth (presentation only)

        const int Points = 100;
        const int Sides = 10;
        public float BodyRadius => 9.5f * _w.Scale;     // thick enough to hold the neurons
        TextWorld _w;
        float _acc, _seed;
        TubeSkin _skin; NeuronGlow _glow;
        readonly List<WmEaten> _eaten = new List<WmEaten>();
        readonly Vector3[] _mid = new Vector3[Points];
        long _lastBrain = -1;
        public System.Action BrainTicked;
        public Vector3[] MidlineRaw => _mid;

        public static WormAgent Create(TextWorld w, BMind mind, WmWorld world, Vector3 entryWorldPos)
        {
            var go = new GameObject($"worm_{mind.name}_{mind.gen}");
            go.transform.SetParent(w.transform, false);
            var a = go.AddComponent<WormAgent>();
            a._w = w; a.Mind = mind; a.World = world;
            a._seed = mind.id * 13.37f;
            float hue = (mind.pc != null && mind.pc.Length > 2) ? Mathf.Repeat(0.52f + 0.3f * mind.pc[2], 1f) : 0.52f;
            a.Tint = Color.HSVToRGB(hue, 0.5f, 1f);
            a.BuildTube();
            // the voice rides on a child at the head, so the tube mesh (in world-local space) stays put
            var mouth = new GameObject("mouth"); mouth.transform.SetParent(go.transform, false);
            a.Voice = WormVoice.Attach(mouth, mind);
            a.Voice.SetTint(a.Tint);
            a.Voice.Speak(mind.name);
            Debug.Log($"[Worm] {mind.name} gen {mind.gen} awake in the text world ({w.Agents.Count + 1} present)");
            return a;
        }

        void BuildTube()
        {
            _skin = new TubeSkin(Points, Sides);
            gameObject.AddComponent<MeshFilter>().sharedMesh = _skin.Mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Burrow/Translucent"));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _glow = new NeuronGlow(World.Asset, Mind.id);
            var g = new GameObject("neurons"); g.transform.SetParent(transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = _glow.Mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Burrow/Glow"));
            _glow.Recolor(World.Brain, Tint);
        }

        public int Midline(Vector3[] outLocal)   // in TextWorld local space, depth included
        {
            int n = Mathf.Min(outLocal.Length, Points);
            for (int i = 0; i < n; i++) outLocal[i] = _mid[i];
            return n;
        }

        void Update()
        {
            _acc += Mathf.Min(Time.deltaTime, 0.1f);
            int steps = 0;
            while (_acc >= (float)WmConst.BodyDt && steps < 6) { World.Tick(); _acc -= (float)WmConst.BodyDt; steps++; }
            if (steps == 0) return;
            if (World.DrainEaten(_eaten) > 0)
            {
                foreach (var e in _eaten) Voice.Speak(World.TokenText(e.Tok));
                _eaten.Clear();
            }
            if (World.BrainTickCount != _lastBrain) { _lastBrain = World.BrainTickCount; _glow.Recolor(World.Brain, Tint); BrainTicked?.Invoke(); }

            // burrow through the thickness: slow wander in depth, head leads, body trails
            float t = Time.time * 0.07f + _seed;
            Z = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * _w.Depth * 0.9f;
            var b = World.Body;
            for (int i = 0; i < Points; i++)
            {
                int j = Mathf.Min(i * 2, WmConst.NSegments - 1);
                float zi = (Mathf.PerlinNoise(t - i * 0.012f, 0.37f) - 0.5f) * _w.Depth * 0.9f;
                _mid[i] = _w.ToLocal(b.Hx[j], b.Hy[j], zi);
            }
            Voice.transform.localPosition = _mid[0];
            _skin.Update(_mid, BodyRadius, Tint, 0.9f);
            var cam = Camera.main;
            var toViewer = cam != null ? transform.InverseTransformDirection((cam.transform.position - transform.position).normalized) : Vector3.back;
            _glow.Place(_mid, Points, BodyRadius, 2.6f * _w.Scale * 1.6f, toViewer);
        }

    }
}
