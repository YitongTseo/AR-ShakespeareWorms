using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// Adds AR Foundation + Meta OpenXR (passthrough/anchors) + Hand tracking.
//   Unity -batchmode -quit -projectPath <proj> -executeMethod ARPackageInstaller.Add -logFile -
public static class ARPackageInstaller
{
    static readonly string[] Packages =
    {
        "com.unity.xr.arfoundation",     // AR Foundation 6
        "com.unity.xr.meta-openxr",      // Meta Quest passthrough + anchors via OpenXR
        "com.unity.xr.hands",            // hand tracking subsystem
    };

    public static void Add()
    {
        foreach (var pkg in Packages)
        {
            Debug.Log($"[ARPackageInstaller] Adding {pkg} ...");
            AddRequest req = Client.Add(pkg);
            while (!req.IsCompleted) Thread.Sleep(100);
            if (req.Status == StatusCode.Success)
                Debug.Log($"[ARPackageInstaller] Added {req.Result.packageId}");
            else
            {
                Debug.LogError($"[ARPackageInstaller] FAILED {pkg}: {req.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
        Debug.Log("[ARPackageInstaller] All AR packages resolved.");
    }
}
