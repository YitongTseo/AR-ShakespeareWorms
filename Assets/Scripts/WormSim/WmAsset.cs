// Parser for the .hwrm asset produced by HamletWorms/tools/bake.py (and by
// tools/worms/bake_mind.py here). Unlike the zero-copy C parser this copies
// every table out of the byte[] into typed managed arrays, which is what makes
// the dynamic vocabulary possible: AddWord/AddToken append to the vocab,
// E-table and token tables so the wearer's spoken words can be scrolled,
// smelled and eaten exactly like Hamlet's.
//
// Layout (bake.py): "HWRM", u32 version=1, u32 n_sections, u32 0, then a TOC of
// 16-byte entries (8-char space-padded tag, u32 offset, u32 length), then the
// 16-byte-aligned section payloads. All alignment inside a section is relative
// to the start of that section.

using System;
using System.Collections.Generic;
using System.Text;

namespace WormSim
{
    public sealed class WmAsset
    {
        private const uint Magic = 0x4D525748u;  // "HWRM" little-endian

        public string Meta = "";

        // --- vocabulary + E-table (grow with AddWord) ---------------------------
        public int NVocab;
        public int NBakedVocab;
        public string[] Vocab;      // _norm'd lookup keys (capacity >= NVocab)
        public double[] Etable;     // [NVocab][DEmb], flat row-major (capacity >= NVocab*DEmb)

        // --- shared nets ---------------------------------------------------------
        public double[] W_Hh, b_Hh, W_Hf, b_Hf, W_P1, b_P1, W_P2, b_P2;

        // --- connectome ----------------------------------------------------------
        public int NNeurons;
        public string[] Neurons;
        public byte[] IsMuscle;
        public int[] RowStart;      // [NNeurons + 1]
        public ushort[] SynCol;
        public double[] SynW;
        public int NEdges;
        public float[] NeuronAxial;    // null when the asset has no NEURONXY
        public float[] NeuronLateral;
        public int NPresyn;
        public ushort[] PresynIdx;     // weights.json key order, for RandExcite
        public int NMuscleVisits;
        public ushort[] MuscleIdx;
        public byte[] MuscleSide;      // 0 = left, 1 = right, 2 = neither
        public ushort[] ChemoPairL = new ushort[WmConst.NPc];
        public ushort[] ChemoPairR = new ushort[WmConst.NPc];
        public ushort[] HungerIdx, NoseIdx, FoodIdx;
        public int MvulvaIdx = -1;

        // --- corpus (token tables grow with AddToken) ----------------------------
        public int NSentences;
        public int NTokens;         // baked + dynamic
        public int NBakedTokens;
        public int[] SentStart;     // [NSentences + 1]
        public ushort[] TokVocab;   // Oov when the word has no nomic vector
        public byte[] TokPos;
        public int[] TokLen;        // CODE POINTS, not bytes (the em dash is 3 bytes)
        public string[] TokText;    // display form, original capitalisation
        public byte[] SentEdible;

        public uint Seed;

        private Dictionary<string, int> _vocabIndex;

        // ---------------------------------------------------------------------
        // Parsing
        // ---------------------------------------------------------------------
        private sealed class Cursor
        {
            public byte[] D;
            public int Base, Off, Size;
            public bool Ok = true;

            public int Take(int n)
            {
                if (!Ok || Off + n > Size) { Ok = false; return -1; }
                int p = Base + Off;
                Off += n;
                return p;
            }
            public uint U32()
            {
                int p = Take(4);
                return p < 0 ? 0u : BitConverter.ToUInt32(D, p);
            }
            public void Align(int n) { while (Off % n != 0) Off++; }
        }

        private static uint RdU32(byte[] d, int p) { return BitConverter.ToUInt32(d, p); }

        private static bool FindSection(byte[] d, int nSections, string tag, Cursor c)
        {
            for (int i = 0; i < nSections; i++)
            {
                int e = 16 + 16 * i;
                bool match = true;
                for (int k = 0; k < 8; k++)
                {
                    byte want = k < tag.Length ? (byte)tag[k] : (byte)' ';
                    if (d[e + k] != want) { match = false; break; }
                }
                if (!match) continue;
                uint off = RdU32(d, e + 8), len = RdU32(d, e + 12);
                if ((long)off + len > d.Length) return false;
                c.D = d; c.Base = (int)off; c.Off = 0; c.Size = (int)len; c.Ok = true;
                return true;
            }
            return false;
        }

