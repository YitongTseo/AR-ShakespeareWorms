// sim/world.py — brain slow, body fast, food in between (world.c).

using System;
using System.Collections.Generic;

namespace WormSim
{
    public struct WmEaten
    {
        public int Tok;       // asset token index of the eaten word
        public int LineId;
        public int WordIdx;
    }

    public sealed class WmWorld
    {
        public readonly WmAsset Asset;
        public readonly WmRng Rng;
        public readonly WmBrain Brain;
        public readonly WmBody Body;
        public readonly WmScroller Scroller;
        public readonly WmChemo Chemo = new WmChemo();

        public bool StimHunger, StimNoseTouch, StimFoodSense;
        public long StimLingerUntil;
        public long TickCount;
        public long BrainTickCount;

        // Most-recent-first, exactly like World._recent_eaten. This IS the
        // worm's memory in v7 — there is no decaying residual any more.
        public readonly int[] Recent = new int[WmConst.History];
        public int NRecent;

        // Words eaten since the last DrainEaten. Unbounded here (the firmware
        // caps at 16); it never feeds the simulation.
        private readonly List<WmEaten> _eaten = new List<WmEaten>();

        // _chemo_pulse builds a plain dict and stimulate_weighted then iterates
        // it. Python dicts iterate in INSERTION order, and insertion happens
        // only when a neuron's contribution is > 0, so the order is data-
        // dependent. These three arrays reproduce that exactly: value,
        // insertion sequence, membership.
        private readonly double[] _chemoVal;
        private readonly int[] _chemoOrder;
        private readonly bool[] _chemoSeen;
        private int _nChemo;
        private readonly double[] _pca = new double[WmConst.NPc];

        public WmWorld(WmAsset a, uint seed) : this(a, seed, null) { }

        /// <summary>Whether this world advances its scroller each tick. False for worms that
        /// share another world's scroller (one shared text, many animals).</summary>
        public bool StepsScroller = true;

        /// <summary>Starts a worm on a SHARED scroller (the AR text world): several minds crawl
        /// over the same words. Token indices in the shared scroller must be valid in `a`
        /// (identical baked corpus; dynamic words injected in the same order).</summary>
        public WmWorld(WmAsset a, uint seed, double[] weights, WmScroller shared, bool stepsScroller)
            : this(a, seed, weights, shared)
        {
            StepsScroller = stepsScroller;
        }

        /// <summary>Starts a worm. Pass `weights` (Asset.NEdges doubles in CSR
        /// order) to run a different generation of the same topology than the
        /// one baked into the asset.</summary>
        public WmWorld(WmAsset a, uint seed, double[] weights) : this(a, seed, weights, null) { }

        private WmWorld(WmAsset a, uint seed, double[] weights, WmScroller shared)
        {
            Asset = a;
            _chemoVal = new double[a.NNeurons];
            _chemoOrder = new int[a.NNeurons];
            _chemoSeen = new bool[a.NNeurons];

            // Draw order matters and follows World.__post_init__, not field
            // order: the brain is constructed first but draws nothing, then the
            // body takes 800 uniforms, and only then does rand_excite take its
            // 40 choices.
            Rng = new WmRng(seed);
            Brain = new WmBrain(a);
            if (weights != null) Brain.SetWeights(weights);
            Body = new WmBody(WmConst.WorldW / 2.0, WmConst.WorldH / 2.0, Rng);
            Brain.RandExcite(Rng, 40);

            // loop=False is generation semantics: one full pass of the play,
            // which is what produced the champions we bake.
            Scroller = shared ?? new WmScroller(a, false);

            StimHunger = true;
        }

        private void Stimulate(ushort[] idx)
        {
            for (int i = 0; i < idx.Length; i++) Brain.Accumulate(idx[i], 1.0);
            Brain.Run();
        }

        private void ChemoAdd(int neuron, double v)
        {
            // Emulates `out[n] = out.get(n, 0.0) + v` on a Python dict: first
            // insertion fixes the iteration position, and insertion only
            // happens when v > 0.
            if (!_chemoSeen[neuron])
            {
                _chemoSeen[neuron] = true;
                _chemoVal[neuron] = 0.0;
                _chemoOrder[_nChemo++] = neuron;
            }
            _chemoVal[neuron] += v;
        }

