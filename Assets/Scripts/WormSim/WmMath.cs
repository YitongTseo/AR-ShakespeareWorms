// The swappable math layer (wm_math.h).
//
// + - * / sqrt are correctly rounded by IEEE 754 and therefore bit-exact
// everywhere. sin/cos/atan2/hypot/exp are NOT bit-exact across libm
// implementations; they are isolated here so a future fixed-polynomial kernel
// can replace them in one place. Under Mono/IL2CPP System.Math forwards to the
// platform's C libm, which is what the C firmware uses on the same machine.
//
// hypot has no System.Math equivalent (Math.Sqrt(x*x+y*y) differs in the last
// bit and can overflow), so it is pulled from libm directly, with a managed
// fallback if the native symbol cannot be bound.

using System;
using System.Runtime.InteropServices;

namespace WormSim
{
    public static class WmMath
    {
        public static double Sin(double x) { return Math.Sin(x); }
        public static double Cos(double x) { return Math.Cos(x); }
        public static double Atan2(double y, double x) { return Math.Atan2(y, x); }
        public static double Exp(double x) { return Math.Exp(x); }
        public static double Sqrt(double x) { return Math.Sqrt(x); }
        public static double Fabs(double x) { return Math.Abs(x); }

        [DllImport("libm", EntryPoint = "hypot", CallingConvention = CallingConvention.Cdecl)]
        private static extern double NativeHypot(double x, double y);

        // 0 = untested, 1 = native works, 2 = use managed fallback.
        private static int _hypotMode;

        /// <summary>True when hypot comes from the platform libm (bit-identical to
        /// the C firmware on the same machine); false when the managed fallback is
        /// in use.</summary>
        public static bool HypotIsNative
        {
            get { Hypot(3.0, 4.0); return _hypotMode == 1; }
        }

        public static double Hypot(double x, double y)
        {
            if (_hypotMode == 1) return NativeHypot(x, y);
            if (_hypotMode == 0)
            {
                try
                {
                    double r = NativeHypot(x, y);
                    _hypotMode = 1;
                    return r;
                }
                catch (DllNotFoundException) { _hypotMode = 2; }
                catch (EntryPointNotFoundException) { _hypotMode = 2; }
            }
            return ManagedHypot(x, y);
        }

        // Scaled sqrt(x^2 + y^2): overflow/underflow-safe but only ~1 ULP
        // accurate, not bit-identical to libm's hypot.
        private static double ManagedHypot(double x, double y)
        {
            x = Math.Abs(x); y = Math.Abs(y);
            if (double.IsInfinity(x) || double.IsInfinity(y)) return double.PositiveInfinity;
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            double hi = x > y ? x : y, lo = x > y ? y : x;
            if (hi == 0.0) return 0.0;
            double r = lo / hi;
            return hi * Math.Sqrt(1.0 + r * r);
        }

        /// <summary>Python's float % (floored). C#'s % on doubles is truncated,
        /// like C fmod; both the body's turn test and the smell direction depend
        /// on the floored behaviour for negative operands.</summary>
        public static double PyMod(double x, double y)
        {
            double r = x % y;  // C#'s % on doubles is C fmod: truncated, exact
            if (r != 0.0 && ((r < 0.0) != (y < 0.0))) r += y;
            return r;
        }

        public static double Relu(double x) { return x > 0.0 ? x : 0.0; }

        // _sigmoid in server/embedding.py clips to [-60, 60] before exp.
        public static double Sigmoid(double x)
        {
            if (x < -60.0) x = -60.0;
            else if (x > 60.0) x = 60.0;
            return 1.0 / (1.0 + Exp(-x));
        }

        /// <summary>y[j] = sum_k x[k] * W[k*nOut + j] + b[j], summed left to right
        /// (row-major W, matching numpy's layout; NOT its BLAS summation order).</summary>
        public static void Affine(double[] x, double[] W, double[] b, int nIn, int nOut, double[] y)
        {
            for (int j = 0; j < nOut; j++)
            {
                double s = 0.0;
                for (int k = 0; k < nIn; k++) s += x[k] * W[k * nOut + j];
                y[j] = s + b[j];
            }
        }
    }
}
