using UnityEngine;

namespace Burrow
{
    // Three tabs under the text world that switch the Overlays: x-ray, senses, chemo. Touch one
    // with an index fingertip (a short dwell, so a hand passing through does not flip it). With a
    // keyboard: X, O and C, the viewer's own keys.
    public class ViewToggles : MonoBehaviour
    {
        const float TabW = 0.10f, TabH = 0.032f, Spacing = 0.125f, Dwell = 0.12f;

        class Tab { public string Name; public KeyCode Key; public Transform T; public Material Back; public TextMesh Label; public float Pop; }
        readonly Tab[] _tabs = {
            new Tab { Name = "x-ray", Key = KeyCode.X }, new Tab { Name = "senses", Key = KeyCode.O }, new Tab { Name = "chemo", Key = KeyCode.C } };
        readonly int[] _over = { -1, -1 };          // per hand: the tab under the fingertip
        readonly float[] _dwell = new float[2];
        readonly bool[] _fired = new bool[2];

        public static ViewToggles Create(Transform parent, Vector3 centre)
        {
            var go = new GameObject("toggles"); go.transform.SetParent(parent, false); go.transform.localPosition = centre;
            var v = go.AddComponent<ViewToggles>();
            v.Build();
            return v;
        }

        void Build()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                var tab = _tabs[i];
                tab.T = new GameObject(tab.Name).transform; tab.T.SetParent(transform, false);
                tab.T.localPosition = new Vector3((i - (_tabs.Length - 1) * 0.5f) * Spacing, 0f, 0f);
                var back = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
                Destroy(back.GetComponent<Collider>());
                back.SetParent(tab.T, false); back.localScale = new Vector3(TabW, TabH, 1f); back.localPosition = new Vector3(0f, 0f, 0.001f);
                back.GetComponent<Renderer>().sharedMaterial = tab.Back = new Material(Shader.Find("Burrow/Panel"));
                tab.Label = new GameObject("label").AddComponent<TextMesh>();
                tab.Label.transform.SetParent(tab.T, false);
                tab.Label.font = BurrowText.Font; tab.Label.fontSize = 40; tab.Label.characterSize = 0.0034f;
                tab.Label.anchor = TextAnchor.MiddleCenter; tab.Label.text = tab.Name;
                tab.Label.GetComponent<MeshRenderer>().sharedMaterial = BurrowText.Material;
            }
        }

        static bool Get(int i) => i == 0 ? Overlays.XRay : i == 1 ? Overlays.Senses : Overlays.Chemo;

        void Flip(int i)
        {
            if (i == 0) Overlays.XRay = !Overlays.XRay; else if (i == 1) Overlays.Senses = !Overlays.Senses; else Overlays.Chemo = !Overlays.Chemo;
            _tabs[i].Pop = 1f;
            Debug.Log($"[World] {_tabs[i].Name} {(Get(i) ? "on" : "off")}");
        }

        // Called every frame for each hand with its index fingertip (valid = tracked and not pinching).
        public void Touch(int hand, bool valid, Vector3 tipWorld)
        {
            int over = -1;
            if (valid)
            {
                var p = transform.InverseTransformPoint(tipWorld);
                for (int i = 0; i < _tabs.Length; i++)
                {
                    var d = p - _tabs[i].T.localPosition;
                    if (Mathf.Abs(d.x) < TabW * 0.55f && Mathf.Abs(d.y) < TabH * 0.75f && Mathf.Abs(d.z) < 0.035f) { over = i; break; }
                }
            }
            if (over != _over[hand]) { _over[hand] = over; _dwell[hand] = 0f; _fired[hand] = false; return; }
            if (over < 0 || _fired[hand]) return;
            _dwell[hand] += Time.deltaTime;
            if (_dwell[hand] >= Dwell) { _fired[hand] = true; Flip(over); }
        }

        void Update()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                var tab = _tabs[i];
                if (Input.GetKeyDown(tab.Key)) Flip(i);
                bool on = Get(i), near = _over[0] == i || _over[1] == i;
                tab.Pop = Mathf.MoveTowards(tab.Pop, 0f, Time.deltaTime * 4f);
                tab.T.localScale = Vector3.one * (1f + 0.18f * tab.Pop + (near ? 0.06f : 0f));
                tab.Back.color = on ? new Color(0.05f, 0.32f, 0.3f, 0.8f) : new Color(0f, 0f, 0.02f, 0.55f);
                tab.Label.color = on ? new Color(0.75f, 1f, 0.92f, 1f) : new Color(0.6f, 0.65f, 0.72f, near ? 0.9f : 0.6f);
            }
        }
    }
}
