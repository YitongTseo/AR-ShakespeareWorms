using System.Collections;
using UnityEngine;
using WormSim;

namespace Burrow
{
    // A mind's bubble beside the text world: translucent, with the worm's own body drawn inside
    // as an x-ray (its 300 neurons lit along the midline) so you can tell which worm is which,
    // plus its name, generation and score. Summoned from a dome with a double flash and a slow
    // flight; tapped, it flashes and flies home.
    public class MindBubble : MonoBehaviour
    {
        public DomeView Dome { get; private set; }
        public WormAgent Agent { get; private set; }
        public int Slot { get; private set; }
        public bool Busy { get; private set; }     // flying
        public float Radius = 0.085f;

        TextWorld _w;
        Transform _shell, _inner;
        TubeSkin _skin; NeuronGlow _glow;
        TextMesh _label;
        MeshRenderer _shellMr;
        readonly Vector3[] _mid = new Vector3[100];
        readonly Vector3[] _fit = new Vector3[100];
        float _hover, _heat;
        public float Heat => _heat;

        public static MindBubble Summon(TextWorld w, DomeView dome, byte[] mindBytes)
        {
            var go = new GameObject($"bubble_{dome.Mind.name}");
            var b = go.AddComponent<MindBubble>();
            b._w = w; b.Dome = dome; b.Slot = w.FreeSlot();
            w.Bubbles.Add(b);
            b.Build();
            b.transform.position = dome.WorldCenter;
            b.transform.localScale = Vector3.one * 0.2f;
            dome.SetAway(true);
            b.Agent = w.Admit(dome.Mind, mindBytes, dome.WorldCenter);
            b.Agent.Bubble = b;
            b.Agent.BrainTicked += b.Recolor;
            ChemoPanel.Create(b, w);
            b.StartCoroutine(b.FlyTo(w.SlotPosition(b.Slot), 2.6f, null));
            return b;
        }

        void Build()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _shell = sphere.transform; _shell.SetParent(transform, false); _shell.localScale = Vector3.one * (Radius * 2f);
            Destroy(sphere.GetComponent<Collider>());
            _shellMr = sphere.GetComponent<MeshRenderer>();
            _shellMr.sharedMaterial = new Material(Shader.Find("Burrow/Dome")) { color = new Color(0.8f, 0.9f, 1f, 1f) };
            var col = gameObject.AddComponent<SphereCollider>(); col.radius = Radius;

            _inner = new GameObject("xray").transform; _inner.SetParent(transform, false);
            _skin = new TubeSkin(100, 10);
            var bgo = new GameObject("body"); bgo.transform.SetParent(_inner, false);
            bgo.AddComponent<MeshFilter>().sharedMesh = _skin.Mesh;
            bgo.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Burrow/Translucent"));

            _label = new GameObject("label").AddComponent<TextMesh>();
            _label.transform.SetParent(transform, false);
            _label.transform.localPosition = new Vector3(0f, Radius + 0.035f, 0f);
            _label.font = BurrowText.Font; _label.fontSize = 40; _label.characterSize = 0.004f;
            _label.anchor = TextAnchor.LowerCenter; _label.alignment = TextAlignment.Center;
            _label.color = new Color(1f, 0.96f, 0.9f, 1f);
            _label.GetComponent<MeshRenderer>().sharedMaterial = BurrowText.Material;
            var m = Dome.Mind;
            _label.text = $"{m.name}\ngen {m.gen} · {m.fitness:F0}";
        }

        void BuildNeurons()
        {
            _glow = new NeuronGlow(Agent.World.Asset, Dome.Mind.id + 7);
            var go = new GameObject("neurons"); go.transform.SetParent(_inner, false);
            go.AddComponent<MeshFilter>().sharedMesh = _glow.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Burrow/Glow"));
            Recolor();
        }

        // Pointing dwell: the shell warms from cool glass toward amber; at 1 it is sent home.
        public void SetHeat(float h)
        {
            _heat = Mathf.Clamp01(h);
            if (_shellMr != null && !Busy)
                _shellMr.material.color = Color.Lerp(new Color(0.8f, 0.9f, 1f, 1f), new Color(1f, 0.55f, 0.15f, 1f) * (1f + _heat), _heat);
        }

        Vector3 _launch;     // direction of the flick that sent it home, bends the flight path

