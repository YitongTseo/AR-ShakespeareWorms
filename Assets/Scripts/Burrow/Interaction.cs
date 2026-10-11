using System.Collections;
using UnityEngine;
using Speech;

namespace Burrow
{
    // All hand interaction once the burrow is placed.
    //   POINT (index out, others curled) at a dome: a line leaves the fingertip, the dome heats
    //         while you linger, flashes, and its mind flies to the text world.
    //   PINCH a bubble: carry it around. It heats while held; hold it for holdToReturn seconds
    //         and it flashes and flies home. Let go sooner and it just stays where you put it.
    //   PINCH inside the text world: one hand moves it; two hands move and turn it.
    //   PINCH held anywhere else: push-to-talk (Listener). Release to send.
    //   FLICK the index finger through a bubble (a fast swipe, no pinch): it flies home.
    //   TOUCH a tab under the text world with an index fingertip: x-ray, senses, chemo on/off.
    public class Interaction : MonoBehaviour
    {
        public MinimalHandVisualizer hands;
        public Transform cameraTransform;
        public BurrowPlacement placement;
        public Listener listener;
        public float pinchOn = 0.018f, pinchOff = 0.03f, talkAfter = 0.5f;
        public float dwellSeconds = 1.6f, rayRange = 8f, holdToReturn = 3.0f, bubbleReach = 0.11f;
        public float flickSpeed = 1.1f;          // m/s of the index tip that counts as a flick

        enum Mode { None, GrabWorld, GrabBubble, Talk }
        class Hand
        {
            public bool down; public float t0; public Mode mode; public Vector3 pinch, grabOffset;
            public MindBubble bubble; public float heat;
            public LineRenderer ray; public DomeView target; public float dwell;
            public Vector3 tipPrev; public bool tipValid; public float flickCooldown;
        }
        readonly Hand[] _h = { new Hand(), new Hand() };
        int _loading;
        // two-handed world grab
        bool _twoHanded; float _yaw0, _angle0; Vector3 _midOffset;

        void Start()
        {
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject($"ray_{i}");
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.positionCount = 2; lr.widthMultiplier = 0.004f;
                lr.sharedMaterial = new Material(Shader.Find("Burrow/VertexColor"));
                go.SetActive(false);
                _h[i].ray = lr;
            }
        }

        void Update()
        {
            if (placement != null && !placement.Placed) { foreach (var h in _h) Cancel(h); return; }
            var loader = BurrowLoader.Instance;
            if (loader == null || !loader.IsLoaded) return;
            for (int i = 0; i < 2; i++) Pinch(i);
            GrabWorld();
            for (int i = 0; i < 2; i++) { Point(i); Flick(i); Touch(i); }
        }

        // ------------------------------------------------------------------ touch: the view tabs
        void Touch(int i)
        {
            var world = TextWorld.Instance; if (world == null || world.Toggles == null) return;
            bool valid = hands.TryGetJoint(i, UnityEngine.XR.Hands.XRHandJointID.IndexTip, out var tip) && !_h[i].down;
            world.Toggles.Touch(i, valid, tip);
        }

        void Cancel(Hand h)
        {
            if (h.mode == Mode.Talk && listener != null) listener.StopTalk(true);
            if (h.mode == Mode.GrabBubble && h.bubble != null) h.bubble.EndGrab();
            h.mode = Mode.None; h.down = false; h.bubble = null; h.ray.gameObject.SetActive(false); SetTarget(h, null);
        }

