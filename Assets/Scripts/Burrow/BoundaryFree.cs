using System.Collections;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Burrow
{
    // Keeps asking the Meta runtime to hide the guardian until it agrees. The feature's own
    // attempt at XR start fails with XR_ERROR_HANDLE_INVALID because the session is not up yet,
    // so we retry from the scene every couple of seconds once passthrough is running.
    public class BoundaryFree : MonoBehaviour
    {
        IEnumerator Start()
        {
            yield return new WaitForSeconds(4f);
            var feature = OpenXRSettings.Instance != null ? OpenXRSettings.Instance.GetFeature<BoundaryVisibilityFeature>() : null;
            if (feature == null || !feature.enabled) { Debug.LogWarning("[Boundary] feature unavailable"); yield break; }
            for (int attempt = 0; attempt < 60; attempt++)
            {
                if (feature.currentVisibility == XrBoundaryVisibility.VisibilitySuppressed) { Debug.Log("[Boundary] suppressed"); yield break; }
                var r = feature.TryRequestBoundaryVisibility(XrBoundaryVisibility.VisibilitySuppressed);
                Debug.Log($"[Boundary] request #{attempt}: {r}");
                yield return new WaitForSeconds(2f);
            }
        }
    }
}
