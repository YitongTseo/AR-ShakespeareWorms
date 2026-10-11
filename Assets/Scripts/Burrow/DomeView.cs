using System.Collections;
using UnityEngine;

namespace Burrow
{
    // One mind's glass dome. Selection highlight + activity glow go through a property block.
    public class DomeView : MonoBehaviour
    {
        public BNode Node { get; private set; }
        public BMind Mind { get; private set; }
        public bool Selected { get; private set; }
        public bool Away { get; private set; }      // its mind is off in the text world

        MeshRenderer _core;
        MaterialPropertyBlock _mpb;
        float _glow, _target;
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int SeedId = Shader.PropertyToID("_Seed");

        public void Init(BNode node, BMind mind, MeshRenderer core)
        {
            Node = node; Mind = mind; _core = core;
            _mpb = new MaterialPropertyBlock();
            _mpb.SetFloat(SeedId, (mind.id * 0.618034f) % 1f);
            _core.SetPropertyBlock(_mpb);
        }

        public void SetSelected(bool on) { Selected = on; _target = on ? 1f : 0f; }
        public void Hover(bool on) { if (!Selected) _target = on ? 0.45f : 0f; }
        // Pointing dwell: the core brightens with the heat (0..1) until it flashes and goes.
        public void SetHeat(float h) { if (!Selected) _target = Mathf.Max(0f, h) * 1.2f; }

        // The dome dims to an empty shell while its mind is away, and wakes when it returns.
        public void SetAway(bool away)
        {
            Away = away;
            _target = away ? -0.6f : 0f;
            _core.enabled = !away;
        }

        // Two bright pulses, then `done`: the moment a mind is called out of the network.
        public IEnumerator Flash(int times)
        {
            for (int k = 0; k < times; k++)
            {
                float t = 0f;
                while (t < 1f) { t += Time.deltaTime / 0.4f; _glow = _target = 1.8f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI); Apply(); yield return null; }
            }
            _glow = _target = 0f; Apply();
        }

        void Apply() { _mpb.SetFloat(GlowId, Mathf.Max(_glow, 0f)); _core.SetPropertyBlock(_mpb); }

        void Update()
        {
            if (Mathf.Abs(_glow - _target) < 0.001f) return;
            _glow = Mathf.MoveTowards(_glow, _target, Time.deltaTime * 3f);
            Apply();
        }

        public Vector3 WorldCenter => transform.position;
        public Vector3 WorldFace => transform.parent.TransformDirection(BurrowLoader.V(Node.face));
    }
}