        // _compute_smells fused with _chemo_pulse. The arithmetic and the
        // ordering are identical either way because sensed_smells is keyed
        // uniquely per word and therefore iterates in in-range order.
        private void ComputeSmells()
        {
            WmAsset a = Asset;
            double wx = Body.TargetX, wy = Body.TargetY;

            _nChemo = 0;
            Array.Clear(_chemoSeen, 0, _chemoSeen.Length);

            List<WmLine> lines = Scroller.Lines;
            double[] pca = _pca;
            for (int li = 0; li < lines.Count; li++)
            {
                WmLine L = lines[li];
                if (!L.Edible) continue;  // set-dressing: no eating, no smelling
                WmWord[] ws = L.Words;
                for (int wi = 0; wi < ws.Length; wi++)
                {
                    WmWord word = ws[wi];
                    if (word.Eaten) continue;

                    double dx = word.X - wx;
                    double dy = word.Y - wy;
                    double d = WmMath.Hypot(dx, dy);
                    if (d > WmConst.FoodSenseRadius) continue;

                    if (!Chemo.Embed(a, word.Tok, Recent, NRecent, pca)) continue;

                    double distanceFactor = 1.0 - (d / WmConst.FoodSenseRadius);
                    double foodAngle = WmMath.Atan2(dy, dx);
                    double angleDiff =
                        WmMath.PyMod(foodAngle - Body.FacingDir + WmConst.Pi, 2.0 * WmConst.Pi) - WmConst.Pi;
                    double directionFactor = 0.5 + 0.5 * WmMath.Sin(angleDiff);

                    // compute_pca_activation: v = pca[i] * intensity, THEN split L/R.
                    double dirL = directionFactor, dirR = 1.0 - directionFactor;
                    for (int i = 0; i < WmConst.NPc; i++)
                    {
                        double v = pca[i] * distanceFactor;
                        double vl = v * dirL, vr = v * dirR;
                        if (vl > 0.0) ChemoAdd(a.ChemoPairL[i], vl);
                        if (vr > 0.0) ChemoAdd(a.ChemoPairR[i], vr);
                    }
                }
            }

            for (int i = 0; i < _nChemo; i++)
            {
                int n = _chemoOrder[i];
                if (_chemoVal[n] > 1.0) _chemoVal[n] = 1.0;  // saturate per neuron
            }
        }

        private void CheckFood()
        {
            double wx = Body.TargetX, wy = Body.TargetY;
            List<WmLine> lines = Scroller.Lines;
            for (int li = 0; li < lines.Count; li++)
            {
                WmLine L = lines[li];
                if (!L.Edible) continue;
                WmWord[] ws = L.Words;
                for (int wi = 0; wi < ws.Length; wi++)
                {
                    WmWord word = ws[wi];
                    if (word.Eaten) continue;
                    double d = WmMath.Hypot(wx - word.X, wy - word.Y);
                    if (d > WmConst.FoodSenseRadius) continue;

                    StimFoodSense = true;
                    StimLingerUntil = TickCount + WmConst.StimLingerTicks;
                    if (d > WmConst.FoodEatRadius) continue;

                    word.Eaten = true;
                    WmEaten e;
                    e.Tok = word.Tok;
                    e.LineId = L.LineId;
                    e.WordIdx = wi;
                    _eaten.Add(e);
                    // _recent_eaten.insert(0, word) then truncate to HISTORY.
                    for (int k = (NRecent < WmConst.History ? NRecent : WmConst.History - 1); k > 0; k--)
                        Recent[k] = Recent[k - 1];
                    Recent[0] = word.Tok;
                    if (NRecent < WmConst.History) NRecent++;
                }
            }
        }

