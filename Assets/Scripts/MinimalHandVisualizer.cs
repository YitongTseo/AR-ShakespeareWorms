using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

// Lightweight hand visualizer: subscribes to the XRHandSubsystem and draws a
// small glowing sphere at every tracked joint on both hands. Also exposes a
// simple pinch value per hand for interaction code to consume. No prefabs — it
// generates everything at runtime so it survives a headless build.
public class MinimalHandVisualizer : MonoBehaviour
{
    [Range(0.004f, 0.02f)] public float jointRadius = 0.009f;
    public Color jointColor = new Color(0.7f, 0.95f, 1f, 1f);

    XRHandSubsystem _subsystem;
    readonly Transform[] _left = new Transform[XRHandJointID.EndMarker.ToIndex()];
    readonly Transform[] _right = new Transform[XRHandJointID.EndMarker.ToIndex()];
    Material _mat;

    // Pinch (thumb-tip to index-tip distance, metres) exposed for other systems.
    public float LeftPinch { get; private set; } = 1f;
    public float RightPinch { get; private set; } = 1f;
    public bool LeftTracked { get; private set; }
    public bool RightTracked { get; private set; }
    public Vector3 LeftPinchPos { get; private set; }
    public Vector3 RightPinchPos { get; private set; }

    // Any joint's current world position (false when that hand or joint is not tracked).
    public bool TryGetJoint(int hand, XRHandJointID id, out Vector3 pos)
    {
        var joints = hand == 0 ? _left : _right;
        bool tracked = hand == 0 ? LeftTracked : RightTracked;
        var t = joints[id.ToIndex()];
        if (!tracked || t == null || !t.gameObject.activeSelf) { pos = Vector3.zero; return false; }
        pos = t.position; return true;
    }

    // Index finger extended, the others curled: a point. Returns the ray origin (index tip)
    // and direction (along the finger), already low-pass filtered against tracking jitter.
    readonly Vector3[] _dirFilt = new Vector3[2];
    public bool TryGetPoint(int hand, out Vector3 origin, out Vector3 dir)
    {
        origin = dir = Vector3.zero;
        if (!TryGetJoint(hand, XRHandJointID.Wrist, out var wrist) || !TryGetJoint(hand, XRHandJointID.IndexTip, out var tip)
            || !TryGetJoint(hand, XRHandJointID.IndexIntermediate, out var mid) || !TryGetJoint(hand, XRHandJointID.MiddleTip, out var mtip)
            || !TryGetJoint(hand, XRHandJointID.RingTip, out var rtip) || !TryGetJoint(hand, XRHandJointID.IndexProximal, out var prox))
            return false;
        float indexLen = Vector3.Distance(tip, wrist), middleLen = Vector3.Distance(mtip, wrist), ringLen = Vector3.Distance(rtip, wrist);
        if (indexLen < 0.12f || middleLen > indexLen - 0.035f || ringLen > indexLen - 0.035f) return false;
        var raw = (tip - prox).normalized;
        _dirFilt[hand] = _dirFilt[hand] == Vector3.zero ? raw : Vector3.Slerp(_dirFilt[hand], raw, 0.18f);
        origin = tip; dir = _dirFilt[hand];
        return true;
    }

    void OnEnable() => TryGetSubsystem();

    void TryGetSubsystem()
    {
        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        _subsystem = loader?.GetLoadedSubsystem<XRHandSubsystem>();
        if (_subsystem != null)
            _subsystem.updatedHands += OnUpdatedHands;
    }

    void Update()
    {
        if (_subsystem == null) TryGetSubsystem();
    }

    void OnDisable()
    {
        if (_subsystem != null) _subsystem.updatedHands -= OnUpdatedHands;
    }

    void OnUpdatedHands(XRHandSubsystem subsystem,
        XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType updateType)
    {
        if (updateType != XRHandSubsystem.UpdateType.Dynamic) return;
        try
        {
            UpdateHand(subsystem.leftHand, _left, isLeft: true);
            UpdateHand(subsystem.rightHand, _right, isLeft: false);
        }
        catch (System.Exception e)
        {
            // Never let a per-frame hiccup spam the log or stall the pipeline.
            if (!_warned) { Debug.LogWarning($"[Hands] {e.Message}"); _warned = true; }
        }
    }
    bool _warned;

    void UpdateHand(XRHand hand, Transform[] joints, bool isLeft)
    {
        bool tracked = hand.isTracked;
        if (isLeft) LeftTracked = tracked; else RightTracked = tracked;

        Vector3 thumb = Vector3.zero, index = Vector3.zero;
        bool haveThumb = false, haveIndex = false;

        for (int i = XRHandJointID.BeginMarker.ToIndex(); i < XRHandJointID.EndMarker.ToIndex(); i++)
        {
            var t = EnsureJoint(joints, i);
            var id = XRHandJointIDUtility.FromIndex(i);
            if (tracked && hand.GetJoint(id).TryGetPose(out Pose pose))
            {
                t.gameObject.SetActive(true);
                t.SetPositionAndRotation(pose.position, pose.rotation);
                if (id == XRHandJointID.ThumbTip) { thumb = pose.position; haveThumb = true; }
                if (id == XRHandJointID.IndexTip) { index = pose.position; haveIndex = true; }
            }
            else t.gameObject.SetActive(false);
        }

        if (haveThumb && haveIndex)
        {
            float pinch = Vector3.Distance(thumb, index);
            Vector3 mid = (thumb + index) * 0.5f;
            if (isLeft) { LeftPinch = pinch; LeftPinchPos = mid; }
            else { RightPinch = pinch; RightPinchPos = mid; }
        }
    }

    Transform EnsureJoint(Transform[] joints, int i)
    {
        if (joints[i] != null) return joints[i];
        if (_mat == null)
        {
            var sh = Shader.Find("Burrow/Unlit") ?? Shader.Find("Unlit/Color");
            if (sh != null) _mat = new Material(sh) { color = jointColor };
        }
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"joint_{i}";
        Destroy(go.GetComponent<Collider>());
        if (_mat != null) go.GetComponent<Renderer>().sharedMaterial = _mat;
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * (jointRadius * 2f);
        joints[i] = go.transform;
        return go.transform;
    }
}
