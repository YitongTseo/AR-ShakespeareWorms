using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor.XR.Management;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;

// Configures the project for passthrough-AR on Quest and builds the Shakespeare Worms APK.
//   Unity -batchmode -quit -projectPath <p> -executeMethod ARBuild.Build -logFile -
public static class ARBuild
{
    const string ApkPath = "Build/ShakespeareWorms.apk";

    // OpenXR features to enable for a passthrough + hand-tracking MR app on Quest.
    static readonly string[] EnableFeatures =
    {
        "MetaQuestFeature",                       // base Quest support + manifest
        "OculusTouchControllerProfile",           // controller fallback
        "MetaQuestTouchPlusControllerProfile",
        "MetaQuestTouchProControllerProfile",
        // Meta OpenXR (AR Foundation) base + AR features
        "MetaOpenXRFeature",
        "OpenXRLifeCycleFeature",
        "DisplayUtilitiesFeature",
        "ARSessionFeature",                       // AR Foundation session (Meta)
        "ARCameraFeature",                        // passthrough (drawn as a composition layer, so...)
        "OpenXRCompositionLayersFeature",         // ...this must be on too, or no passthrough layer is ever created
        "ARAnchorFeature",                        // anchors (colocation groundwork)
        "HandTracking",                           // XR Hands
        "BoundaryVisibilityFeature",              // walk anywhere: the guardian is suppressed in passthrough
    };

    static readonly string[] AlwaysIncludeShaders =
    {
        "Burrow/Tunnel", "Burrow/Dome", "Burrow/DomeInner", "Burrow/Panel",
        "Burrow/Unlit", "Burrow/Text", "Burrow/VertexColor", "Burrow/Glow", "Burrow/Translucent",       // hand joints, prompts, pop-up text (no built-ins: see IncludeShaders)
    };

    public static void Build()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        ConfigurePlayer();
        ConfigureXR();
        IncludeShaders();

        string scene = BurrowSceneBuilder.BuildScene();   // regenerate scene from code

        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { scene },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"[ARBuild] Result: {s.result}, size: {s.totalSize}, out: {s.outputPath}");
        if (s.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;   // passthrough needs 12L
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.ternalbiota.shakespeareworms");
        PlayerSettings.productName = "Shakespeare Worms";
        PlayerSettings.companyName = "TernalBiota";
        // Passthrough needs no forced VR splash covering it.
        PlayerSettings.SplashScreen.showUnityLogo = false;

        // Passthrough is composited behind our layer using the framebuffer alpha: Android
        // discards it unless "Render Over Native UI" (preserveFramebufferAlpha) is on.
        PlayerSettings.preserveFramebufferAlpha = true;
        // Vulkan only (Meta-recommended) and no MSAA, so passthrough alpha is preserved.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        QualitySettings.antiAliasing = 0;
    }

    static void ConfigureXR()
    {
        var group = BuildTargetGroup.Android;
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
            out XRGeneralSettingsPerBuildTarget perBT);
        if (perBT == null)
        {
            perBT = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perBT, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBT, true);
        }
        perBT.CreateDefaultManagerSettingsForBuildTarget(group);
        var settings = perBT.SettingsForBuildTarget(group);

        bool assigned = XRPackageMetadataStore.AssignLoader(settings.Manager, typeof(OpenXRLoader).FullName, group);
        Debug.Log($"[ARBuild] OpenXR loader assigned: {assigned}");

        FeatureHelpers.RefreshFeatures(group);
        var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        var wanted = new HashSet<string>(EnableFeatures);
        foreach (var feature in oxr.GetFeatures())
        {
            string n = feature.GetType().Name;
            if (wanted.Contains(n))
            {
                feature.enabled = true;
                Debug.Log($"[ARBuild] enabled feature: {n}");
                if (n == "BoundaryVisibilityFeature")
                {
                    // the feature suppresses the boundary itself at XR init when this flag is set
                    var so = new SerializedObject(feature);
                    var prop = so.FindProperty("m_SuppressVisibility");
                    if (prop != null) { prop.boolValue = true; so.ApplyModifiedProperties(); Debug.Log("[ARBuild] boundary visibility: suppress"); }
                }
            }
        }
        var enabledNames = oxr.GetFeatures().Where(f => f.enabled).Select(f => f.GetType().Name);
        Debug.Log($"[ARBuild] active features: {string.Join(", ", enabledNames)}");

        EditorUtility.SetDirty(perBT);
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(settings.Manager);
        EditorUtility.SetDirty(oxr);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void IncludeShaders()
    {
        var gsObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
        if (gsObj == null) { Debug.LogWarning("[ARBuild] no GraphicsSettings.asset"); return; }
        var so = new SerializedObject(gsObj);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");

        // Only our own shaders go in this list. Dangling entries and built-in shaders here
        // have both made the unity_builtin_extra write assert ('m_LockCount == 0') on this
        // machine, so anything that is not a project shader is pruned.
        for (int i = arr.arraySize - 1; i >= 0; i--)
        {
            var obj = arr.GetArrayElementAtIndex(i).objectReferenceValue;
            string path = obj != null ? AssetDatabase.GetAssetPath(obj) : "";
            if (obj == null || !path.StartsWith("Assets/"))
            { arr.DeleteArrayElementAtIndex(i); Debug.Log($"[ARBuild] pruned always-include shader #{i} ({path})"); }
        }
        var existing = new HashSet<Object>();
        for (int i = 0; i < arr.arraySize; i++)
            existing.Add(arr.GetArrayElementAtIndex(i).objectReferenceValue);

        foreach (var name in AlwaysIncludeShaders)
        {
            var sh = Shader.Find(name);
            if (sh == null) { Debug.LogWarning($"[ARBuild] shader not found: {name}"); continue; }
            if (existing.Contains(sh)) continue;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            Debug.Log($"[ARBuild] always-include shader: {name}");
        }
        so.ApplyModifiedProperties();
    }
}