        // ------------------------------------------------------------------ pinch
        void Pinch(int i)
        {
            var h = _h[i];
            bool tracked = i == 0 ? hands.LeftTracked : hands.RightTracked;
            float d = i == 0 ? hands.LeftPinch : hands.RightPinch;
            Vector3 p = i == 0 ? hands.LeftPinchPos : hands.RightPinchPos;
            bool now = tracked && d < (h.down ? pinchOff : pinchOn);
            var world = TextWorld.Instance;
            h.pinch = p;

            if (now && !h.down)
            {
                h.down = true; h.t0 = Time.time; h.heat = 0f;
                var b = NearestBubble(p);
                if (b != null) { h.mode = Mode.GrabBubble; h.bubble = b; h.grabOffset = b.transform.position - p; b.BeginGrab(); }
                else if (world != null && world.ContainsWorld(p)) { h.mode = Mode.GrabWorld; h.grabOffset = world.transform.position - p; }
                else h.mode = Mode.None;
            }
            if (h.down)
            {
                switch (h.mode)
                {
                    case Mode.GrabBubble:
                        if (h.bubble == null) { h.mode = Mode.None; break; }
                        h.bubble.transform.position = Vector3.Lerp(h.bubble.transform.position, p + h.grabOffset, 0.4f);
                        h.heat = Mathf.Min(1f, h.heat + Time.deltaTime / holdToReturn);
                        h.bubble.SetHeat(h.heat);
                        if (h.heat >= 1f)
                        {
                            Debug.Log($"[World] returning {h.bubble.Dome.Mind.name}");
                            h.bubble.EndGrab(); h.bubble.Return(); h.bubble = null; h.mode = Mode.None;
                        }
                        break;
                    case Mode.None:
                        if (Time.time - h.t0 > talkAfter && listener != null && !listener.Listening && listener.StartTalk(i, p))
                            h.mode = Mode.Talk;
                        break;
                }
            }
            if (!now && h.down)
            {
                h.down = false;
                if (h.mode == Mode.Talk) listener.StopTalk(false);
                if (h.mode == Mode.GrabBubble && h.bubble != null) h.bubble.EndGrab();
                h.mode = Mode.None; h.bubble = null;
            }
        }

        MindBubble NearestBubble(Vector3 p)
        {
            var world = TextWorld.Instance; if (world == null) return null;
            MindBubble best = null; float bestD = bubbleReach;
            foreach (var b in world.Bubbles)
            {
                if (b.Busy) continue;
                float dist = Vector3.Distance(b.transform.position, p) - b.Radius;
                if (dist < bestD) { bestD = dist; best = b; }
            }
            return best;
        }

        // one hand: carry the box. two hands: carry it by the midpoint and turn it with the line between them.
        void GrabWorld()
        {
            var world = TextWorld.Instance; if (world == null) return;
            bool l = _h[0].down && _h[0].mode == Mode.GrabWorld, r = _h[1].down && _h[1].mode == Mode.GrabWorld;
            if (l && r)
            {
                var mid = (_h[0].pinch + _h[1].pinch) * 0.5f;
                var v = _h[1].pinch - _h[0].pinch; v.y = 0f;
                float angle = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                if (!_twoHanded) { _twoHanded = true; _yaw0 = world.transform.eulerAngles.y; _angle0 = angle; _midOffset = world.transform.position - mid; }
                float yaw = _yaw0 + Mathf.DeltaAngle(_angle0, angle);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                // keep the grabbed midpoint under the hands while turning about it
                var offsetNow = rot * Quaternion.Inverse(Quaternion.Euler(0f, _yaw0, 0f)) * _midOffset;
                world.transform.rotation = Quaternion.Slerp(world.transform.rotation, rot, 0.4f);
                world.transform.position = Vector3.Lerp(world.transform.position, mid + offsetNow, 0.4f);
            }
            else
            {
                if (_twoHanded) { _twoHanded = false; foreach (var hh in _h) if (hh.mode == Mode.GrabWorld) hh.grabOffset = world.transform.position - hh.pinch; }
                var g = l ? _h[0] : r ? _h[1] : null;
                if (g != null) world.transform.position = Vector3.Lerp(world.transform.position, g.pinch + g.grabOffset, 0.35f);
            }
        }

