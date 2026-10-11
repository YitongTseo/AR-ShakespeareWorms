using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace Burrow
{
    // Loads the baked burrow (mesh + manifest) from StreamingAssets and builds the
    // network under this transform: one tunnel mesh with the motion shader, plus a
    // glass dome at every mind. Other systems subscribe to Loaded.
    public class BurrowLoader : MonoBehaviour
    {
        public static BurrowLoader Instance { get; private set; }

        public BurrowManifest Manifest { get; private set; }
        public Mesh TunnelMesh { get; private set; }
        public Material TunnelMaterial { get; private set; }
        public Transform Content { get; private set; }
        public readonly List<DomeView> Domes = new List<DomeView>();
        public bool IsLoaded { get; private set; }
        public event Action Loaded;

        string[] _poems;
        public string Poem(int mind) => _poems != null && mind >= 0 && mind < _poems.Length ? _poems[mind] : "";

        void Awake() { Instance = this; }
        void Start() => StartCoroutine(Load());

        static string StreamingPath(string rel) => Path.Combine(Application.streamingAssetsPath, "burrow", rel);

        static IEnumerator ReadBytes(string path, Action<byte[]> done)
        {
            if (path.Contains("://"))
            {
                using var req = UnityWebRequest.Get(path);
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) done(req.downloadHandler.data);
                else { Debug.LogError($"[Burrow] load failed {path}: {req.error}"); done(null); }
            }
            else done(File.Exists(path) ? File.ReadAllBytes(path) : null);
        }

        IEnumerator Load()
        {
            byte[] json = null, mesh = null, poems = null;
            yield return ReadBytes(StreamingPath("burrow.json"), b => json = b);
            yield return ReadBytes(StreamingPath("burrow_mesh.bytes"), b => mesh = b);
            if (json == null || mesh == null) { Debug.LogError("[Burrow] missing bake"); yield break; }

            Manifest = JsonUtility.FromJson<BurrowManifest>(System.Text.Encoding.UTF8.GetString(json));
            Debug.Log($"[Burrow] manifest: {Manifest.nodes.Length} nodes, {Manifest.runs.Length} runs, {Manifest.minds.Length} minds");
            yield return null;

            TunnelMesh = BuildMesh(mesh);
            yield return null;

            Content = new GameObject("BurrowContent").transform;
            Content.SetParent(transform, false);

            var shader = Shader.Find("Burrow/Tunnel");
            if (shader == null) Debug.LogError("[Burrow] shader Burrow/Tunnel missing");
            TunnelMaterial = new Material(shader ?? Shader.Find("Standard"));
            var go = new GameObject("Tunnels");
            go.transform.SetParent(Content, false);
            go.AddComponent<MeshFilter>().sharedMesh = TunnelMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TunnelMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            BuildDomes();
            yield return null;

            // poems are only needed by pop-ups; parse them after everything is on screen
            yield return ReadBytes(StreamingPath("poems.json"), b => poems = b);
            if (poems != null)
                _poems = JsonUtility.FromJson<PoemFile>(System.Text.Encoding.UTF8.GetString(poems)).poems;

            IsLoaded = true;
            Loaded?.Invoke();
        }

        // burrow_mesh.bytes layout: see tools/burrow/bake.py
        public static Mesh BuildMesh(byte[] data)
        {
            using var br = new BinaryReader(new MemoryStream(data));
            var magic = br.ReadBytes(4);
            if (magic[0] != 'B' || magic[1] != 'R' || magic[2] != 'R' || magic[3] != 'W')
                throw new Exception("bad burrow mesh magic");
            int version = br.ReadInt32(), nV = br.ReadInt32(), nI = br.ReadInt32(), nRuns = br.ReadInt32();
            var pos = new Vector3[nV]; var nrm = new Vector3[nV];
            var a0 = new Vector4[nV]; var a1 = new Vector2[nV];
            var idx = new int[nI];
            for (int i = 0; i < nV; i++) pos[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            for (int i = 0; i < nV; i++) nrm[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            for (int i = 0; i < nV; i++) a0[i] = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            for (int i = 0; i < nV; i++) a1[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            for (int i = 0; i < nI; i++) idx[i] = (int)br.ReadUInt32();

            var m = new Mesh { name = "burrow_tunnels", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(pos);
            m.SetNormals(nrm);
            m.SetUVs(0, a0);
            m.SetUVs(1, a1);
            m.SetTriangles(idx, 0, false);
            m.RecalculateBounds();
            var b = m.bounds; b.Expand(0.3f); m.bounds = b;   // vertex displacement headroom
            m.UploadMeshData(true);
            Debug.Log($"[Burrow] mesh v{version}: {nV} verts, {nI / 3} tris, {nRuns} runs");
            return m;
        }

        void BuildDomes()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphereMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sphere);
            var glass = new Material(Shader.Find("Burrow/Dome") ?? Shader.Find("Standard"));
            var inner = new Material(Shader.Find("Burrow/DomeInner") ?? Shader.Find("Standard"));

            var domeRoot = new GameObject("Domes").transform;
            domeRoot.SetParent(Content, false);
            foreach (var n in Manifest.nodes)
            {
                if (n.kind != "mind") continue;
                var mind = Manifest.minds[n.mind];
                var go = new GameObject($"dome_{mind.name}_g{mind.gen}");
                go.transform.SetParent(domeRoot, false);
                go.transform.localPosition = V(n.dome);
                go.transform.localScale = Vector3.one * (n.dome_r * 2f);
                go.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = glass; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                var col = go.AddComponent<SphereCollider>(); col.radius = 0.5f;

                var core = new GameObject("core");
                core.transform.SetParent(go.transform, false);
                core.transform.localPosition = -V(n.face) * 0.18f;
                core.transform.localScale = Vector3.one * 0.68f;
                core.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
                var cr = core.AddComponent<MeshRenderer>();
                cr.sharedMaterial = inner; cr.shadowCastingMode = ShadowCastingMode.Off; cr.receiveShadows = false;

                var view = go.AddComponent<DomeView>();
                view.Init(n, mind, cr);
                Domes.Add(view);
            }
            Debug.Log($"[Burrow] {Domes.Count} domes");
        }

        public static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);
    }
}