        private static string[] TakeStrTab(Cursor c)
        {
            int count = (int)c.U32();
            int blobLen = (int)c.U32();
            int offp = c.Take(4 * (count + 1));
            int blobp = c.Take(blobLen);
            c.Align(4);
            if (offp < 0 || blobp < 0) { c.Ok = false; return new string[0]; }
            var outp = new string[count];
            for (int i = 0; i < count; i++)
            {
                int s = (int)RdU32(c.D, offp + 4 * i), e = (int)RdU32(c.D, offp + 4 * (i + 1));
                outp[i] = Encoding.UTF8.GetString(c.D, blobp + s, e - s);
            }
            return outp;
        }

        private static double[] TakeF64(Cursor c, int n)
        {
            int p = c.Take(8 * n);
            if (p < 0) return null;
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = BitConverter.ToDouble(c.D, p + 8 * i);
            return a;
        }
        private static float[] TakeF32(Cursor c, int n)
        {
            int p = c.Take(4 * n);
            if (p < 0) return null;
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = BitConverter.ToSingle(c.D, p + 4 * i);
            return a;
        }
        private static int[] TakeU32Arr(Cursor c, int n)
        {
            int p = c.Take(4 * n);
            if (p < 0) return null;
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = (int)BitConverter.ToUInt32(c.D, p + 4 * i);
            return a;
        }
        private static ushort[] TakeU16Arr(Cursor c, int n)
        {
            int p = c.Take(2 * n);
            if (p < 0) return null;
            var a = new ushort[n];
            for (int i = 0; i < n; i++) a[i] = BitConverter.ToUInt16(c.D, p + 2 * i);
            return a;
        }
        private static byte[] TakeU8Arr(Cursor c, int n)
        {
            int p = c.Take(n);
            if (p < 0) return null;
            var a = new byte[n];
            Array.Copy(c.D, p, a, 0, n);
            return a;
        }

        /// <summary>Parses a .hwrm image. Throws FormatException on a malformed file.</summary>
        public static WmAsset Parse(byte[] data)
        {
            var a = new WmAsset();
            if (!a.Open(data)) throw new FormatException("bad .hwrm asset");
            return a;
        }

        /// <summary>Parses a mind split by tools/worms/split_shared.py: the shared tables
        /// (VOCAB, ETABLE, NET, CORPUS) come from shared.hwrm, everything else from the
        /// mind's own small file. Sections in `mind` win when both carry the same tag.</summary>
        public static WmAsset Parse(byte[] shared, byte[] mind)
        {
            return Parse(Merge(shared, mind));
        }

        /// <summary>Concatenates the section tables of two .hwrm files into one valid file.</summary>
        public static byte[] Merge(byte[] shared, byte[] mind)
        {
            var secs = new List<KeyValuePair<string, byte[]>>();
            var have = new HashSet<string>();
            foreach (var kv in Sections(mind)) { secs.Add(kv); have.Add(kv.Key); }
            foreach (var kv in Sections(shared)) if (!have.Contains(kv.Key)) secs.Add(kv);
            int headLen = 16, tocLen = 16 * secs.Count;
            int bodyStart = (headLen + tocLen + 15) / 16 * 16;
            int total = bodyStart;
            foreach (var kv in secs) total += (kv.Value.Length + 15) / 16 * 16;
            var o = new byte[total];
            WrU32(o, 0, Magic); WrU32(o, 4, 1); WrU32(o, 8, (uint)secs.Count); WrU32(o, 12, 0);
            int cur = bodyStart;
            for (int i = 0; i < secs.Count; i++)
            {
                var tag = System.Text.Encoding.ASCII.GetBytes(secs[i].Key.PadRight(8));
                Array.Copy(tag, 0, o, 16 + 16 * i, 8);
                WrU32(o, 16 + 16 * i + 8, (uint)cur); WrU32(o, 16 + 16 * i + 12, (uint)secs[i].Value.Length);
                Array.Copy(secs[i].Value, 0, o, cur, secs[i].Value.Length);
                cur += (secs[i].Value.Length + 15) / 16 * 16;
            }
            return o;
        }

        private static List<KeyValuePair<string, byte[]>> Sections(byte[] d)
        {
            var list = new List<KeyValuePair<string, byte[]>>();
            if (d == null || d.Length < 16 || RdU32(d, 0) != Magic) throw new FormatException("bad .hwrm asset");
            int n = (int)RdU32(d, 8);
            for (int i = 0; i < n; i++)
            {
                string tag = System.Text.Encoding.ASCII.GetString(d, 16 + 16 * i, 8).TrimEnd();
                int off = (int)RdU32(d, 16 + 16 * i + 8), size = (int)RdU32(d, 16 + 16 * i + 12);
                var b = new byte[size]; Array.Copy(d, off, b, 0, size);
                list.Add(new KeyValuePair<string, byte[]>(tag, b));
            }
            return list;
        }

        private static void WrU32(byte[] d, int off, uint v)
        {
            d[off] = (byte)v; d[off + 1] = (byte)(v >> 8); d[off + 2] = (byte)(v >> 16); d[off + 3] = (byte)(v >> 24);
        }

