using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Speech
{
    // The native engine opens model files by path, and on Android StreamingAssets lives inside
    // the APK, so on first launch every file listed in speech/files.txt is copied to
    // persistentDataPath/speech. A stamp file skips the copy on later launches.
    public static class SpeechModels
    {
        public static string Root => Path.Combine(Application.persistentDataPath, "speech");
        public static string TtsModel => Path.Combine(Root, "tts", "model.onnx");
        public static string TtsTokens => Path.Combine(Root, "tts", "tokens.txt");
        public static string TtsData => Path.Combine(Root, "tts", "espeak-ng-data");
        public static string AsrEncoder => Path.Combine(Root, "asr", "encoder.onnx");
        public static string AsrDecoder => Path.Combine(Root, "asr", "decoder.onnx");
        public static string AsrTokens => Path.Combine(Root, "asr", "tokens.txt");
        const string Stamp = "installed.v1";

        public static bool Installed => File.Exists(Path.Combine(Root, Stamp));

        public static IEnumerator Install(Action<float> progress = null)
        {
            if (Installed) { progress?.Invoke(1f); yield break; }
            string src = Path.Combine(Application.streamingAssetsPath, "speech");
            string list = null;
            yield return Read(Path.Combine(src, "files.txt"), b => list = b == null ? null : System.Text.Encoding.UTF8.GetString(b));
            if (list == null) { Debug.LogError("[Speech] no speech/files.txt in StreamingAssets"); yield break; }
            var files = list.Split('\n');
            int done = 0;
            foreach (var raw in files)
            {
                var rel = raw.Trim();
                if (rel.Length == 0) continue;
                string dst = Path.Combine(Root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                byte[] data = null;
                yield return Read(Path.Combine(src, rel), b => data = b);
                if (data == null) { Debug.LogError($"[Speech] missing {rel}"); yield break; }
                File.WriteAllBytes(dst, data);
                progress?.Invoke(++done / (float)files.Length);
            }
            File.WriteAllText(Path.Combine(Root, Stamp), DateTime.UtcNow.ToString("o"));
            Debug.Log($"[Speech] installed {done} model files to {Root}");
        }

        static IEnumerator Read(string path, Action<byte[]> done)
        {
            if (path.Contains("://"))
            {
                using var req = UnityWebRequest.Get(path);
                yield return req.SendWebRequest();
                done(req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null);
            }
            else done(File.Exists(path) ? File.ReadAllBytes(path) : null);
        }
    }
}
