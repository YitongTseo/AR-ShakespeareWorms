using System.Collections.Generic;
using UnityEngine;
using WormSim;

namespace Burrow
{
    // The three views the wearer can switch on over the text world, after the ones in the
    // wordswordsworms.org focus viewer (HamletRNAWorld v7/viewer/focus):
    //   XRay   - the connectome inside each body, coloured by neuron class (xray-render.js)
    //   Senses - dashed lines from a worm's nose to every word it can smell (text-canvas.js)
    //   Chemo  - a panel by each bubble: the 12 chemosensory pairs and which words feed them
    //            (chemo-panel.js)
    public static class Overlays
    {
        public static bool XRay = true, Senses = true, Chemo = false;

        public static Color Hsl(float h, float s, float l, float a = 1f)
        {
            float c = (1f - Mathf.Abs(2f * l - 1f)) * s, hp = Mathf.Repeat(h, 360f) / 60f;
            float x = c * (1f - Mathf.Abs(hp % 2f - 1f)), m = l - c * 0.5f;
            float r = 0f, g = 0f, b = 0f;
            if (hp < 1f) { r = c; g = x; } else if (hp < 2f) { r = x; g = c; } else if (hp < 3f) { g = c; b = x; }
            else if (hp < 4f) { g = x; b = c; } else if (hp < 5f) { r = x; b = c; } else { r = c; b = x; }
            return new Color(r + m, g + m, b + m, a);
        }

        // One stable colour per word (chemo-panel.js wordHue), so a word's smell line and its
        // share of every chemosensory bar read as the same thing.
        static readonly Dictionary<string, string> _keys = new Dictionary<string, string>();
        static readonly Dictionary<string, float> _hues = new Dictionary<string, float>();

        public static string WordKey(string word)
        {
            if (!_keys.TryGetValue(word, out var k)) _keys[word] = k = word.ToLowerInvariant();
            return k;
        }

        public static float WordHue(string word)
        {
            if (_hues.TryGetValue(word, out var hue)) return hue;
            int h = 0;
            foreach (char ch in WordKey(word)) h = unchecked((h << 5) - h + ch);
            return _hues[word] = ((h % 360) + 360) % 360;
        }

        // Each chemosensory pair's own hue, spaced round the wheel (chemo-panel.js pcHue).
        public static float PairHue(int pc) => pc * 360f / WmConst.NPc;
    }

    public enum NeuronClass : byte { Inter, Chemo, Sensory, Motor, Muscle }

    // sim/connectome.py's class sets, which the viewer's x-ray colours by. The asset carries the
    // chemosensory pairs and the muscles; the other two sets are by name.
    public static class NeuronClasses
    {
        static readonly HashSet<string> Sensory = new HashSet<string> {
            "PHAL","PHAR","PHBL","PHBR", "ALML","ALMR","AVM","PLML","PLMR","PVM", "PVDL","PVDR","FLPL","FLPR",
            "ADEL","ADER","PDEL","PDER", "CEPDL","CEPDR","CEPVL","CEPVR",
            "IL1DL","IL1DR","IL1L","IL1R","IL1VL","IL1VR", "IL2DL","IL2DR","IL2L","IL2R","IL2VL","IL2VR",
            "OLLL","OLLR", "OLQDL","OLQDR","OLQVL","OLQVR", "BAGL","BAGR","URXL","URXR", "AQR","PQR","BDUL","BDUR" };
        static readonly HashSet<string> HeadMotor = new HashSet<string> {
            "RMEL","RMER","RMED","RMEV", "RMDDL","RMDDR","RMDL","RMDR","RMDVL","RMDVR",
            "SMDDL","SMDDR","SMDVL","SMDVR", "SMBDL","SMBDR","SMBVL","SMBVR", "URADL","URADR","URAVL","URAVR",
            "DVB","AVL","HSNL","HSNR","PDA","PDB" };
        static readonly string[] CordMotor = { "VA", "VB", "VC", "VD", "DA", "DB", "DD", "AS" };

        // ventral cord motor neurons are a class prefix and a number: VA1..VA12, AS1..AS11, ...
        static bool IsCordMotor(string n)
        {
            if (n.Length < 3) return false;
            for (int i = 2; i < n.Length; i++) if (!char.IsDigit(n[i])) return false;
            foreach (var p in CordMotor) if (n.StartsWith(p, System.StringComparison.Ordinal)) return true;
            return false;
        }

        public static NeuronClass[] Classify(WmAsset a)
        {
            var cls = new NeuronClass[a.NNeurons];
            for (int i = 0; i < a.NNeurons; i++)
            {
                var n = a.Neurons[i];
                cls[i] = a.IsMuscle[i] != 0 ? NeuronClass.Muscle : Sensory.Contains(n) ? NeuronClass.Sensory
                       : HeadMotor.Contains(n) || IsCordMotor(n) ? NeuronClass.Motor : NeuronClass.Inter;
            }
            for (int i = 0; i < WmConst.NPc; i++) { cls[a.ChemoPairL[i]] = NeuronClass.Chemo; cls[a.ChemoPairR[i]] = NeuronClass.Chemo; }
            return cls;
        }

        // xray-render.js: hue, resting alpha and dot size per class.
        public static void Look(NeuronClass c, out float hue, out float restAlpha, out float size)
        {
            switch (c)
            {
                case NeuronClass.Chemo:   hue = 195f; restAlpha = 0.45f; size = 1.25f; break;
                case NeuronClass.Sensory: hue = 220f; restAlpha = 0.35f; size = 1.1f; break;
                case NeuronClass.Motor:   hue = 28f;  restAlpha = 0.35f; size = 1.1f; break;
                case NeuronClass.Muscle:  hue = 50f;  restAlpha = 0.2f;  size = 1f; break;
                default:                  hue = 210f; restAlpha = 0.28f; size = 1f; break;
            }
        }
    }
}