        public bool Open(byte[] d)
        {
            if (!BitConverter.IsLittleEndian) return false;
            if (d.Length < 16 || RdU32(d, 0) != Magic) return false;
            if (RdU32(d, 4) != 1) return false;
            int nSections = (int)RdU32(d, 8);
            if (d.Length < 16 + 16 * nSections) return false;

            var c = new Cursor();

            if (FindSection(d, nSections, "META", c))
            {
                var t = TakeStrTab(c);
                Meta = t.Length > 0 ? t[0] : "";
            }

            if (!FindSection(d, nSections, "VOCAB", c)) return false;
            Vocab = TakeStrTab(c);
            NVocab = NBakedVocab = Vocab.Length;
            if (!c.Ok) return false;

            if (!FindSection(d, nSections, "ETABLE", c)) return false;
            {
                int n = (int)c.U32(), dim = (int)c.U32();
                if (n != NVocab || dim != WmConst.DEmb) return false;
                Etable = TakeF64(c, n * dim);
                if (!c.Ok) return false;
            }

            if (!FindSection(d, nSections, "NET", c)) return false;
            {
                // Same order bake.py writes them in.
                W_Hh = TakeF64(c, WmConst.DHistConcat * WmConst.DEmb);
                b_Hh = TakeF64(c, WmConst.DEmb);
                W_Hf = TakeF64(c, WmConst.DFuse * WmConst.DEmb);
                b_Hf = TakeF64(c, WmConst.DEmb);
                W_P1 = TakeF64(c, WmConst.DPosIn * WmConst.DPosHid);
                b_P1 = TakeF64(c, WmConst.DPosHid);
                W_P2 = TakeF64(c, WmConst.DPosHid * 1);
                b_P2 = TakeF64(c, 1);
                if (!c.Ok) return false;
            }

            if (!FindSection(d, nSections, "NEURONS", c)) return false;
            Neurons = TakeStrTab(c);
            NNeurons = Neurons.Length;
            IsMuscle = TakeU8Arr(c, NNeurons);
            if (!c.Ok) return false;
            MvulvaIdx = -1;
            for (int i = 0; i < NNeurons; i++)
                if (Neurons[i] == "MVULVA") { MvulvaIdx = i; break; }

            if (!FindSection(d, nSections, "SYNAPSE", c)) return false;
            {
                int nn = (int)c.U32();
                NEdges = (int)c.U32();
                if (nn != NNeurons) return false;
                RowStart = TakeU32Arr(c, nn + 1);
                SynCol = TakeU16Arr(c, NEdges);
                c.Align(8);
                SynW = TakeF64(c, NEdges);
                if (!c.Ok) return false;
            }

            if (!FindSection(d, nSections, "MUSCLES", c)) return false;
            NMuscleVisits = (int)c.U32();
            MuscleIdx = TakeU16Arr(c, NMuscleVisits);
            MuscleSide = TakeU8Arr(c, NMuscleVisits);
            if (!c.Ok) return false;

            NeuronAxial = null; NeuronLateral = null;
            if (FindSection(d, nSections, "NEURONXY", c))
            {
                NeuronAxial = TakeF32(c, NNeurons);
                NeuronLateral = TakeF32(c, NNeurons);
                if (!c.Ok) { NeuronAxial = null; NeuronLateral = null; }
            }

            if (!FindSection(d, nSections, "PRESYN", c)) return false;
            NPresyn = (int)c.U32();
            PresynIdx = TakeU16Arr(c, NPresyn);
            if (!c.Ok) return false;

            if (!FindSection(d, nSections, "CHEMOMAP", c)) return false;
            for (int i = 0; i < WmConst.NPc; i++)
            {
                int p = c.Take(4);
                if (p < 0) return false;
                ChemoPairL[i] = BitConverter.ToUInt16(d, p);
                ChemoPairR[i] = BitConverter.ToUInt16(d, p + 2);
            }

            if (!FindSection(d, nSections, "STIMSETS", c)) return false;
            {
                int nh = (int)c.U32(); HungerIdx = TakeU16Arr(c, nh);
                int nn = (int)c.U32(); NoseIdx = TakeU16Arr(c, nn);
                int nf = (int)c.U32(); FoodIdx = TakeU16Arr(c, nf);
                if (!c.Ok) return false;
            }

            if (!FindSection(d, nSections, "CORPUS", c)) return false;
            {
                NSentences = (int)c.U32();
                int nt = (int)c.U32();
                SentStart = TakeU32Arr(c, NSentences + 1);
                TokVocab = TakeU16Arr(c, nt);
                TokPos = TakeU8Arr(c, nt);
                byte[] lens = TakeU8Arr(c, nt);
                SentEdible = TakeU8Arr(c, NSentences);
                c.Align(8);
                TokText = TakeStrTab(c);
                if (!c.Ok || TokText.Length != nt) return false;
                TokLen = new int[nt];
                for (int i = 0; i < nt; i++) TokLen[i] = lens[i];
                NTokens = NBakedTokens = nt;
            }

            Seed = 0;
            if (FindSection(d, nSections, "SEED", c)) Seed = c.U32();

            _vocabIndex = null;
            return true;
        }

