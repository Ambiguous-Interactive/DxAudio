namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>
    /// The main-thread random stream: an <see cref="ISfxRandomStream"/> over a <see cref="SfxXoshiro256StarStar"/>,
    /// owning the Gaussian spare and the draw discipline. Audio-thread hot loops draw through the struct directly.
    /// </summary>
    public sealed class SfxRandomStream : ISfxRandomStream, ISfxRandom
    {
        /// <summary>The seed this stream was derived from.</summary>
        public ulong Seed => _generator.Seed;

        private SfxXoshiro256StarStar _generator;
        private double _spareGaussian;

        public SfxRandomStream(ulong seed)
        {
            _generator = new SfxXoshiro256StarStar(seed);
            _spareGaussian = double.NaN;
        }

        /// <summary>
        /// Builds the stream for one node instance: the seed comes from
        /// <see cref="SfxRandomSeeds.NodeSeed"/>, so the same graph seed, path and instance index give the same
        /// stream on every platform.
        /// </summary>
        public static SfxRandomStream ForNode(ulong graphSeed, string nodePath, ulong instanceIndex)
        {
            return new SfxRandomStream(SfxRandomSeeds.NodeSeed(graphSeed, nodePath, instanceIndex));
        }

        public double NextDouble()
        {
            return _generator.NextDouble();
        }

        public float NextFloat(float min, float max)
        {
            if (!(min < max) || float.IsInfinity(min) || float.IsInfinity(max))
            {
                return min;
            }

            double span = (double)max - min;
            double drawn = min + (span * NextDouble());
            float value = (float)drawn;
            if (max < value)
            {
                return max;
            }

            if (value < min)
            {
                return min;
            }

            return value;
        }

        public bool NextBoolSparing(double probability)
        {
            if (double.IsNaN(probability) || probability <= 0d)
            {
                return false;
            }

            if (1d <= probability)
            {
                return true;
            }

            return NextDouble() < probability;
        }

        public double NextExponential(double rate)
        {
            if (!(0d < rate))
            {
                return double.NaN;
            }

            return SfxRandomMath.ExponentialFromUniform(NextDouble(), rate);
        }

        public double NextGaussian(double mean, double standardDeviation)
        {
            if (!(0d < standardDeviation) || double.IsInfinity(standardDeviation))
            {
                return mean;
            }

            if (!double.IsNaN(_spareGaussian))
            {
                double spare = _spareGaussian;
                _spareGaussian = double.NaN;
                return mean + (standardDeviation * spare);
            }

            double firstUniform = NextDouble();
            double secondUniform = NextDouble();
            SfxRandomMath.GaussianPairFromUniform(firstUniform, secondUniform, 0d, 1d, out double standard, out double spareNext);
            _spareGaussian = spareNext;
            return mean + (standardDeviation * standard);
        }

        public ISfxRandomStream Fork()
        {
            return new SfxRandomStream(ForkSeed());
        }

        public RandomStreamState Capture()
        {
            RandomStreamState state = _generator.Capture();
            return new RandomStreamState(state.S0, state.S1, state.S2, state.S3, _spareGaussian);
        }

        public void Restore(in RandomStreamState state)
        {
            if (state.S0 == 0UL && state.S1 == 0UL && state.S2 == 0UL && state.S3 == 0UL)
            {
                return;
            }

            _generator.Restore(state);
            _spareGaussian = state.SpareGaussian;
        }

        /// <summary>
        /// Derives a child seed from this stream's full current position — the four words and any pending spare —
        /// so the same position always forks to the same stream and the fork never follows its parent's state.
        /// </summary>
        private ulong ForkSeed()
        {
            RandomStreamState state = Capture();
            ulong hash = SfxRandomSeeds.Fold(state.S0, state.S1);
            hash = SfxRandomSeeds.Fold(hash, state.S2);
            hash = SfxRandomSeeds.Fold(hash, state.S3);
            hash = SfxRandomSeeds.Fold(hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(state.SpareGaussian + 0d)));
            return SfxRandomSeeds.Mix64(hash);
        }
    }
}
