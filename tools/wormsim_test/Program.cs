// Console driver for the C# port, mirroring HamletWorms/tools/hostsim/main.c
// line for line so the two traces can be diffed directly:
//
//   ./run.sh <asset.hwrm> <n_ticks> [state_every] [embed]
//
// Body state prints as C's %a (hex float) — the whole point is to catch a
// discrepancy in the last bit, which %f would hide.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using WormSim;

static class Program
{
    // printf("%a") for a double, as glibc/Apple libc format it: "0x1.<hex>p<exp>"
    // with trailing zero digits stripped and no '.' when the fraction is zero.
    static string HexFloat(double v)
    {
        long bits = BitConverter.DoubleToInt64Bits(v);
        bool neg = bits < 0;
        int exp = (int)((bits >> 52) & 0x7FF);
        long mant = bits & 0xFFFFFFFFFFFFFL;
        var sb = new StringBuilder();
        if (neg) sb.Append('-');
        if (exp == 0x7FF)
        {
            sb.Append(mant == 0 ? "inf" : "nan");
            return sb.ToString();
        }
        int e;
        string lead;
        if (exp == 0)
        {
            if (mant == 0) { sb.Append("0x0p+0"); return sb.ToString(); }
            lead = "0x0"; e = -1022;   // subnormal, glibc style
        }
        else
        {
            lead = "0x1"; e = exp - 1023;
        }
        sb.Append(lead);
        if (mant != 0)
        {
            string hex = mant.ToString("x13").TrimEnd('0');
            sb.Append('.').Append(hex);
        }
        sb.Append('p').Append(e >= 0 ? "+" : "").Append(e.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    static int Main(string[] argv)
    {
        if (argv.Length < 2)
        {
            Console.Error.WriteLine("usage: wormsim_test <asset.hwrm> <n_ticks> [state_every] [embed]");
            return 2;
        }
        string path = argv[0];
        long nTicks = long.Parse(argv[1], CultureInfo.InvariantCulture);
        long stateEvery = argv.Length > 2 ? long.Parse(argv[2], CultureInfo.InvariantCulture) : 600;

        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16);
        stdout.NewLine = "\n";
        var stderr = Console.Error;

        byte[] buf = File.ReadAllBytes(path);
        WmAsset a;
        try { a = WmAsset.Parse(buf); }
        catch (FormatException) { stderr.WriteLine("bad asset"); return 1; }

        stderr.WriteLine("asset: {0} vocab, {1} neurons, {2} edges, {3} sentences, {4} tokens, seed {5}",
            a.NVocab, a.NNeurons, a.NEdges, a.NSentences, a.NTokens, a.Seed);
        if (!WmMath.HypotIsNative) stderr.WriteLine("warning: hypot is the managed fallback, not libm");

        // Probe mode: dump the 12-dim chemosensory vector for a handful of
        // (word, history) pairs — same probes as hostsim's embed mode.
        if (argv.Length > 3 && argv[3] == "embed")
        {
            int[][] probes = {
                new[] {100, 0, 0, 0, 0, 0}, new[] {101, 100, 0, 0, 0, 0},
                new[] {205, 100, 101, 0, 0, 0}, new[] {311, 205, 100, 101, 102, 0},
                new[] {412, 311, 205, 100, 101, 102}, new[] {77, 412, 311, 205, 100, 101},
            };
            int[] nHist = {0, 1, 2, 4, 5, 5};
            var chemo = new WmChemo();
            var pca = new double[WmConst.NPc];
            for (int p = 0; p < 6; p++)
            {
                var hist = new int[5];
                Array.Copy(probes[p], 1, hist, 0, 5);
                if (!chemo.Embed(a, probes[p][0], hist, nHist[p], pca))
                {
                    stdout.WriteLine("PROBE {0} OOV", probes[p][0]);
                    continue;
                }
                stdout.Write("PROBE {0} {1}", probes[p][0], nHist[p]);
                for (int i = 0; i < WmConst.NPc; i++) stdout.Write(" " + HexFloat(pca[i]));
                stdout.WriteLine();
            }
            stdout.Flush();
            return 0;
        }

        var w = new WmWorld(a, a.Seed);
        var got = new List<WmEaten>();

        // Smoke test for the dynamic vocabulary: at tick `sayAt` lay a spoken
        // line into the field — two Hamlet words the worm already knows, one
        // brand-new word with a synthetic taste row, and one tasteless word —
        // and report its geometry and whether it can be smelled. Not part of
        // the C comparison (the C firmware has no equivalent).
        //   run.sh <asset> <n_ticks> <state_every> say [tick]
        bool sayMode = argv.Length > 3 && argv[3] == "say";
        long sayAt = sayMode && argv.Length > 4 ? long.Parse(argv[4], CultureInfo.InvariantCulture) : 1200;

        int maxLines = 0, maxWords = 0;
        for (long t = 0; t < nTicks; t++)
        {
            if (sayMode && t == sayAt)
            {
                int nTokBefore = a.NTokens, nVocBefore = a.NVocab;
                var row = new double[WmConst.DEmb];
                for (int i = 0; i < row.Length; i++) row[i] = 0.25 + 0.05 * i;
                int[] toks = {
                    a.AddKnownWordToken("Horatio", 0),      // NOUN, baked vocabulary
                    a.AddKnownWordToken("remember", 1),     // VERB, baked vocabulary
                    a.AddWord("spaceship", row, 0),         // NOUN, new taste row
                    a.AddWord("zzqx", null, 10),            // X, no taste (OOV)
                };
                bool ok = w.SpawnLine(toks, true);
                stdout.WriteLine("SAY {0} spawned={1} vocab {2}->{3} tokens {4}->{5}", w.TickCount, ok,
                    nVocBefore, a.NVocab, nTokBefore, a.NTokens);
                WmLine L = w.Scroller.Lines[w.Scroller.Lines.Count - 1];
                var pca = new double[WmConst.NPc];
                for (int i = 0; i < L.NWords; i++)
                {
                    WmWord wd = L.Words[i];
                    bool smell = w.Chemo.Embed(a, wd.Tok, w.Recent, w.NRecent, pca);
                    stdout.WriteLine("  word {0} tok={1} vocab={2} pos={3} len={4} x={5} y={6} smellable={7}{8}",
                        a.TokText[wd.Tok], wd.Tok, a.TokVocab[wd.Tok] == WmConst.Oov ? "OOV" : a.TokVocab[wd.Tok].ToString(),
                        a.TokPos[wd.Tok], a.TokLen[wd.Tok], wd.X.ToString("F2", CultureInfo.InvariantCulture),
                        wd.Y.ToString("F2", CultureInfo.InvariantCulture), smell,
                        smell ? " pc0=" + pca[0].ToString("F4", CultureInfo.InvariantCulture) + " pc11=" + pca[11].ToString("F4", CultureInfo.InvariantCulture) : "");
                }
            }
            w.Tick();
            if (w.Scroller.Lines.Count > maxLines) maxLines = w.Scroller.Lines.Count;
            for (int li = 0; li < w.Scroller.Lines.Count; li++)
                if (w.Scroller.Lines[li].NWords > maxWords) maxWords = w.Scroller.Lines[li].NWords;

            if (w.Scroller.Exhausted)
            {
                stderr.WriteLine("corpus exhausted at tick {0}", w.TickCount);
                break;
            }

            got.Clear();
            int n = w.DrainEaten(got);
            for (int i = 0; i < n; i++)
                stdout.WriteLine("EAT {0} {1} {2} {3}", w.TickCount, got[i].LineId, got[i].WordIdx, a.TokText[got[i].Tok]);

            if (stateEvery > 0 && (t % stateEvery) == 0)
                stdout.WriteLine("STATE {0} {1} {2} {3} {4}", w.TickCount,
                    HexFloat(w.Body.TargetX), HexFloat(w.Body.TargetY),
                    HexFloat(w.Body.FacingDir), HexFloat(w.Body.Speed));
        }

        stderr.WriteLine("peak {0}/{1} active lines, {2}/{3} words per line",
            maxLines, WmConst.MaxActiveLines, maxWords, WmConst.MaxLineWords);
        stdout.WriteLine("END {0}", w.TickCount);
        stdout.Flush();
        return 0;
    }
}
