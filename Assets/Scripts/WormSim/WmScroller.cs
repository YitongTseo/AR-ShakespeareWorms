// sim/text_scroller.py — Hamlet lines drifting upward (scroller.c).
//
// The Python keeps a `_dead` set keyed by (line_id, word_idx) that outlives the
// line; here the eaten flag lives on the word itself, which is equivalent
// because line_ids are never reused and alive_words() only ever walks _active.
//
// Beyond the firmware: SpawnLine injects a line of arbitrary tokens (the
// wearer's speech) with the same geometry rules as a corpus spawn. It does not
// touch sent_idx / next_spawn, so the baked schedule is unchanged.

using System.Collections.Generic;

namespace WormSim
{
    public sealed class WmWord
    {
        public int Tok;        // index into WmAsset.Tok* tables
        public double X, Y;    // world units; X is the centre of the word
        public bool Eaten;
    }

    public sealed class WmLine
    {
        public int LineId;
        public int SentIdx;    // -1 for an injected line
        public bool Edible;
        public WmWord[] Words;
        public int NWords { get { return Words.Length; } }
    }

    public sealed class WmScroller
    {
        public readonly WmAsset Asset;
        public readonly List<WmLine> Lines = new List<WmLine>();
        public int SentIdx;
        public int LineId;
        public double Elapsed, NextSpawn;
        public bool Loop;

        public WmScroller(WmAsset a, bool loop)
        {
            Asset = a;
            Loop = loop;
        }

        public bool Exhausted
        {
            get { return !Loop && SentIdx >= Asset.NSentences && Lines.Count == 0; }
        }

        private void Spawn()
        {
            WmAsset a = Asset;
            int si;
            if (Loop)
            {
                si = SentIdx % a.NSentences;
            }
            else
            {
                if (SentIdx >= a.NSentences) return;
                si = SentIdx;
            }
            SentIdx++;

            int start = a.SentStart[si], end = a.SentStart[si + 1];
            int n = end - start;
            if (n > WmConst.MaxLineWords) n = WmConst.MaxLineWords;
            if (Lines.Count >= WmConst.MaxActiveLines) return;  // firmware slot cap (never hit by the baked corpus)

            var toks = new int[n];
            for (int i = 0; i < n; i++) toks[i] = start + i;
            Lines.Add(Layout(toks, si, a.SentEdible[si] != 0));
        }

        private WmLine Layout(int[] toks, int sentIdx, bool edible)
        {
            WmAsset a = Asset;
            int n = toks.Length;
            var L = new WmLine();
            L.LineId = LineId++;
            L.SentIdx = sentIdx;
            L.Edible = edible;
            L.Words = new WmWord[n];

            // total_w = sum(len(t) * CHAR_W) + WORD_GAP * (n - 1), summed left to
            // right. len() is code points; the asset's TokLen carries that.
            double totalW = 0.0;
            for (int i = 0; i < n; i++) totalW += (double)a.TokLen[toks[i]] * WmConst.CharW;
            totalW += WmConst.WordGap * (double)(n - 1);

            double x = WmConst.CenterX - totalW / 2.0;
            for (int i = 0; i < n; i++)
            {
                int tok = toks[i];
                double cw = (double)a.TokLen[tok] * WmConst.CharW;
                var w = new WmWord();
                w.Tok = tok;
                w.X = x + cw / 2.0;  // centre of the word
                w.Y = WmConst.SpawnY;
                w.Eaten = false;
                L.Words[i] = w;
                x += cw + WmConst.WordGap;
            }
            return L;
        }

        /// <summary>Inserts a new scrolling line of the given tokens (indices
        /// from WmAsset.AddWord/AddToken, or baked token indices) at the spawn
        /// position, laid out exactly like a corpus line. Returns false when the
        /// line could not be spawned (empty, or all line slots are in use).
        /// The injected line gets a fresh LineId and SentIdx = -1.</summary>
        public bool SpawnLine(int[] tokenIndices, bool edible)
        {
            if (tokenIndices == null || tokenIndices.Length == 0) return false;
            int n = tokenIndices.Length;
            if (n > WmConst.MaxLineWords) n = WmConst.MaxLineWords;
            if (Lines.Count >= WmConst.MaxActiveLines) return false;
            var toks = new int[n];
            for (int i = 0; i < n; i++)
            {
                int t = tokenIndices[i];
                if (t < 0 || t >= Asset.NTokens) return false;
                toks[i] = t;
            }
            Lines.Add(Layout(toks, -1, edible));
            return true;
        }

        public void Step(double dt)
        {
            Elapsed += dt;

            for (int i = 0; i < Lines.Count; i++)
            {
                WmWord[] ws = Lines[i].Words;
                for (int j = 0; j < ws.Length; j++) ws[j].Y -= WmConst.ScrollSpeed * dt;
            }

            // Drop lines with no word still above KillY, preserving order.
            int keep = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                WmWord[] ws = Lines[i].Words;
                bool any = false;
                for (int j = 0; j < ws.Length; j++)
                    if (ws[j].Y > WmConst.KillY) { any = true; break; }
                if (any)
                {
                    if (keep != i) Lines[keep] = Lines[i];
                    keep++;
                }
            }
            if (keep < Lines.Count) Lines.RemoveRange(keep, Lines.Count - keep);

            if (Elapsed >= NextSpawn)
            {
                if (Loop || SentIdx < Asset.NSentences) Spawn();
                NextSpawn = Elapsed + WmConst.SpawnInterval;
            }
        }
    }
}
