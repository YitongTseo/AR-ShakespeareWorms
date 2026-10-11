// CPython's random.Random, ported (mt19937.c).
//
// Not "an MT19937" — *the* one, including CPython's init_by_array seeding path
// and the exact 53-bit double construction, because the worm's entire life is
// downstream of these draws. WmWorld makes them in this order:
//
//   1. WmBody -> 4 x Uniform(-1, 1) per link, 800 draws for the 200-segment body.
//   2. WmBrain.RandExcite -> 40 x choice(list(weights.keys())).

namespace WormSim
{
    public sealed class WmRng
    {
        private const int N = 624;
        private const int M = 397;
        private const uint MatrixA = 0x9908b0dfu;
        private const uint UpperMask = 0x80000000u;
        private const uint LowerMask = 0x7fffffffu;

        private readonly uint[] _mt = new uint[N];
        private int _index;

        public WmRng() { Seed(0); }
        public WmRng(uint seed) { Seed(seed); }

        private void InitGenrand(uint s)
        {
            _mt[0] = s;
            for (int i = 1; i < N; i++)
                _mt[i] = unchecked(1812433253u * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i);
            _index = N;
        }

        private void InitByArray(uint[] key)
        {
            int klen = key.Length;
            InitGenrand(19650218u);
            int i = 1, j = 0;
            int k = N > klen ? N : klen;
            for (; k > 0; k--)
            {
                _mt[i] = unchecked((_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1664525u)) + key[j] + (uint)j);
                i++; j++;
                if (i >= N) { _mt[0] = _mt[N - 1]; i = 1; }
                if (j >= klen) j = 0;
            }
            for (k = N - 1; k > 0; k--)
            {
                _mt[i] = unchecked((_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1566083941u)) - (uint)i);
                i++;
                if (i >= N) { _mt[0] = _mt[N - 1]; i = 1; }
            }
            _mt[0] = 0x80000000u;
            _index = N;
        }

        /// <summary>random.seed(n) for 0 &lt;= n &lt; 2^32: CPython splits abs(n)
        /// into 32-bit little-endian words (at least one), so this is a
        /// single-element key.</summary>
        public void Seed(uint seed)
        {
            InitByArray(new uint[] { seed });
        }

        /// <summary>random.seed(n) for arbitrary non-negative n, given as its
        /// little-endian 32-bit words (what CPython's random_seed builds).</summary>
        public void SeedKey(uint[] key)
        {
            InitByArray(key.Length == 0 ? new uint[] { 0 } : key);
        }

        public uint NextU32()
        {
            if (_index >= N)
            {
                uint y;
                int kk;
                for (kk = 0; kk < N - M; kk++)
                {
                    y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                    _mt[kk] = _mt[kk + M] ^ (y >> 1) ^ ((y & 1u) != 0 ? MatrixA : 0u);
                }
                for (; kk < N - 1; kk++)
                {
                    y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                    _mt[kk] = _mt[kk + (M - N)] ^ (y >> 1) ^ ((y & 1u) != 0 ? MatrixA : 0u);
                }
                y = (_mt[N - 1] & UpperMask) | (_mt[0] & LowerMask);
                _mt[N - 1] = _mt[M - 1] ^ (y >> 1) ^ ((y & 1u) != 0 ? MatrixA : 0u);
                _index = 0;
            }
            uint r = _mt[_index++];
            r ^= (r >> 11);
            r ^= (r << 7) & 0x9d2c5680u;
            r ^= (r << 15) & 0xefc60000u;
            r ^= (r >> 18);
            return r;
        }

        /// <summary>CPython random.random(): two draws, 27 + 26 bits, exactly
        /// this expression.</summary>
        public double NextDouble()
        {
            uint a = NextU32() >> 5;
            uint b = NextU32() >> 6;
            return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
        }

        public double Uniform(double a, double b)
        {
            return a + (b - a) * NextDouble();
        }

        private uint GetRandBits(int k)
        {
            if (k == 0) return 0;
            if (k >= 32) return NextU32();
            return NextU32() >> (32 - k);
        }

        /// <summary>CPython _randbelow_with_getrandbits: rejection-sample
        /// k = n.bit_length() bits.</summary>
        public uint Below(uint n)
        {
            if (n == 0) return 0;
            int k = 0;
            for (uint v = n; v != 0; v >>= 1) k++;
            uint x = GetRandBits(k);
            while (x >= n) x = GetRandBits(k);
            return x;
        }
    }
}
