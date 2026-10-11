using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Speech;
using WormSim;

namespace Burrow
{
    // The one shared Hamlet: a floating volume at the wearer's level where every summoned mind
    // crawls over the same scrolling text. The simulation is 2D (the worms are not aware of
    // depth); the third dimension is pure presentation: each word sits at its own depth and the
    // worms drift through the thickness, so they meet words in 3D as they burrow.
    public class TextWorld : MonoBehaviour
    {
        public static TextWorld Instance { get; private set; }

        public float Scale = 0.00056f;            // metres per world unit: 1600 x 1000 -> 0.9 x 0.56 m
        public float Depth = 0.16f;               // thickness of the pseudo-3D volume
        public float Height = 1.22f;              // the wearer's level: where it first appears
        public float Distance = 1.05f;

        public readonly List<WormAgent> Agents = new List<WormAgent>();
        public readonly List<MindBubble> Bubbles = new List<MindBubble>();
        public bool Ready { get; private set; }
        public Bounds LocalBounds => new Bounds(Vector3.zero, new Vector3((float)WmConst.WorldW * Scale + 0.1f, (float)WmConst.WorldH * Scale + 0.1f, Depth + 0.1f));

        WmAsset _corpus;                          // shared.hwrm + one mind: holds the scroller's tables
        WmScroller _scroller;
        byte[] _shared;
        readonly List<string[]> _spokenHistory = new List<string[]>();      // replayed into late joiners' assets
        readonly List<byte[]> _spokenPos = new List<byte[]>();
        readonly List<double[][]> _spokenTaste = new List<double[][]>();
        readonly Dictionary<WmWord, TextMesh> _words = new Dictionary<WmWord, TextMesh>();
        readonly Stack<TextMesh> _pool = new Stack<TextMesh>();
        readonly List<WmWord> _gone = new List<WmWord>();
        Transform _wordRoot, _visual;
        BoxCollider _grab;
        float _acc;
        bool _placed;

        public byte[] SharedBytes => _shared;
        public WmScroller Scroller => _scroller;

        void Awake() { Instance = this; }

        void Start()
        {
            // everything visible lives under one child that stays off until the world is placed
            // (the object itself must stay active, or the loading coroutine never runs)
            _visual = new GameObject("visual").transform; _visual.SetParent(transform, false);
            // a faint pane at the back and a hairline frame make the volume legible
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(back.GetComponent<Collider>());
            back.SetParent(_visual, false);
            back.localScale = new Vector3((float)WmConst.WorldW * Scale + 0.06f, (float)WmConst.WorldH * Scale + 0.06f, 1f);
            back.localPosition = new Vector3(0f, 0f, Depth * 0.5f + 0.01f);
            back.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Burrow/Panel")) { color = new Color(0f, 0f, 0.01f, 0.55f) };
            Frame();
            _wordRoot = new GameObject("words").transform; _wordRoot.SetParent(_visual, false);
            _grab = gameObject.AddComponent<BoxCollider>();
            _grab.size = LocalBounds.size; _grab.isTrigger = true; _grab.enabled = false;
            _visual.gameObject.SetActive(false);
            StartCoroutine(Load());
        }

        void Frame()
        {
            var b = LocalBounds; var e = b.extents - new Vector3(0.05f, 0.05f, 0.05f);
            var lr = new GameObject("frame").AddComponent<LineRenderer>();
            lr.transform.SetParent(_visual, false);
            lr.useWorldSpace = false; lr.loop = false; lr.widthMultiplier = 0.0025f;
            lr.sharedMaterial = new Material(Shader.Find("Burrow/VertexColor"));
            var c = new Color(0.5f, 0.6f, 0.8f, 0.35f);
            lr.startColor = c; lr.endColor = c;
            Vector3[] p = {
                new Vector3(-e.x,-e.y,-e.z), new Vector3(e.x,-e.y,-e.z), new Vector3(e.x,e.y,-e.z), new Vector3(-e.x,e.y,-e.z), new Vector3(-e.x,-e.y,-e.z),
                new Vector3(-e.x,-e.y,e.z), new Vector3(e.x,-e.y,e.z), new Vector3(e.x,e.y,e.z), new Vector3(-e.x,e.y,e.z), new Vector3(-e.x,-e.y,e.z) };
            lr.positionCount = p.Length; lr.SetPositions(p);
        }

        static IEnumerator ReadBytes(string path, System.Action<byte[]> done)
        {
            if (path.Contains("://"))
            {
                using var req = UnityWebRequest.Get(path);
                yield return req.SendWebRequest();
                done(req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null);
            }
            else done(File.Exists(path) ? File.ReadAllBytes(path) : null);
        }

