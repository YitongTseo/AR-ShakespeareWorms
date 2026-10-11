using System;
using System.Runtime.InteropServices;

namespace Speech
{
    // P/Invoke surface of tools/speech/wormspeech.c (libwormspeech.so, arm64 Android).
    internal static class SpeechNative
    {
        const string Lib = "wormspeech";

        [DllImport(Lib)] public static extern int ws_version();
        [DllImport(Lib)] public static extern IntPtr ws_tts_create(string model, string tokens, string dataDir, int threads);
        [DllImport(Lib)] public static extern int ws_tts_sample_rate(IntPtr tts);
        [DllImport(Lib)] public static extern int ws_tts_num_speakers(IntPtr tts);
        [DllImport(Lib)] public static extern IntPtr ws_tts_generate(IntPtr tts, string text, int sid, float speed,
                                                                   out IntPtr samples, out int n, out int sampleRate);
        [DllImport(Lib)] public static extern void ws_tts_free_audio(IntPtr audio);
        [DllImport(Lib)] public static extern void ws_tts_destroy(IntPtr tts);
        [DllImport(Lib)] public static extern IntPtr ws_asr_create(string encoder, string decoder, string tokens, int threads);
        [DllImport(Lib)] public static extern IntPtr ws_asr_recognize(IntPtr asr, float[] samples, int n, int sampleRate);
        [DllImport(Lib)] public static extern void ws_asr_destroy(IntPtr asr);
        [DllImport(Lib)] public static extern void ws_free(IntPtr p);

        public static bool Available
        {
            get
            {
                try { return ws_version() == 1; }
                catch (Exception) { return false; }   // DllNotFoundException in the editor
            }
        }
    }
}
