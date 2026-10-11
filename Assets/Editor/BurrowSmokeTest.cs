using System.IO;
using UnityEditor;
using UnityEngine;
using Burrow;

// Headless sanity check of the bake: parses the manifest the way the app does and builds
// the tunnel mesh. Unity -batchmode -quit -projectPath . -executeMethod BurrowSmokeTest.Run
public static class BurrowSmokeTest
{
    public static void Run()
    {
        string dir = Path.Combine(Application.streamingAssetsPath, "burrow");
        var manifest = JsonUtility.FromJson<BurrowManifest>(File.ReadAllText(Path.Combine(dir, "burrow.json")));
        var poems = JsonUtility.FromJson<PoemFile>(File.ReadAllText(Path.Combine(dir, "poems.json")));
        var mesh = BurrowLoader.BuildMesh(File.ReadAllBytes(Path.Combine(dir, "burrow_mesh.bytes")));
        int minds = 0, domes = 0;
        foreach (var n in manifest.nodes) if (n.kind == "mind") { domes++; if (n.dome != null && n.dome.Length == 3 && n.dome_r > 0) minds++; }
        Debug.Log($"[SmokeTest] version {manifest.version} seed {manifest.seed} ceiling {manifest.@params.ceiling} sculpt_top {manifest.@params.sculpt_top}");
        Debug.Log($"[SmokeTest] nodes {manifest.nodes.Length}, mind nodes {domes} (with domes {minds}), runs {manifest.runs.Length}, minds {manifest.minds.Length}, poems {poems.poems.Length}");
        Debug.Log($"[SmokeTest] run0 a={manifest.runs[0].a} b={manifest.runs[0].b} kind={manifest.runs[0].kind} spine floats {manifest.runs[0].sp.Length}");
        Debug.Log($"[SmokeTest] mesh {mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris, bounds {mesh.bounds}");
        bool ok = manifest.@params.ceiling > 0 && minds == domes && minds > 100 && mesh.vertexCount > 1000 && poems.poems.Length == manifest.minds.Length;
        Debug.Log(ok ? "[SmokeTest] OK" : "[SmokeTest] FAILED");
        if (!ok) EditorApplication.Exit(1);
    }
}
