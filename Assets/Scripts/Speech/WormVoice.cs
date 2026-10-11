using System.Collections.Generic;
using UnityEngine;
using Burrow;

namespace Speech
{
    // A mind's mouth. Eaten words gather into a line above the animal's head; after a pause of
    // `phraseGap` seconds with nothing new (or at `maxWords`) the whole line is recited in one
    // breath, in this mind's own voice, spatialised where it is.
    public class WormVoice : MonoBehaviour
    {
        public VoiceProfile Profile { get; private set; }
        public float phraseGap = 3f;
        public int maxWords = 7;
        AudioSource _src;
        readonly List<string> _pending = new List<string>();
        readonly Queue<string> _queue = new Queue<string>();      // phrases waiting to be voiced
        readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
        bool _busy;
        float _lastWord = -1f, _hideAt = -1f;
        TextMesh _caption;
        Color _tint = Color.white;

        public static WormVoice Attach(GameObject host, BMind mind)
        {
            var v = host.AddComponent<WormVoice>();
            var eng = SpeechEngine.Instance;
            v.Profile = VoiceProfile.For(mind, eng != null ? eng.NumSpeakers : 109);
            v._src = host.AddComponent<AudioSource>();
            v._src.spatialBlend = 1f; v._src.rolloffMode = AudioRolloffMode.Logarithmic;
            v._src.minDistance = 0.4f; v._src.maxDistance = 8f; v._src.playOnAwake = false;
            v._src.pitch = v.Profile.pitch;
            v._src.volume = 1f;
            // a hall round each voice: the words are recited, not muttered
            var rv = host.AddComponent<AudioReverbFilter>();
            rv.reverbPreset = AudioReverbPreset.Concerthall;
            rv.dryLevel = 0f; rv.room = -800f; rv.reverbLevel = -400f; rv.decayTime = 1.9f;
            v.BuildCaption();
            return v;
        }

        public void SetTint(Color c) { _tint = c; if (_caption != null) _caption.color = c; }

        void BuildCaption()
        {
            var go = new GameObject("caption");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.045f, -0.03f);
            _caption = go.AddComponent<TextMesh>();
            _caption.font = Burrow.BurrowText.Font; _caption.fontSize = 40; _caption.characterSize = 0.0042f;
            _caption.anchor = TextAnchor.LowerCenter; _caption.alignment = TextAlignment.Center;
            _caption.color = _tint;
            go.GetComponent<MeshRenderer>().sharedMaterial = Burrow.BurrowText.Material;
            go.SetActive(false);
        }

        // Only words: punctuation tokens the animal eats are swallowed in silence.
        public void Speak(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return;
            var w = word.Trim().Trim('.', ',', '!', '?', ';', ':', '"', '(', ')', '[', ']', '-');
            bool hasLetter = false, hasVowel = false; foreach (var ch in w) { if (char.IsLetter(ch)) hasLetter = true; if ("aeiouyAEIOUY".IndexOf(ch) >= 0) hasVowel = true; }
            if (!hasLetter) return;
            var low = w.ToLowerInvariant();
            if ((w.Length == 1 && low != "a" && low != "i" && low != "o") || (!hasVowel && w.IndexOf('\'') < 0)) return;   // stray letters, not words
            _pending.Add(w);
            _lastWord = Time.time;
            ShowPending();
            if (_pending.Count >= maxWords) Flush();
        }

        void ShowPending()
        {
            if (_caption == null) return;
            _caption.text = string.Join(" ", _pending);
            _caption.gameObject.SetActive(true);
            _hideAt = -1f;
        }

        void Flush()
        {
            if (_pending.Count == 0) return;
            var phrase = string.Join(" ", _pending);
            _pending.Clear();
            if (_queue.Count > 3) _queue.Dequeue();
            _queue.Enqueue(phrase);
        }

        void Update()
        {
            if (_pending.Count > 0 && Time.time - _lastWord > phraseGap) Flush();
            if (_caption != null && _caption.gameObject.activeSelf)
            {
                var cam = Camera.main;
                if (cam != null) _caption.transform.rotation = Quaternion.LookRotation(_caption.transform.position - cam.transform.position, Vector3.up);
                if (_hideAt > 0f && Time.time > _hideAt) _caption.gameObject.SetActive(false);
            }
            if (_busy || _queue.Count == 0 || _src.isPlaying) return;
            var eng = SpeechEngine.Instance;
            if (eng == null || !eng.Ready) { _queue.Clear(); return; }
            var phrase = _queue.Dequeue();
            // declaimed: capitalised and ended, so the synthesiser gives the line a full cadence
            var line = char.ToUpperInvariant(phrase[0]) + phrase.Substring(1) + ".";
            if (_cache.TryGetValue(line, out var clip)) { Play(clip, phrase); return; }
            _busy = true;
            eng.Say(line, Profile.speaker, Profile.speed, c =>
            {
                _busy = false;
                if (c == null || this == null) return;
                if (_cache.Count > 200) _cache.Clear();
                _cache[line] = c;
                Play(c, phrase);
            });
        }

        void Play(AudioClip c, string phrase)
        {
            _src.PlayOneShot(c);
            if (_caption != null && _pending.Count == 0) { _caption.text = phrase; _caption.gameObject.SetActive(true); }
            _hideAt = Time.time + c.length;        // the clip carries its own silent tail
        }
    }
}
