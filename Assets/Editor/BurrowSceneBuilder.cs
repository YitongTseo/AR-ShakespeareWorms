using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Burrow;
using Speech;

// Builds the passthrough-AR scene from scratch (no prefabs) so it can be created in
// batch mode. Produces Assets/Scenes/Burrow.unity containing:
//   - AR Session (+ AR Input Manager)
//   - XR Origin -> Camera Offset -> Main Camera (clears to alpha 0: passthrough shows)
//   - Hands visualizer
//   - Burrow root: loader, motion, placement, selector
public static class BurrowSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Burrow.unity";

    [MenuItem("Burrow/Build AR Scene")]
    public static string BuildScene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // All shading is hand-rolled in the burrow shaders; keep the scene itself unlit.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.fog = false;

        var sessionGo = new GameObject("AR Session");
        sessionGo.AddComponent<ARSession>();
        sessionGo.AddComponent<ARInputManager>();

        var originGo = new GameObject("XR Origin");
        var origin = originGo.AddComponent<XROrigin>();
        var offsetGo = new GameObject("Camera Offset");
        offsetGo.transform.SetParent(originGo.transform, false);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(offsetGo.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // alpha 0 = the real room shows through
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 60f;
        // HDR / MSAA intermediates destroy the framebuffer alpha Meta composites passthrough against.
        cam.allowHDR = false;
        cam.allowMSAA = false;
        camGo.AddComponent<ARCameraManager>();
        camGo.AddComponent<HeadPoseDriver>();

        origin.Camera = cam;
        origin.CameraFloorOffsetObject = offsetGo;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        var handsGo = new GameObject("Hands");
        var hands = handsGo.AddComponent<MinimalHandVisualizer>();
        hands.jointRadius = 0.005f;

        var root = new GameObject("Burrow");
        root.AddComponent<BurrowLoader>();
        root.AddComponent<BurrowMotion>();
        var placement = root.AddComponent<BurrowPlacement>();
        placement.hands = hands; placement.cameraTransform = camGo.transform;
        var listener = root.AddComponent<Listener>();
        listener.hands = hands; listener.cameraTransform = camGo.transform;
        var interaction = root.AddComponent<Interaction>();
        interaction.hands = hands; interaction.cameraTransform = camGo.transform; interaction.placement = placement; interaction.listener = listener;
        var worldGo = new GameObject("TextWorld");
        worldGo.AddComponent<TextWorld>();
        root.AddComponent<BoundaryFree>();
        root.AddComponent<SpeechEngine>();
        root.AddComponent<VocabExt>();
        var audioGo = new GameObject("AudioListener");
        audioGo.transform.SetParent(camGo.transform, false);
        audioGo.AddComponent<AudioListener>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[BurrowSceneBuilder] Saved {ScenePath}");
        return ScenePath;
    }
}
