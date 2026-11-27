namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>
    /// A captured <see cref="ISfxRandomStream"/> position: the four xoshiro256** words plus any spare Gaussian
    /// draw. The spare is NaN when none is stored.
    /// </summary>
    public readonly struct RandomStreamState : IEquatable<RandomStreamState>
    {
        public readonly ulong S0;
        public readonly ulong S1;
        public readonly ulong S2;
        public readonly ulong S3;

        /// <summary>The stored spare standard normal, or NaN when none is stored.</summary>
        public readonly double SpareGaussian;

        public RandomStreamState(ulong s0, ulong s1, ulong s2, ulong s3, double spareGaussian)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
            SpareGaussian = spareGaussian;
        }

        public bool Equals(RandomStreamState other)
        {
            return S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3 && SpareGaussian.Equals(other.SpareGaussian);
        }

        public override bool Equals(object other)
        {
            return other is RandomStreamState state && Equals(state);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(S0, S1, S2, S3, SpareGaussian);
        }
    }
}
