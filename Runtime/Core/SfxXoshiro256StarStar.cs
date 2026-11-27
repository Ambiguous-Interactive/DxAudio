namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>
    /// The xoshiro256** generator, reimplemented from Blackman and Vigna's published specification. A struct so an
    /// audio-thread hot loop draws without interface dispatch, boxing, or heap use.
    /// </summary>
    /// <remarks>
    /// Seeding expands one 64-bit seed through the splitmix64 finalizer, so an all-zero state is unreachable. The
    /// uniform conversion is the standard 53-bit form: a draw is the top 53 bits times 2^-53, which lands in
    /// [0, 1).
    /// </remarks>
    public struct SfxXoshiro256StarStar : IEquatable<SfxXoshiro256StarStar>
    {
        private const double UniformScale = 1.0d / 9007199254740992.0d;

        /// <summary>The seed this generator was constructed with.</summary>
        public ulong Seed => _seed;

        private readonly ulong _seed;
        private ulong _s0;
        private ulong _s1;
        private ulong _s2;
        private ulong _s3;

        public SfxXoshiro256StarStar(ulong seed)
        {
            _seed = seed;
            ulong state = seed;
            state += SfxRandomSeeds.Gamma;
            _s0 = SfxRandomSeeds.Mix64(state);
            state += SfxRandomSeeds.Gamma;
            _s1 = SfxRandomSeeds.Mix64(state);
            state += SfxRandomSeeds.Gamma;
            _s2 = SfxRandomSeeds.Mix64(state);
            state += SfxRandomSeeds.Gamma;
            _s3 = SfxRandomSeeds.Mix64(state);
        }

        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }

        /// <summary>Returns the next 64 pseudorandom value and advances the state.</summary>
        public ulong NextUInt64()
        {
            ulong result = RotateLeft(_s1 * 5UL, 7) * 9UL;
            ulong scrambled = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= scrambled;
            _s3 = RotateLeft(_s3, 45);
            return result;
        }

        /// <summary>Returns a uniformly distributed value in [0, 1) and advances the state.</summary>
        public double NextDouble()
        {
            return (NextUInt64() >> 11) * UniformScale;
        }

        /// <summary>Captures the four state words. A struct keeps no spare, so the spare is NaN.</summary>
        public RandomStreamState Capture()
        {
            return new RandomStreamState(_s0, _s1, _s2, _s3, double.NaN);
        }

        /// <summary>Restores the four state words from a captured position.</summary>
        public void Restore(in RandomStreamState state)
        {
            _s0 = state.S0;
            _s1 = state.S1;
            _s2 = state.S2;
            _s3 = state.S3;
        }

        public bool Equals(SfxXoshiro256StarStar other)
        {
            return _seed == other._seed && _s0 == other._s0 && _s1 == other._s1 && _s2 == other._s2 && _s3 == other._s3;
        }

        public override bool Equals(object other)
        {
            return other is SfxXoshiro256StarStar generator && Equals(generator);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_seed, _s0, _s1, _s2, _s3);
        }
    }
}
