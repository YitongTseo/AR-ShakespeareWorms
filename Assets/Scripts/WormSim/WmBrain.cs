// sim/connectome.py — integrate-and-fire, double-buffered (connectome.c).
//
// The one thing to be careful about here is *order*. Float addition is not
// associative, so synapses must land in the order CPython's dict iteration
// produced (bake.py preserved it in the CSR rows) and neurons must be visited
// in sorted() order (bake.py sorted them).

namespace WormSim
{
    public sealed class WmBrain
    {
        public readonly WmAsset Asset;

        /// <summary>[NNeurons][2], interleaved: Psyn[n*2 + state].</summary>
        public readonly double[] Psyn;

        /// <summary>Synaptic weights in CSR edge order. The baked table by
        /// default; SetWeights swaps in another generation without disturbing
        /// the topology, which never changes.</summary>
        public double[] SynW;

        public int ThisState, NextState;
        public double AccumLeft, AccumRight;

        public WmBrain(WmAsset a)
        {
            Asset = a;
            Psyn = new double[2 * a.NNeurons];
            SynW = a.SynW;
            ThisState = 0;
            NextState = 1;
            AccumLeft = 0.0;
            AccumRight = 0.0;
        }

        /// <summary>Charge of neuron n as the brain currently sees it
        /// (psyn[n][this_state]) — what Connectome.activity() reports and what a
        /// connectome activity map should draw.</summary>
        public double Activity(int n) { return Psyn[n * 2 + ThisState]; }

        public void Accumulate(int pre, double scale)
        {
            WmAsset a = Asset;
            if (pre < 0 || pre >= a.NNeurons) return;
            int s = a.RowStart[pre], e = a.RowStart[pre + 1];
            int ns = NextState;
            ushort[] col = a.SynCol;
            double[] w = SynW;
            double[] psyn = Psyn;
            for (int i = s; i < e; i++)
                psyn[col[i] * 2 + ns] += w[i] * scale;
        }

        private void FireNeuron(int n)
        {
            // Guarding MVULVA is dead code in practice — Run() already skips
            // anything whose name starts with a muscle prefix, and MVULVA starts
            // with "MVU". Kept because the Python keeps it.
            if (n == Asset.MvulvaIdx) return;
            Accumulate(n, 1.0);
            Psyn[n * 2 + NextState] = 0.0;
        }

        private void MotorControl()
        {
            WmAsset a = Asset;
            AccumLeft = 0.0;
            AccumRight = 0.0;
            int ns = NextState;
            // MUSCLE_LIST visits MDL21 and MVL21 twice (M_RIGHT carries the
            // left-side names — a typo inherited from the GoPiGo original that
            // worm-sim and v7 both preserved). The second visit reads the value
            // this loop already zeroed and adds 0.0 to accum_LEFT, because
            // `m in _left_set` matches first. MDR21/MVR21 are consequently never
            // visited at all: their charge accumulates forever and is never read.
            // Both quirks are load-bearing and live in the baked MUSCLES table.
            for (int i = 0; i < a.NMuscleVisits; i++)
            {
                int m = a.MuscleIdx[i];
                double v = Psyn[m * 2 + ns];
                if (a.MuscleSide[i] == 0) AccumLeft += v;
                else if (a.MuscleSide[i] == 1) AccumRight += v;
                Psyn[m * 2 + ns] = 0.0;
            }
        }

        public void Run()
        {
            WmAsset a = Asset;
            int ts = ThisState;
            int n = a.NNeurons;
            for (int i = 0; i < n; i++)
            {
                if (a.IsMuscle[i] != 0) continue;
                if (Psyn[i * 2 + ts] > WmConst.FireThreshold) FireNeuron(i);
            }
            MotorControl();
            int ns = NextState;
            for (int i = 0; i < n; i++) Psyn[i * 2 + ts] = Psyn[i * 2 + ns];
            ThisState = ns;
            NextState = ts;
        }

        /// <summary>choice() over list(weights.keys()) — 300 entries, not the 396
        /// neurons. The length sets the rejection-sampling bit width, so using
        /// the wrong list consumes a different number of draws and desyncs the
        /// whole RNG stream.</summary>
        public void RandExcite(WmRng r, int k)
        {
            WmAsset a = Asset;
            for (int i = 0; i < k; i++)
                Accumulate(a.PresynIdx[r.Below((uint)a.NPresyn)], 1.0);
        }

        /// <summary>Swap the synaptic weights. Pass null to go back to the baked
        /// ones. The array must have Asset.NEdges entries in CSR order.</summary>
        public void SetWeights(double[] weights)
        {
            SynW = weights ?? Asset.SynW;
        }
    }
}