        // ---------------------------------------------------------------------
        // Dynamic vocabulary (extension beyond the C firmware)
        // ---------------------------------------------------------------------

        /// <summary>server/embedding.py _norm: the lookup key for a surface word —
        /// lowercased, surrounding apostrophes then whitespace stripped.</summary>
        public static string NormWord(string word)
        {
            if (string.IsNullOrEmpty(word)) return "";
            return word.ToLowerInvariant().Trim('\'').Trim();
        }

        /// <summary>Row of the E-table for a (surface or normalised) word, or -1
        /// when it is not in the vocabulary. Dynamic words are included.</summary>
        public int FindVocab(string word)
        {
            if (_vocabIndex == null)
            {
                _vocabIndex = new Dictionary<string, int>(NVocab * 2);
                for (int i = 0; i < NVocab; i++)
                    if (!_vocabIndex.ContainsKey(Vocab[i])) _vocabIndex[Vocab[i]] = i;
            }
            int idx;
            string key = NormWord(word);
            return _vocabIndex.TryGetValue(key, out idx) ? idx : -1;
        }

        private static int CodePointCount(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
                n++;
            }
            return n;
        }

        /// <summary>Appends a token (a scrollable/edible word slot) that refers to
        /// an existing vocabulary row (or Oov / -1 for a word with no taste), and
        /// returns its token index for WmScroller.SpawnLine. posTag is the
        /// universal-tag index (embedding.POS_TAGS order: NOUN VERB ADJ ADV DET
        /// ADP PRON PRT CONJ NUM X .); out-of-range tags fold to X like bake.py.</summary>
        public int AddToken(string text, int vocabIdx, byte posTag)
        {
            if (text == null) text = "";
            if (posTag >= WmConst.NTags) posTag = 10;  // "X"
            ushort vi = (vocabIdx < 0 || vocabIdx >= NVocab) ? WmConst.Oov : (ushort)vocabIdx;
            if (NTokens == TokVocab.Length)
            {
                int cap = Math.Max(16, NTokens * 2);
                Array.Resize(ref TokVocab, cap);
                Array.Resize(ref TokPos, cap);
                Array.Resize(ref TokLen, cap);
                Array.Resize(ref TokText, cap);
            }
            int t = NTokens++;
            TokVocab[t] = vi;
            TokPos[t] = posTag;
            TokLen[t] = CodePointCount(text);
            TokText[t] = text;
            return t;
        }

        /// <summary>Appends a NEW vocabulary word with its 11-dim E-table row
        /// (ReLU(nomic512 @ W_E + b_E), computed offline or by a nearest-known-
        /// taste lookup), plus a token for it, and returns the token index. Pass
        /// a null row for a word with no taste (it scrolls but cannot be
        /// smelled, like the baked OOV tokens). The baked tables are untouched;
        /// nothing in the baked corpus can refer to the new rows.</summary>
        public int AddWord(string text, double[] etableRow, byte posTag)
        {
            int vi = -1;
            if (etableRow != null)
            {
                if (etableRow.Length != WmConst.DEmb)
                    throw new ArgumentException("etableRow must have " + WmConst.DEmb + " entries");
                if (NVocab >= WmConst.Oov)
                    throw new InvalidOperationException("vocabulary is full (ushort indices)");
                if (NVocab == Vocab.Length)
                {
                    int cap = Math.Max(16, NVocab * 2);
                    Array.Resize(ref Vocab, cap);
                    Array.Resize(ref Etable, cap * WmConst.DEmb);
                }
                vi = NVocab++;
                Vocab[vi] = NormWord(text);
                Array.Copy(etableRow, 0, Etable, vi * WmConst.DEmb, WmConst.DEmb);
                if (_vocabIndex != null && !_vocabIndex.ContainsKey(Vocab[vi])) _vocabIndex[Vocab[vi]] = vi;
            }
            return AddToken(text, vi, posTag);
        }

        /// <summary>Convenience for words the worm already knows: looks the word
        /// up in the vocabulary (baked or added) and appends a token for it.
        /// Unknown words get an OOV token — they scroll but have no taste.</summary>
        public int AddKnownWordToken(string text, byte posTag)
        {
            return AddToken(text, FindVocab(text), posTag);
        }
    }
}
