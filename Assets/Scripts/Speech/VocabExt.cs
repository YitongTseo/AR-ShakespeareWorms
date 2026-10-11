using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Speech
{
    // The extended vocabulary baked by tools/speech/vocab.py: for each word the 11-dim taste
    // row the worm's chemosensory path wants, and its POS tag. Unknown words map to the
    // nearest known word by character trigrams, so the worm can always taste what it hears.
    public class VocabExt : MonoBehaviour
    {
        public static VocabExt Instance { get; private set; }
        public bool Loaded { get; private set; }
        public int Count => _words?.Length ?? 0;
        public int Dim { get; private set; } = 11;

        [Serializable] class File_ { public int n, dim, hamlet; public string embedder; public string[] words; public float[] etable; public int[] pos; }

        string[] _words; float[] _etable; int[] _pos;
        readonly Dictionary<string, int> _index = new Dictionary<string, int>();
        readonly Dictionary<string, List<int>> _trigrams = new Dictionary<string, List<int>>();

        void Awake() { Instance = this; }
        void Start() => StartCoroutine(Load());

        IEnumerator Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "speech", "vocab_ext.json");
            string json = null;
            if (path.Contains("://"))
            {
                using var req = UnityWebRequest.Get(path);
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) json = req.downloadHandler.text;
            }
            else if (File.Exists(path)) json = File.ReadAllText(path);
            if (json == null) { Debug.LogWarning("[Vocab] no vocab_ext.json"); yield break; }
            var f = JsonUtility.FromJson<File_>(json);
            _words = f.words; _etable = f.etable; _pos = f.pos; Dim = f.dim;
            for (int i = 0; i < _words.Length; i++)
            {
                _index[_words[i]] = i;
                foreach (var t in Trigrams(_words[i]))
                {
                    if (!_trigrams.TryGetValue(t, out var l)) _trigrams[t] = l = new List<int>();
                    l.Add(i);
                }
            }
            Loaded = true;
            Debug.Log($"[Vocab] {_words.Length} words ({f.hamlet} from Hamlet), embedder {f.embedder}");
        }

        public static string Normalize(string w) => w.Trim().Trim('.', ',', '!', '?', ';', ':', '"', '(', ')').ToLowerInvariant().Trim('\'');

        // Exact or nearest-by-trigram index for a spoken word; -1 if the table is not loaded.
        public int Find(string word, out bool exact)
        {
            exact = false;
            if (!Loaded) return -1;
            var w = Normalize(word);
            if (w.Length == 0) return -1;
            if (_index.TryGetValue(w, out int i)) { exact = true; return i; }
            var counts = new Dictionary<int, int>();
            var tg = Trigrams(w);
            foreach (var t in tg)
                if (_trigrams.TryGetValue(t, out var l)) foreach (var k in l) counts[k] = counts.TryGetValue(k, out var c) ? c + 1 : 1;
            int best = -1; float bestScore = 0f;
            foreach (var kv in counts)
            {
                int other = _words[kv.Key].Length + 2;              // its trigram count
                float jaccard = kv.Value / (float)(tg.Count + other - kv.Value);
                if (jaccard > bestScore) { bestScore = jaccard; best = kv.Key; }
            }
            return best;
        }

        public string Word(int i) => _words[i];
        public int Pos(int i) => _pos[i];
        public double[] Taste(int i)
        {
            var row = new double[Dim];
            for (int k = 0; k < Dim; k++) row[k] = _etable[i * Dim + k];
            return row;
        }

        static List<string> Trigrams(string w)
        {
            var s = "^" + w + "$"; var l = new List<string>(s.Length);
            for (int i = 0; i + 3 <= s.Length; i++) l.Add(s.Substring(i, 3));
            if (l.Count == 0) l.Add(s);
            return l;
        }
    }
}
