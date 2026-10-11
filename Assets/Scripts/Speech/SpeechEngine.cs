using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace Speech
{
    // Offline text-to-speech and speech-to-text on a worker thread. Results come back on the
    // main thread through the callbacks. Without the native plugin (editor) it stays idle.
    public class SpeechEngine : MonoBehaviour
    {
        public static SpeechEngine Instance { get; private set; }
        public bool Ready { get; private set; }
        public int NumSpeakers { get; private set; } = 1;
        public int SampleRate { get; private set; } = 22050;
        public event Action ReadyChanged;

        IntPtr _tts, _asr;
        Thread _worker;
        readonly BlockingCollection<Action> _jobs = new BlockingCollection<Action>();
        readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
        volatile bool _quit;

        void Awake() { Instance = this; }

        void Start() => StartCoroutine(Boot());

        IEnumerator Boot()
        {
            if (!SpeechNative.Available) { Debug.LogWarning("[Speech] native plugin unavailable; speech disabled"); yield break; }
            yield return SpeechModels.Install(p => { });
            if (!SpeechModels.Installed) yield break;
            _worker = new Thread(Loop) { IsBackground = true, Name = "speech" };
            _worker.Start();
            _jobs.Add(() =>
            {
                var t0 = DateTime.Now;
                _tts = SpeechNative.ws_tts_create(SpeechModels.TtsModel, SpeechModels.TtsTokens, SpeechModels.TtsData, 3);
                _asr = SpeechNative.ws_asr_create(SpeechModels.AsrEncoder, SpeechModels.AsrDecoder, SpeechModels.AsrTokens, 3);
                bool ok = _tts != IntPtr.Zero && _asr != IntPtr.Zero;
                int ns = ok ? SpeechNative.ws_tts_num_speakers(_tts) : 1;
                int sr = ok ? SpeechNative.ws_tts_sample_rate(_tts) : 22050;
                _main.Enqueue(() =>
                {
                    Ready = ok; NumSpeakers = Math.Max(1, ns); SampleRate = sr;
                    Debug.Log($"[Speech] engine {(ok ? "ready" : "FAILED")} in {(DateTime.Now - t0).TotalSeconds:F1}s, {ns} speakers @ {sr} Hz");
                    ReadyChanged?.Invoke();
                });
            });
        }

        void Loop()
        {
            foreach (var job in _jobs.GetConsumingEnumerable())
            {
                if (_quit) break;
                try { job(); } catch (Exception e) { Debug.LogError($"[Speech] job failed: {e.Message}"); }
            }
        }

        void Update()
        {
            while (_main.TryDequeue(out var a)) a();
        }

        void OnDestroy()
        {
            _quit = true; _jobs.CompleteAdding();
            if (_tts != IntPtr.Zero) SpeechNative.ws_tts_destroy(_tts);
            if (_asr != IntPtr.Zero) SpeechNative.ws_asr_destroy(_asr);
        }

        // Synthesise `text` with speaker `sid`; `done` receives a ready-to-play clip (or null).
        public void Say(string text, int sid, float speed, Action<AudioClip> done)
        {
            if (!Ready) { done?.Invoke(null); return; }
            sid = Mathf.Clamp(sid, 0, NumSpeakers - 1);
            _jobs.Add(() =>
            {
                var h = SpeechNative.ws_tts_generate(_tts, text, sid, speed, out var ptr, out int n, out int sr);
                float[] samples = null;
                if (h != IntPtr.Zero && n > 0) { samples = new float[n]; Marshal.Copy(ptr, samples, 0, n); }
                if (h != IntPtr.Zero) SpeechNative.ws_tts_free_audio(h);
                _main.Enqueue(() =>
                {
                    if (samples == null) { done?.Invoke(null); return; }
                    // a tail of silence keeps the source playing while the reverb rings out
                    int pad = (int)(sr * 1.6f);
                    var padded = new float[samples.Length + pad];
                    System.Array.Copy(samples, padded, samples.Length);
                    var clip = AudioClip.Create($"say_{text}", padded.Length, 1, sr, false);
                    clip.SetData(padded, 0);
                    done?.Invoke(clip);
                });
            });
        }

        // Transcribe mono PCM in [-1,1] at `sampleRate`; `done` receives the text ("" if nothing).
        public void Transcribe(float[] pcm, int sampleRate, Action<string> done)
        {
            if (!Ready || pcm == null || pcm.Length == 0) { done?.Invoke(""); return; }
            _jobs.Add(() =>
            {
                var p = SpeechNative.ws_asr_recognize(_asr, pcm, pcm.Length, sampleRate);
                string text = p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
                if (p != IntPtr.Zero) SpeechNative.ws_free(p);
                _main.Enqueue(() => done?.Invoke(text.Trim()));
            });
        }
    }
}