        // ------------------------------------------------------------------ point: dwell on a dome to summon
        void Point(int i)
        {
            var h = _h[i];
            if (h.down || !hands.TryGetPoint(i, out var origin, out var dir))
            { h.ray.gameObject.SetActive(false); SetTarget(h, null); return; }

            DomeView target = null; Vector3 hitAt = origin + dir * 3f;
            if (Physics.SphereCast(origin, 0.03f, dir, out var hit, rayRange))
            {
                hitAt = hit.point;
                var dv = hit.collider.GetComponent<DomeView>(); if (dv != null && !dv.Away) target = dv;
            }
            if (target == null && Physics.SphereCast(origin, 0.09f, dir, out hit, rayRange))
            {
                var dv = hit.collider.GetComponent<DomeView>(); if (dv != null && !dv.Away) { target = dv; hitAt = dv.WorldCenter; }
            }
            SetTarget(h, target);

            h.ray.gameObject.SetActive(true);
            h.ray.SetPosition(0, origin); h.ray.SetPosition(1, hitAt);
            var cool = new Color(0.65f, 0.85f, 1f, 0.55f); var hot = new Color(1f, 0.6f, 0.2f, 0.95f);
            var col = Color.Lerp(cool, hot, h.dwell);
            h.ray.startColor = col; h.ray.endColor = new Color(col.r, col.g, col.b, target != null ? col.a : 0f);

            if (target == null) return;
            h.dwell = Mathf.Min(1f, h.dwell + Time.deltaTime / dwellSeconds);
            target.SetHeat(h.dwell);
            if (h.dwell >= 1f)
            {
                h.dwell = 0f;
                if (_loading == 0) StartCoroutine(SummonFrom(target));
                SetTarget(h, null);
            }
        }

        // ------------------------------------------------------------------ flick: swat a bubble home
        void Flick(int i)
        {
            var h = _h[i];
            h.flickCooldown -= Time.deltaTime;
            if (!hands.TryGetJoint(i, UnityEngine.XR.Hands.XRHandJointID.IndexTip, out var tip)) { h.tipValid = false; return; }
            if (!h.tipValid) { h.tipPrev = tip; h.tipValid = true; return; }
            var delta = tip - h.tipPrev; float dt = Mathf.Max(Time.deltaTime, 1e-3f);
            var vel = delta / dt;
            h.tipPrev = tip;
            if (h.down || h.flickCooldown > 0f || vel.magnitude < flickSpeed) return;
            var world = TextWorld.Instance; if (world == null) return;
            // did the fingertip's path this frame pass through (or just graze) a bubble?
            foreach (var b in world.Bubbles)
            {
                if (b.Busy) continue;
                var c = b.transform.position;
                float t = Mathf.Clamp01(Vector3.Dot(c - h.tipPrev + delta, delta) / Mathf.Max(delta.sqrMagnitude, 1e-6f));
                var closest = (tip - delta) + delta * t;
                if (Vector3.Distance(closest, c) < b.Radius + 0.04f)
                {
                    Debug.Log($"[World] flicked {b.Dome.Mind.name} home at {vel.magnitude:F1} m/s");
                    b.Return(vel.normalized);
                    h.flickCooldown = 1f;
                    break;
                }
            }
        }

        void SetTarget(Hand h, DomeView d)
        {
            if (h.target == d) return;
            if (h.target != null) { h.target.SetHeat(0f); h.target.Hover(false); }
            h.target = d; h.dwell = 0f;
            if (d != null) d.Hover(true);
        }

        IEnumerator SummonFrom(DomeView dome)
        {
            var world = TextWorld.Instance;
            if (world == null || !world.Ready) yield break;
            _loading++;
            dome.SetAway(true);
            Debug.Log($"[World] summoning {dome.Mind.name} gen {dome.Mind.gen}");
            byte[] bytes = null;
            yield return world.LoadMind(dome.Mind.id, b => bytes = b);
            if (bytes == null) { dome.SetAway(false); _loading--; yield break; }
            yield return dome.Flash(2);
            MindBubble.Summon(world, dome, bytes);
            _loading--;
        }
    }
}
