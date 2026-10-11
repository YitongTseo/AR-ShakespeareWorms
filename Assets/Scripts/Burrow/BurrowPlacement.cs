using UnityEngine;

namespace Burrow
{
    // Roots the network in the room. The wearer pinches at the centre of the physical
    // sculpture; the baked tendril tip lands exactly there and the funnel grows up
    // from it. Holding both pinches for two seconds returns to placement mode.
    public class BurrowPlacement : MonoBehaviour
    {
        public MinimalHandVisualizer hands;
        public Transform cameraTransform;
        public float pinchOn = 0.018f, pinchOff = 0.03f;
        public bool Placed { get; private set; }

        BurrowLoader _loader;
        Transform _reticle;
        TextMesh _prompt;
        float _bothHeld, _held, _readyAt;
        Vector3 _tipLocal;
        public float holdToPlace = 1.0f;

        void Start()
        {
            _loader = BurrowLoader.Instance;
            _loader.Loaded += OnLoaded;
            var ret = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ret.GetComponent<Collider>());
            ret.name = "PlacementReticle";
            ret.transform.localScale = Vector3.one * 0.03f;
            var sh = Shader.Find("Burrow/Unlit");
            if (sh != null) ret.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = new Color(1f, 0.35f, 0.3f, 1f) };
            _reticle = ret.transform;

            var pgo = new GameObject("Prompt");
            _prompt = pgo.AddComponent<TextMesh>();
            _prompt.characterSize = 0.012f; _prompt.fontSize = 48; _prompt.anchor = TextAnchor.MiddleCenter;
            _prompt.alignment = TextAlignment.Center;
            _prompt.color = new Color(1f, 0.95f, 0.9f, 0.95f);
            _prompt.font = BurrowText.Font;
            _prompt.GetComponent<MeshRenderer>().sharedMaterial = BurrowText.Material;
            _prompt.text = "loading the burrows...";
        }

        void OnLoaded()
        {
            var m = _loader.Manifest;
            foreach (var n in m.nodes) if (n.kind == "sculpture") _tipLocal = BurrowLoader.V(n.p);
            _loader.Content.gameObject.SetActive(false);
            _prompt.text = "pinch and hold at the centre of the sculpture";
            _readyAt = Time.time + 2.5f;      // let hand tracking settle; no accidental placement on launch
        }

        void Update()
        {
            if (!_loader.IsLoaded) { FollowPrompt(); return; }
            bool l = hands.LeftTracked && hands.LeftPinch < pinchOn, r = hands.RightTracked && hands.RightPinch < pinchOn;

            if (Placed)
            {
                // both hands pinched and held: re-place
                _bothHeld = (l && r) ? _bothHeld + Time.deltaTime : 0f;
                if (_bothHeld > 2f) { Placed = false; _bothHeld = 0f; _held = 0f; _readyAt = Time.time + 1.5f; _loader.Content.gameObject.SetActive(false); _prompt.text = "pinch and hold at the centre of the sculpture"; }
                _reticle.gameObject.SetActive(false); _prompt.gameObject.SetActive(false);
                return;
            }

            _reticle.gameObject.SetActive(true); _prompt.gameObject.SetActive(true);
            FollowPrompt();
            bool pinching = (l || r) && Time.time > _readyAt;
            Vector3 at = r ? hands.RightPinchPos : hands.LeftPinchPos;
            if (!pinching) at = hands.RightTracked ? hands.RightPinchPos : hands.LeftPinchPos;
            _reticle.position = at;
            // hold the pinch still for a second: the reticle swells as it charges
            _held = pinching ? _held + Time.deltaTime : 0f;
            _reticle.localScale = Vector3.one * (0.03f + 0.05f * Mathf.Clamp01(_held / holdToPlace));
            if (_held >= holdToPlace) { Place(at); _held = 0f; }
        }

        void FollowPrompt()
        {
            if (cameraTransform == null) return;
            var f = cameraTransform.forward; f.y = 0f; f.Normalize();
            _prompt.transform.position = cameraTransform.position + f * 1.1f + Vector3.up * -0.12f;
            _prompt.transform.rotation = Quaternion.LookRotation(f, Vector3.up);
        }

        void Place(Vector3 sculptureCentre)
        {
            // yaw the layout so its tendril side faces away from the wearer: the throat
            // then wraps around the viewer's side of the sculpture
            var toUser = cameraTransform.position - sculptureCentre; toUser.y = 0f;
            float yaw = toUser.sqrMagnitude > 1e-4f ? Mathf.Atan2(toUser.x, toUser.z) * Mathf.Rad2Deg : 0f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            transform.position = sculptureCentre - transform.rotation * _tipLocal;
            _loader.Content.gameObject.SetActive(true);
            Placed = true;
            if (TextWorld.Instance != null) TextWorld.Instance.PlaceInFrontOf(cameraTransform);
            Debug.Log($"[Burrow] placed at {sculptureCentre}, yaw {yaw:F0}");
        }
    }
}
