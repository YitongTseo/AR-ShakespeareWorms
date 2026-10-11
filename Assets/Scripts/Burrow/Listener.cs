using System.Collections;
using UnityEngine;
using Speech;

namespace Burrow
{
    // Push-to-talk. Interaction calls StartTalk when a pinch is held with nothing targeted and
    // StopTalk when it is released. The recognised sentence is captioned by the hand for a
    // moment and published on the ConversationBus (the text world lays it into the field).
    public class Listener : MonoBehaviour
    {
        public MinimalHandVisualizer hands;
        public Transform cameraTransform;
        public int sampleRate = 16000;
        public int maxSeconds = 12;

        AudioClip _rec;
        int _hand = -1;
        float _t0;
        TextMesh _caption;
        Transform _orb;
        Material _orbMat;
        bool _permissionAsked;

        void Start()
        {
            var go = new GameObject("Caption");
            _caption = go.AddComponent<TextMesh>();
            _caption.font = BurrowText.Font; _caption.fontSize = 44; _caption.characterSize = 0.006f;
            _caption.anchor = TextAnchor.LowerCenter; _caption.alignment = TextAlignment.Center;
            _caption.color = new Color(1f, 0.92f, 0.8f, 1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = BurrowText.Material;
            go.SetActive(false);

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(orb.GetComponent<Collider>());
            orb.transform.localScale = Vector3.one * 0.022f;
            _orbMat = new Material(Shader.Find("Burrow/Unlit")) { color = new Color(1f, 0.3f, 0.25f, 1f) };
            orb.GetComponent<Renderer>().sharedMaterial = _orbMat;
            _orb = orb.transform; orb.SetActive(false);
        }

        public bool Listening => _hand >= 0;

        // Returns true when recording actually started (engine ready, permission granted, mic present).
        public bool StartTalk(int hand, Vector3 pos)
        {
            if (_hand >= 0) return false;
            var eng = SpeechEngine.Instance;
            if (eng == null || !eng.Ready) { Caption("(speech engine not ready)", pos, 1.5f); return false; }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                if (!_permissionAsked) { UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone); _permissionAsked = true; }
                Caption("(allow the microphone, then try again)", pos, 2f);
                return false;
            }
#endif
            if (Microphone.devices.Length == 0) { Caption("(no microphone)", pos, 1.5f); return false; }
            _rec = Microphone.Start(null, false, maxSeconds, sampleRate);
            _hand = hand; _t0 = Time.time;
            _orb.gameObject.SetActive(true);
            Caption("listening... let go to send", pos, 1.2f);
            return true;
        }

        public void StopTalk(bool cancel)
        {
            if (_hand < 0) return;
            int hand = _hand;
            int pos = Microphone.GetPosition(null);
            Microphone.End(null);
            _orb.gameObject.SetActive(false);
            _hand = -1;
            if (cancel || _rec == null || pos < sampleRate / 4) return;     // under a quarter second: nothing said
            var pcm = new float[pos];
            _rec.GetData(pcm, 0);
            // level check + gentle auto-gain: the headset mic is quiet, whisper wants speech near full scale
            double sum = 0; float peak = 0f;
            foreach (var v in pcm) { sum += v * v; peak = Mathf.Max(peak, Mathf.Abs(v)); }
            float rms = (float)System.Math.Sqrt(sum / pcm.Length);
            float gain = peak > 1e-4f ? Mathf.Clamp(0.7f / peak, 1f, 12f) : 1f;
            if (gain > 1.01f) for (int k = 0; k < pcm.Length; k++) pcm[k] = Mathf.Clamp(pcm[k] * gain, -1f, 1f);
            Debug.Log($"[Speech] captured {pos / (float)sampleRate:F1}s, rms {rms:F4}, peak {peak:F3}, gain x{gain:F1}, device '{(Microphone.devices.Length > 0 ? Microphone.devices[0] : "none")}'");
            Vector3 at = hand == 0 ? hands.LeftPinchPos : hands.RightPinchPos;
            Caption("...", at, 4f);
            SpeechEngine.Instance.Transcribe(pcm, sampleRate, text =>
            {
                Caption(string.IsNullOrEmpty(text) ? "(nothing heard)" : text, at, 4f);
                Debug.Log($"[Speech] heard: \"{text}\"");
                ConversationBus.Publish(text);
            });
        }

        void Update()
        {
            if (_hand >= 0)
            {
                _orb.position = (_hand == 0 ? hands.LeftPinchPos : hands.RightPinchPos) + Vector3.up * 0.04f;
                _orbMat.color = Color.Lerp(new Color(1f, 0.3f, 0.25f), new Color(1f, 0.8f, 0.3f), 0.5f + 0.5f * Mathf.Sin(Time.time * 8f));
                if (Time.time - _t0 > maxSeconds - 0.2f) StopTalk(false);
            }
            if (_caption.gameObject.activeSelf && cameraTransform != null)
            {
                var look = _caption.transform.position - cameraTransform.position; look.y = 0f;
                if (look.sqrMagnitude > 1e-4f) _caption.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
        }

        void Caption(string text, Vector3 at, float seconds)
        {
            StopAllCoroutines();
            _caption.text = text;
            _caption.transform.position = at + Vector3.up * 0.08f;
            _caption.gameObject.SetActive(true);
            StartCoroutine(HideAfter(seconds));
        }

        IEnumerator HideAfter(float s) { yield return new WaitForSeconds(s); _caption.gameObject.SetActive(false); }
    }
}
