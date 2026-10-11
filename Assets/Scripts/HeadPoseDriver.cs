using UnityEngine;
using UnityEngine.XR;

// Drives the camera transform directly from the HMD center-eye pose every frame
// (and right before render for low latency). This does not rely on serialized
// Input System actions, which don't survive a headless scene build reliably.
[DefaultExecutionOrder(-1000)]
public class HeadPoseDriver : MonoBehaviour
{
    InputDevice _hmd;

    void OnEnable() { Application.onBeforeRender += Apply; }
    void OnDisable() { Application.onBeforeRender -= Apply; }
    void Update() { Apply(); }

    void Apply()
    {
        if (!_hmd.isValid)
            _hmd = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (!_hmd.isValid) return;

        if (_hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 pos))
            transform.localPosition = pos;
        if (_hmd.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rot))
            transform.localRotation = rot;
    }
}
