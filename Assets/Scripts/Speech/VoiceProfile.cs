using UnityEngine;
using Burrow;

namespace Speech
{
    // Which of the ~109 British speakers a mind gets, and how it bends that voice. Voices drift
    // along a lineage (nearby generations sound related) and the two flasks live in different
    // halves of the speaker set; the genome's principal components add the individual tilt.
    public struct VoiceProfile
    {
        // Speakers with weight: the lowest-pitched, clearest of the 109 (tools/speech/rank_speakers.py).
        // Empty = use them all.
        public static int[] Curated = { 102, 71, 76, 32, 13, 97, 104, 29, 47, 23, 20, 10, 24, 106, 75, 96, 26, 38, 7, 93, 12, 28, 50, 55, 61, 64, 60, 95, 98, 25, 62, 70, 16, 82, 79, 105 };

        public int speaker;
        public float speed;    // TTS length scale: >1 faster
        public float pitch;    // AudioSource pitch; the TTS speed compensates so tempo stays put

        public static VoiceProfile For(BMind m, int numSpeakers)
        {
            int half = Mathf.Max(1, numSpeakers / 2);
            uint h = Hash(m.name);
            float pc0 = m.pc != null && m.pc.Length > 0 ? m.pc[0] : 0f;
            float pc1 = m.pc != null && m.pc.Length > 1 ? m.pc[1] : 0f;
            // lineage drift: one speaker step every ~4 generations, plus a per-name nudge
            int drift = (m.gen / 4) * 3 + (int)(h % 3u);
            int speaker = (m.flask % 2) * half + (drift + Mathf.RoundToInt(pc0 * 6f) + 1000 * half) % half;
            // recital: everyone a shade lower and slower than conversation, the genome tilting it
            float pitch = Mathf.Clamp(0.93f + 0.08f * pc1 + 0.03f * (((h >> 8) % 7) - 3f) / 3f, 0.8f, 1.05f);
            float rate = Mathf.Clamp(0.78f + 0.08f * pc0, 0.68f, 0.9f);
            if (Curated != null && Curated.Length > 0)
                speaker = Curated[((m.flask % 2) * (Curated.Length / 2) + drift + Mathf.RoundToInt(pc0 * 3f) + 1000 * Curated.Length) % Curated.Length];
            return new VoiceProfile { speaker = Mathf.Clamp(speaker, 0, numSpeakers - 1), speed = rate / pitch, pitch = pitch };
        }

        static uint Hash(string s)
        {
            uint h = 2166136261u;
            foreach (var c in s) { h ^= c; h *= 16777619u; }
            return h;
        }
    }
}
