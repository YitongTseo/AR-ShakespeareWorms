using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// Adds the XR packages needed for a Quest OpenXR build, resolving the latest
// versions compatible with this editor (so nothing is pinned to a guess).
// Run FIRST, before BuildQuest.cs is present, since that script references the
// OpenXR assemblies that this install brings in.
//
//   Unity -batchmode -quit -projectPath <proj> -executeMethod PackageInstaller.AddPackages -logFile -
public static class PackageInstaller
{
    static readonly string[] Packages =
    {
        "com.unity.xr.openxr",          // pulls in com.unity.xr.management + input system
        "com.unity.xr.interaction.toolkit",
    };

    public static void AddPackages()
    {
        foreach (var pkg in Packages)
        {
            Debug.Log($"[PackageInstaller] Adding {pkg} ...");
            AddRequest req = Client.Add(pkg);
            while (!req.IsCompleted)
                Thread.Sleep(100);

            if (req.Status == StatusCode.Success)
                Debug.Log($"[PackageInstaller] Added {req.Result.packageId}");
            else
            {
                Debug.LogError($"[PackageInstaller] FAILED {pkg}: {req.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
        Debug.Log("[PackageInstaller] All packages resolved.");
    }
}
