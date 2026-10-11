// server/embedding.py forward pass, minus the 512->11 projection (chemo.c).
//
// bake.py already folded E = ReLU(nomic512 @ W_E + b_E) into the per-word
// E-table, exactly as EmbeddingModel.prime() does once per generation. The same
// table serves both the current word and the eaten history, and dynamic words
// added with WmAsset.AddWord join it seamlessly.
//
// What is left is four small matmuls. An instance owns its scratch buffers so
// the per-brain-tick smell pass allocates nothing.

using System;

namespace WormSim
{
    public sealed class WmChemo
    {
        private readonly double[] _he = new double[WmConst.DHistConcat];
        private readonly double[] _hpos = new double[WmConst.NTags * WmConst.History];
        private readonly double[] _histSummary = new double[WmConst.DEmb];
        private readonly double[] _fuse = new double[WmConst.DFuse];
        private readonly double[] _embOut = new double[WmConst.DEmb];
        private readonly double[] _posIn = new double[WmConst.DPosIn];
        private readonly double[] _posHid = new double[WmConst.DPosHid];
        private readonly double[] _posOut = new double[1];

        // _onehot_pos: an empty history slot contributes all zeros; a real word
        // one-hots its tag. Unknown tags were already folded to "X" by bake.py.
        private static void OneHot(byte tag, double[] dst, int off)
        {
            Array.Clear(dst, off, WmConst.NTags);
            if (tag < WmConst.NTags) dst[off + tag] = 1.0;
        }

        /// <summary>Fills out[0..NPc) for one on-screen word given the worm's
        /// eaten history (token indices, most-recent-first). Returns false when
        /// the word is OOV, matching embed() returning None.</summary>
        public bool Embed(WmAsset a, int tok, int[] recent, int nRecent, double[] outp)
        {
            ushort vi = a.TokVocab[tok];
            if (vi == WmConst.Oov) return false;

            // --- history: zero-padded to HISTORY, most-recent-first ---
            double[] he = _he, hpos = _hpos;
            for (int j = 0; j < WmConst.History; j++)
            {
                int heOff = j * WmConst.DEmb;
                int hpOff = j * WmConst.NTags;
                if (j < nRecent)
                {
                    int htok = recent[j];
                    ushort hvi = a.TokVocab[htok];
                    if (hvi == WmConst.Oov) Array.Clear(he, heOff, WmConst.DEmb);
                    else Array.Copy(a.Etable, hvi * WmConst.DEmb, he, heOff, WmConst.DEmb);
                    // The POS branch tags the actual word even when the
                    // embedding branch treats it as OOV — _norm strips
                    // apostrophes but tag_word does not, so "o'er" tags fine
                    // and embeds as zeros.
                    OneHot(a.TokPos[htok], hpos, hpOff);
                }
                else
                {
                    Array.Clear(he, heOff, WmConst.DEmb);
                    Array.Clear(hpos, hpOff, WmConst.NTags);
                }
            }

            double[] hs = _histSummary;
            WmMath.Affine(he, a.W_Hh, a.b_Hh, WmConst.DHistConcat, WmConst.DEmb, hs);
            for (int i = 0; i < WmConst.DEmb; i++) hs[i] = WmMath.Relu(hs[i]);

            // --- embedding branch ---
            double[] fuse = _fuse;
            Array.Copy(a.Etable, vi * WmConst.DEmb, fuse, 0, WmConst.DEmb);
            Array.Copy(hs, 0, fuse, WmConst.DEmb, WmConst.DEmb);

            double[] embOut = _embOut;
            WmMath.Affine(fuse, a.W_Hf, a.b_Hf, WmConst.DFuse, WmConst.DEmb, embOut);
            for (int i = 0; i < WmConst.DEmb; i++) outp[i] = WmMath.Sigmoid(embOut[i]);

            // --- POS branch ---
            double[] posIn = _posIn;
            OneHot(a.TokPos[tok], posIn, 0);
            Array.Copy(hpos, 0, posIn, WmConst.NTags, WmConst.NTags * WmConst.History);

            double[] posHid = _posHid;
            WmMath.Affine(posIn, a.W_P1, a.b_P1, WmConst.DPosIn, WmConst.DPosHid, posHid);
            for (int i = 0; i < WmConst.DPosHid; i++) posHid[i] = WmMath.Relu(posHid[i]);

            WmMath.Affine(posHid, a.W_P2, a.b_P2, WmConst.DPosHid, 1, _posOut);
            outp[WmConst.DEmb] = WmMath.Sigmoid(_posOut[0]);

            return true;
        }
    }
}
