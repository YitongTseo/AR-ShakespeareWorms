using System.Collections.Generic;
using UnityEngine;
using Speech;
using WormSim;

namespace Burrow
{
    // One mind living in the shared TextWorld: its simulation, its body drawn as a tube that
    // burrows through the volume's depth, and its voice at the body's position. The Overlays
    // switches add its connectome (x-ray) and the lines to the words it smells (senses).
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
        Material _skinMat; GameObject _neurons, _senseGo;
        NeuronClass[] _classes;
        SenseLines _lines;
        bool _xray;
        public readonly WormSenses Senses = new WormSenses();
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
            mr.sharedMaterial = _skinMat = new Material(Shader.Find("Burrow/Translucent"));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _glow = new NeuronGlow(World.Asset, Mind.id);
            _classes = NeuronClasses.Classify(World.Asset);
            _neurons = Child("neurons", transform, _glow.Mesh, new Material(Shader.Find("Burrow/Glow")));
            _neurons.GetComponent<MeshRenderer>().sharedMaterial.SetFloat("_Core", 0.12f);      // keep the class colours: no white-hot centres
            _lines = new SenseLines();
            _senseGo = new GameObject("senses"); _senseGo.transform.SetParent(transform, false);
            Child("lines", _senseGo.transform, _lines.Lines, new Material(Shader.Find("Burrow/Dash")));
            Child("smelled", _senseGo.transform, _lines.Dots, new Material(Shader.Find("Burrow/Glow")));
            _senseGo.SetActive(false);
            _xray = !Overlays.XRay; ApplyXRay();
        }

        static GameObject Child(string name, Transform parent, Mesh mesh, Material mat)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }

        // X-ray on: the skin thins to a ghost and the connectome shows through. Off: just the animal.
        void ApplyXRay()
        {
            if (_xray == Overlays.XRay) return;
            _xray = Overlays.XRay;
            _neurons.SetActive(_xray);
            _skinMat.SetFloat("_Solid", _xray ? 0.18f : 1f);
            if (_xray) _glow.Recolor(World.Brain, Tint, _classes);
        }

        public int Midline(Vector3[] outLocal)   // in TextWorld local space, depth included
        {
            int n = Mathf.Min(outLocal.Length, Points);
            for (int i = 0; i < n; i++) outLocal[i] = _mid[i];
            return n;
        }

        void Update()
        {
            ApplyXRay();
            _acc += Mathf.Min(Time.deltaTime, 0.1f);
            int steps = 0;
            while (_acc >= (float)WmConst.BodyDt && steps < 6) { World.Tick(); _acc -= (float)WmConst.BodyDt; steps++; }
            if (steps == 0) return;
            if (World.DrainEaten(_eaten) > 0)
            {
                foreach (var e in _eaten) Voice.Speak(World.TokenText(e.Tok));
                _eaten.Clear();
            }
            if (World.BrainTickCount != _lastBrain) { _lastBrain = World.BrainTickCount; if (_xray) _glow.Recolor(World.Brain, Tint, _classes); BrainTicked?.Invoke(); }

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
            if (_xray) _glow.Place(_mid, Points, BodyRadius, 2.6f * _w.Scale * 1.6f, toViewer);

            if (Overlays.Senses || Overlays.Chemo) Senses.Sample(World); else Senses.Count = 0;
            _senseGo.SetActive(Overlays.Senses);
            if (Overlays.Senses && cam != null)
                _lines.Update(Senses, _w, World.Asset, _mid, BodyRadius, transform.InverseTransformPoint(cam.transform.position));
        }

    }
}
