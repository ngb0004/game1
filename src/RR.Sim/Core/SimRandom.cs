namespace RR.Sim.Core
{
    /// xorshift128+ seeded through splitmix64. The only source of randomness in
    /// a fight: never use System.Random or UnityEngine.Random in the sim.
    public sealed class SimRandom
    {
        ulong _s0;
        ulong _s1;

        public SimRandom(ulong seed)
        {
            ulong x = seed;
            _s0 = SplitMix(ref x);
            _s1 = SplitMix(ref x);
            if (_s0 == 0 && _s1 == 0) _s1 = 1;
        }

        static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong s1 = _s0;
            ulong s0 = _s1;
            _s0 = s0;
            s1 ^= s1 << 23;
            _s1 = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
            return _s1 + s0;
        }

        /// Uniform in [0, 1).
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// Uniform in [0, maxExclusive).
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            return (int)(NextULong() % (ulong)maxExclusive);
        }

        /// Uniform in [min, max].
        public int NextIntInclusive(int min, int max)
        {
            if (max <= min) return min;
            return min + NextInt(max - min + 1);
        }

        public bool Roll(double chance) => NextDouble() < chance;
    }
}
