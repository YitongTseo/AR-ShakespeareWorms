using System.Collections.Generic;
using UnityEngine;

namespace Burrow
{
    // Drives the tunnel shader's per-run table: breathing phase and the travelling
    // "swallowed prey" boluses. A bolus is born at an old mind, crawls downstream
    // run by run along the lineage toward the sculpture, and is absorbed there.
    // Runs are baked upstream -> downstream (a -> b), so a bolus always moves in +s.
    public class BurrowMotion : MonoBehaviour
    {
        [Header("Boluses")]
        public float speed = 0.22f;            // m/s along the tunnel
        public float halfWidth = 0.11f;        // m, gaussian half width
        public float amplitude = 0.75f;        // fraction of tunnel radius
        public float spawnEvery = 2.2f;        // s, mean interval
        public int maxAlive = 18;

        const int MaxRuns = 512;
        readonly Vector4[] _table = new Vector4[MaxRuns];
        static readonly int RunsId = Shader.PropertyToID("_Runs");
        static readonly int RunInfoId = Shader.PropertyToID("_RunInfo");
        readonly Vector4[] _info = new Vector4[MaxRuns];

        class Bolus { public int run; public float s; public float age; public float life; }
        readonly List<Bolus> _alive = new List<Bolus>();
        List<int>[] _downstream;     // node -> runs leaving it (a == node)
        List<int> _sources = new List<int>();
        BurrowLoader _loader;
        float _nextSpawn;
        System.Random _rng = new System.Random(3);

        void Start()
        {
            _loader = BurrowLoader.Instance;
            _loader.Loaded += OnLoaded;
        }

        void OnLoaded()
        {
            var m = _loader.Manifest;
            _downstream = new List<int>[m.nodes.Length];
            for (int i = 0; i < m.nodes.Length; i++) _downstream[i] = new List<int>();
            var hasIncoming = new bool[m.nodes.Length];
            foreach (var r in m.runs)
            {
                if (r.kind == "spur") continue;          // dead ends carry nothing
                _downstream[r.a].Add(r.id);
                hasIncoming[r.b] = true;
            }
            foreach (var n in m.nodes)
                if (n.kind == "mind" && !hasIncoming[n.id]) _sources.Add(n.id);
            if (_sources.Count == 0) foreach (var n in m.nodes) if (n.kind == "mind") _sources.Add(n.id);
            for (int i = 0; i < MaxRuns; i++)
            {
                _table[i] = new Vector4(-10f, 0f, halfWidth, (float)(_rng.NextDouble() * 6.2831853));
                var dir = new Vector3((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f).normalized;
                float len = i < m.runs.Length ? m.runs[i].len : 1f;
                _info[i] = new Vector4(len, dir.x, dir.y, dir.z);
            }
            _loader.TunnelMaterial.SetVectorArray(RunsId, _table);
            _loader.TunnelMaterial.SetVectorArray(RunInfoId, _info);
            _nextSpawn = Time.time + 2f;
        }

        int PickDownstream(int node)
        {
            var list = _downstream[node];
            if (list.Count == 0) return -1;
            // weight by flow so the bolus follows the trunk more often than the twigs
            float tot = 0f; foreach (var id in list) tot += _loader.Manifest.runs[id].flow + 0.5f;
            float x = (float)_rng.NextDouble() * tot;
            foreach (var id in list) { x -= _loader.Manifest.runs[id].flow + 0.5f; if (x <= 0) return id; }
            return list[list.Count - 1];
        }

        bool RunBusy(int run) { foreach (var b in _alive) if (b.run == run) return true; return false; }

        void Update()
        {
            if (!_loader.IsLoaded) return;
            var runs = _loader.Manifest.runs;
            float dt = Time.deltaTime, t = Time.time;

            if (t >= _nextSpawn && _alive.Count < maxAlive)
            {
                int node = _sources[_rng.Next(_sources.Count)];
                int run = PickDownstream(node);
                if (run >= 0 && !RunBusy(run))
                    _alive.Add(new Bolus { run = run, s = -halfWidth * 2f, age = 0f, life = 0f });
                _nextSpawn = t + spawnEvery * (0.6f + 0.8f * (float)_rng.NextDouble());
            }

            // clear previous positions, then write the live ones
            for (int i = 0; i < runs.Length; i++) { _table[i].x = -10f; _table[i].y = 0f; }
            for (int k = _alive.Count - 1; k >= 0; k--)
            {
                var b = _alive[k];
                b.age += dt;
                // slow, slightly uneven crawl: peristalsis rather than a conveyor
                float v = speed * (0.75f + 0.25f * Mathf.Sin(b.age * 1.7f + b.run));
                b.s += v * dt;
                var r = runs[b.run];
                if (b.s > r.len + halfWidth * 2f)
                {
                    int next = PickDownstream(r.b);
                    if (next < 0 || RunBusy(next)) { _alive.RemoveAt(k); continue; }   // absorbed at the sculpture (or blocked)
                    b.run = next; b.s = -halfWidth * 2f;
                    continue;
                }
                float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(b.age / 2.5f));
                _table[b.run].x = b.s;
                _table[b.run].y = amplitude * fadeIn;
                _table[b.run].z = halfWidth;
            }
            _loader.TunnelMaterial.SetVectorArray(RunsId, _table);
        }
    }
}