        IEnumerator Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "worms");
            yield return ReadBytes(Path.Combine(dir, "shared.hwrm"), b => _shared = b);
            byte[] mind0 = null;
            yield return ReadBytes(Path.Combine(dir, "mind_0.hwrm"), b => mind0 = b);
            if (_shared == null || mind0 == null) { Debug.LogError("[World] missing worm assets"); yield break; }
            _corpus = WmAsset.Parse(_shared, mind0);
            _scroller = new WmScroller(_corpus, true);      // the play loops forever here
            for (int i = 0; i < 60 * 40; i++) _scroller.Step(WmConst.BodyDt);   // a field already full of words
            Ready = true;
            Debug.Log($"[World] ready: {_corpus.NSentences} sentences, {_corpus.NVocab} words");
        }

        public IEnumerator LoadMind(int id, System.Action<byte[]> done)
        {
            yield return ReadBytes(Path.Combine(Application.streamingAssetsPath, "worms", $"mind_{id}.hwrm"), done);
        }

        // Appears at the wearer's level in front of them the first time the burrow is placed.
        public void PlaceInFrontOf(Transform cam)
        {
            if (_placed) return;
            var f = cam.forward; f.y = 0f; f.Normalize();
            transform.position = new Vector3(cam.position.x, 0f, cam.position.z) + f * Distance + Vector3.up * Height;
            transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            _visual.gameObject.SetActive(true);
            _grab.enabled = true;
            _placed = true;
            Debug.Log($"[World] placed at {transform.position}");
        }

        public bool ContainsWorld(Vector3 worldPoint) => _placed && LocalBounds.Contains(transform.InverseTransformPoint(worldPoint));

        public Vector3 SlotPosition(int i)
        {
            // three on each side, four across the top, then a second ring further out
            float w = (float)WmConst.WorldW * Scale * 0.5f, h = (float)WmConst.WorldH * Scale * 0.5f;
            Vector3[] slots = {
                new Vector3(-w - 0.17f,  0.18f, 0f), new Vector3(w + 0.17f,  0.18f, 0f),
                new Vector3(-w - 0.17f,  0.00f, 0f), new Vector3(w + 0.17f,  0.00f, 0f),
                new Vector3(-w - 0.17f, -0.18f, 0f), new Vector3(w + 0.17f, -0.18f, 0f),
                new Vector3(-0.45f, h + 0.16f, 0f), new Vector3(-0.15f, h + 0.16f, 0f), new Vector3(0.15f, h + 0.16f, 0f), new Vector3(0.45f, h + 0.16f, 0f),
                new Vector3(-w - 0.36f,  0.09f, 0f), new Vector3(w + 0.36f,  0.09f, 0f), new Vector3(-w - 0.36f, -0.09f, 0f), new Vector3(w + 0.36f, -0.09f, 0f),
            };
            return transform.TransformPoint(slots[i % slots.Length]);
        }

        public int FreeSlot()
        {
            for (int i = 0; i < 14; i++)
            {
                bool used = false;
                foreach (var b in Bubbles) if (b.Slot == i) { used = true; break; }
                if (!used) return i;
            }
            return Bubbles.Count % 14;
        }

        // A mind arrives: spin up its world on the shared scroller, replaying what has been said so far.
        public WormAgent Admit(BMind mind, byte[] mindBytes, Vector3 entryWorldPos)
        {
            var asset = WmAsset.Parse(_shared, mindBytes);
            for (int i = 0; i < _spokenHistory.Count; i++) InjectInto(asset, _spokenHistory[i], _spokenPos[i], _spokenTaste[i]);
            bool first = Agents.Count == 0;
            var world = new WmWorld(asset, asset.Seed, null, _scroller, first);
            var agent = WormAgent.Create(this, mind, world, entryWorldPos);
            agent.transform.SetParent(_visual, false);
            Agents.Add(agent);
            return agent;
        }

        public void Dismiss(WormAgent agent)
        {
            Agents.Remove(agent);
            // whoever is left first drives the scroller
            for (int i = 0; i < Agents.Count; i++) Agents[i].World.StepsScroller = i == 0;
            Destroy(agent.gameObject);
        }

        // --- what the wearer says: identical tokens in every mind's asset, one line in the shared field
        public void Hear(string text)
        {
            if (!Ready) return;
            var vocab = VocabExt.Instance;
            var words = new List<string>(); var pos = new List<byte>(); var taste = new List<double[]>();
            foreach (var raw in text.Split(' '))
            {
                var w = VocabExt.Normalize(raw);
                if (w.Length == 0) continue;
                byte tag = 10; double[] row = null;
                if (_corpus.FindVocab(w) < 0 && vocab != null && vocab.Loaded)
                {
                    int k = vocab.Find(w, out _);
                    if (k >= 0) { tag = (byte)vocab.Pos(k); row = vocab.Taste(k); }
                }
                else if (vocab != null && vocab.Loaded) { int k = vocab.Find(w, out _); if (k >= 0) tag = (byte)vocab.Pos(k); }
                words.Add(raw.Trim()); pos.Add(tag); taste.Add(row);
            }
            if (words.Count == 0) return;
            var wa = words.ToArray(); var pa = pos.ToArray(); var ta = taste.ToArray();
            _spokenHistory.Add(wa); _spokenPos.Add(pa); _spokenTaste.Add(ta);
            int[] toks = InjectInto(_corpus, wa, pa, ta);
            foreach (var ag in Agents) InjectInto(ag.World.Asset, wa, pa, ta);
            if (!_scroller.SpawnLine(toks, true)) Debug.LogWarning("[World] field full, sentence dropped");
        }

        static int[] InjectInto(WmAsset a, string[] words, byte[] pos, double[][] taste)
        {
            var toks = new int[words.Length];
            for (int i = 0; i < words.Length; i++)
            {
                var w = VocabExt.Normalize(words[i]);
                int vi = a.FindVocab(w);
                toks[i] = vi >= 0 || taste[i] == null ? a.AddToken(words[i], vi, pos[i]) : a.AddWord(words[i], taste[i], pos[i]);
            }
            return toks;
        }

        // --- depth: stable per word, so a word keeps its place in the volume as it drifts down
        public float WordDepth(WmLine line, int wordIdx)
        {
            uint h = (uint)(line.LineId * 7919 + wordIdx * 104729 + 13);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return ((h & 0xffff) / 65535f - 0.5f) * Depth;
        }

        public Vector3 ToLocal(double x, double y, float z) =>
            new Vector3(((float)x - (float)WmConst.CenterX) * Scale, ((float)y - (float)WmConst.WorldH * 0.5f) * Scale, -z);

        void Update()
        {
            if (!Ready || !_placed) return;
            // nobody here: the text still drifts (one shared step), so the world never looks dead
            if (Agents.Count == 0)
            {
                _acc += Mathf.Min(Time.deltaTime, 0.1f);
                while (_acc >= (float)WmConst.BodyDt) { _scroller.Step(WmConst.BodyDt); _acc -= (float)WmConst.BodyDt; }
            }
            DrawWords();
        }

        void DrawWords()
        {
            var seen = new HashSet<WmWord>();
            foreach (var line in _scroller.Lines)
                for (int wi = 0; wi < line.Words.Length; wi++)
                {
                    var w = line.Words[wi];
                    if (w.Eaten || w.Y < 0 || w.Y > WmConst.WorldH) continue;
                    seen.Add(w);
                    if (!_words.TryGetValue(w, out var tm))
                    {
                        tm = _pool.Count > 0 ? _pool.Pop() : MakeWord();
                        tm.gameObject.SetActive(true);
                        tm.text = _corpus.TokText[w.Tok];
                        tm.color = line.SentIdx < 0 ? new Color(0.62f, 0.85f, 1f, 1f)
                                 : line.Edible ? new Color(0.93f, 0.9f, 0.82f, 1f) : new Color(0.55f, 0.55f, 0.55f, 1f);
                        _words[w] = tm;
                    }
                    float yn = (float)(w.Y / WmConst.WorldH);
                    var c = tm.color; c.a = Mathf.Clamp01(yn / 0.08f) * Mathf.Clamp01((1f - yn) / 0.08f); tm.color = c;
                    tm.transform.localPosition = ToLocal(w.X, w.Y, WordDepth(line, wi));
                    tm.transform.localRotation = Quaternion.identity;      // planar: the box turns, the text does not
                }
            _gone.Clear();
            foreach (var kv in _words) if (!seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var w in _gone) { var tm = _words[w]; _words.Remove(w); tm.gameObject.SetActive(false); _pool.Push(tm); }
        }

        TextMesh MakeWord()
        {
            var go = new GameObject("w");
            go.transform.SetParent(_wordRoot, false);
            var t = go.AddComponent<TextMesh>();
            t.font = BurrowText.Font; t.fontSize = 40; t.characterSize = 0.0036f; t.anchor = TextAnchor.MiddleCenter; t.alignment = TextAlignment.Center;
            go.GetComponent<MeshRenderer>().sharedMaterial = BurrowText.Material;
            return t;
        }
    }
}