        private void CheckWalls()
        {
            bool hit = false;
            if (Body.TargetX < 0.0) { Body.TargetX = 0.0; hit = true; }
            else if (Body.TargetX > WmConst.WorldW) { Body.TargetX = WmConst.WorldW; hit = true; }
            if (Body.TargetY < 0.0) { Body.TargetY = 0.0; hit = true; }
            else if (Body.TargetY > WmConst.WorldH) { Body.TargetY = WmConst.WorldH; hit = true; }
            if (hit)
            {
                StimNoseTouch = true;
                StimLingerUntil = TickCount + WmConst.StimLingerTicks;
            }
        }

        /// <summary>One body tick (1/60 s). The brain runs every 30th tick.</summary>
        public void Tick()
        {
            WmAsset a = Asset;

            if (StepsScroller) Scroller.Step(WmConst.BodyDt);

            if (TickCount % WmConst.BrainTickPeriod == 0)
            {
                // Smells are computed only on brain ticks: the brain is the only
                // reader and the embedding pass is the dominant per-tick cost.
                // Note this reads the body position from BEFORE this tick's Step().
                ComputeSmells();

                if (StimHunger) Stimulate(a.HungerIdx);
                if (StimNoseTouch) Stimulate(a.NoseIdx);
                if (StimFoodSense) Stimulate(a.FoodIdx);

                if (_nChemo > 0)
                {
                    for (int i = 0; i < _nChemo; i++)
                    {
                        int n = _chemoOrder[i];
                        double v = _chemoVal[n];
                        if (v > 0.0) Brain.Accumulate(n, v);
                    }
                    Brain.Run();
                }

                BrainTickCount++;
                Body.ConsumeMotor(Brain.AccumLeft, Brain.AccumRight);
            }

            Body.Step();
            CheckFood();
            CheckWalls();

            if (TickCount >= StimLingerUntil)
            {
                StimHunger = true;
                StimNoseTouch = false;
                StimFoodSense = false;
            }

            TickCount++;
        }

        /// <summary>Moves the words eaten since the last call into `outp`
        /// (appended) and returns how many there were.</summary>
        public int DrainEaten(List<WmEaten> outp)
        {
            int n = _eaten.Count;
            if (outp != null) outp.AddRange(_eaten);
            _eaten.Clear();
            return n;
        }

        /// <summary>Poke the worm. Raises the same nose-touch stimulus a wall
        /// collision does, so the escape response comes out of the connectome's
        /// own FLP/ASH/OLQ mechanosensors rather than being animated on top.
        /// This does change the worm's life, and therefore its poem.</summary>
        public void Poke()
        {
            StimNoseTouch = true;
            StimLingerUntil = TickCount + WmConst.StimLingerTicks;
        }

        /// <summary>Lays a line of tokens (see WmAsset.AddWord / AddToken) into
        /// the worm's field at the spawn position. Edible lines can be smelled
        /// and eaten like Hamlet's dialogue.</summary>
        public bool SpawnLine(int[] tokenIndices, bool edible)
        {
            return Scroller.SpawnLine(tokenIndices, edible);
        }

        /// <summary>Convenience: lays a spoken sentence into the field. Words the
        /// worm already knows (baked vocabulary or earlier AddWord calls) get
        /// their taste; unknown ones get OOV tokens and are merely visible. POS
        /// tags default to X (index 10) when not supplied.</summary>
        public bool Say(string[] words, byte[] posTags, bool edible)
        {
            if (words == null || words.Length == 0) return false;
            var toks = new int[words.Length];
            for (int i = 0; i < words.Length; i++)
            {
                byte tag = posTags != null && i < posTags.Length ? posTags[i] : (byte)10;
                toks[i] = Asset.AddKnownWordToken(words[i], tag);
            }
            return Scroller.SpawnLine(toks, edible);
        }

        /// <summary>Text of a token (eaten word, on-screen word).</summary>
        public string TokenText(int tok) { return Asset.TokText[tok]; }

        /// <summary>The recently eaten words, most recent first.</summary>
        public string[] RecentWords()
        {
            var r = new string[NRecent];
            for (int i = 0; i < NRecent; i++) r[i] = Asset.TokText[Recent[i]];
            return r;
        }
    }
}