        IEnumerator FlyTo(Vector3 target, float seconds, System.Action done)
        {
            Busy = true;
            var from = transform.position; float s0 = transform.localScale.x;
            var mid = (from + target) * 0.5f + Vector3.up * 0.25f + Vector3.Cross(target - from, Vector3.up).normalized * 0.12f + _launch * 0.5f;
            _launch = Vector3.zero;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / seconds;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                var p = (1 - e) * (1 - e) * from + 2 * (1 - e) * e * mid + e * e * target;      // quadratic bezier
                transform.position = p;
                transform.localScale = Vector3.one * Mathf.Lerp(s0, 1f, e);
                yield return null;
            }
            transform.position = target; transform.localScale = Vector3.one;
            Busy = false;
            done?.Invoke();
        }

        public void Return() { Return(Vector3.zero); }

        // Sends the mind home. A non-zero `launch` (a flick) throws it that way first.
        public void Return(Vector3 launch)
        {
            if (Busy) return;
            _launch = launch;
            StartCoroutine(Flash(() =>
            {
                _w.Bubbles.Remove(this);
                if (Agent != null) { Agent.BrainTicked -= Recolor; _w.Dismiss(Agent); Agent = null; }
                StartCoroutine(FlyTo(Dome.WorldCenter, 2.4f, () => { Dome.SetAway(false); Destroy(gameObject); }));
            }));
        }

        IEnumerator Flash(System.Action done)
        {
            Busy = true;
            var mat = _shellMr.material;
            for (int k = 0; k < 2; k++)
            {
                float t = 0f;
                while (t < 1f) { t += Time.deltaTime / 0.45f; mat.color = Color.Lerp(new Color(0.8f, 0.9f, 1f), Color.white * 2.5f, Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI)); yield return null; }
            }
            mat.color = new Color(0.8f, 0.9f, 1f, 1f);
            Busy = false;
            done?.Invoke();
        }

        public void Hover(bool on) { _hover = on ? 1f : 0f; }

        // Pinched and carried: follows the hand; the heat shows how close it is to being sent home.
        public bool Grabbed { get; private set; }
        public void BeginGrab() { Grabbed = true; }
        public void EndGrab() { Grabbed = false; SetHeat(0f); }

        void Recolor()
        {
            if (Agent == null || _glow == null) return;
            _glow.Recolor(Agent.World.Brain, Agent.Tint);
        }

        void LateUpdate()
        {
            if (Agent == null) return;
            if (_glow == null) BuildNeurons();
            // the x-ray copies the live body: fit its midline (flattened) inside the bubble
            int n = Agent.Midline(_mid);
            var lo = new Vector3(1e9f, 1e9f, 1e9f); var hi = -lo;
            for (int i = 0; i < n; i++) { lo = Vector3.Min(lo, _mid[i]); hi = Vector3.Max(hi, _mid[i]); }
            var c = (lo + hi) * 0.5f; float ext = Mathf.Max(hi.x - lo.x, hi.y - lo.y, 0.02f);
            float k = Radius * 1.45f / ext;
            // the real shape, depth included: a small 3D animal, not a drawing of one
            for (int i = 0; i < n; i++) { var q = (_mid[i] - c) * k; q.z *= 2.2f; _fit[i] = q; }
            float bodyR = Mathf.Clamp(Agent.BodyRadius * k * 1.1f, 0.004f, 0.012f);
            _skin.Update(_fit, bodyR, Agent.Tint, 0.3f);                       // a ghost of a body: the neurons are the point
            var camNow = Camera.main;
            var toViewer = camNow != null ? _inner.InverseTransformDirection((camNow.transform.position - transform.position).normalized) : Vector3.back;
            _glow.Place(_fit, n, bodyR, Mathf.Clamp(bodyR * 0.7f, 0.003f, 0.0055f), toViewer);
            // the animal keeps the text world's orientation (walk round it); only the label faces you
            _inner.rotation = _w.transform.rotation;
            var cam = Camera.main; if (cam != null)
            {
                var look = transform.position - cam.transform.position;
                _label.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
            if (_hover > 0f) _shell.localScale = Vector3.one * (Radius * 2f * (1f + 0.08f * Mathf.Sin(Time.time * 6f)));
            else _shell.localScale = Vector3.one * (Radius * 2f);
        }
    }
}
