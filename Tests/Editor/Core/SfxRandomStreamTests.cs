namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class SfxRandomStreamTests
    {
        private const int StatisticsDraws = 50_000;

        private static double[] DrawsFrom(ISfxRandomStream stream, int count)
        {
            double[] draws = new double[count];
            for (int index = 0; index < count; ++index)
            {
                draws[index] = stream.NextDouble();
            }

            return draws;
        }

        private static double[] MixedDraws(ISfxRandomStream stream, int rounds)
        {
            double[] draws = new double[rounds * 5];
            int write = 0;
            for (int round = 0; round < rounds; ++round)
            {
                draws[write++] = stream.NextDouble();
                draws[write++] = stream.NextFloat(-1f, 1f);
                draws[write++] = stream.NextBoolSparing(0.3d) ? 1d : 0d;
                draws[write++] = stream.NextExponential(2d);
                draws[write++] = stream.NextGaussian(0d, 1d);
            }

            return draws;
        }

        private static bool SameWords(in RandomStreamState left, in RandomStreamState right)
        {
            return left.S0 == right.S0 && left.S1 == right.S1 && left.S2 == right.S2 && left.S3 == right.S3;
        }

        private static double Pearson(double[] first, double[] second)
        {
            int firstLength = first.Length;
            int secondLength = second.Length;
            double sumFirst = 0d;
            double sumSecond = 0d;
            for (int index = 0; index < firstLength; ++index)
            {
                sumFirst += first[index];
                sumSecond += second[index];
            }

            double meanFirst = sumFirst / firstLength;
            double meanSecond = sumSecond / secondLength;
            double covariance = 0d;
            double spreadFirst = 0d;
            double spreadSecond = 0d;
            for (int index = 0; index < firstLength; ++index)
            {
                double offsetFirst = first[index] - meanFirst;
                double offsetSecond = second[index] - meanSecond;
                covariance += offsetFirst * offsetSecond;
                spreadFirst += offsetFirst * offsetFirst;
                spreadSecond += offsetSecond * offsetSecond;
            }

            double denominator = Math.Sqrt(spreadFirst * spreadSecond);
            return 0d < denominator ? covariance / denominator : 0d;
        }

        [Test]
        public void SameSeedAndSettingsRenderIdentically()
        {
            double[] uniformFirst = DrawsFrom(new SfxRandomStream(42UL), 128);
            double[] mixedFirst = MixedDraws(new SfxRandomStream(42UL), 32);

            Assert.That(DrawsFrom(new SfxRandomStream(42UL), 128), Is.EqualTo(uniformFirst));
            Assert.That(MixedDraws(new SfxRandomStream(42UL), 32), Is.EqualTo(mixedFirst));
        }

        [Test]
        public void ARerenderedTimelineIsBitIdentical()
        {
            SfxRandomStream first = new(42UL);
            for (int index = 0; index < 17; ++index)
            {
                first.NextDouble();
            }

            double[] firstPass = MixedDraws(first, 16);

            SfxRandomStream second = new(42UL);
            for (int index = 0; index < 17; ++index)
            {
                second.NextDouble();
            }

            RandomStreamState saved = second.Capture();
            MixedDraws(second, 16);
            second.Restore(saved);

            double[] secondPass = MixedDraws(second, 16);
            Assert.That(secondPass, Is.EqualTo(firstPass), "the same seed and position render the same timeline");
        }

        [Test]
        public void ANodeStreamMatchesTheSeedItWasDerivedFrom()
        {
            ulong seed = SfxRandomSeeds.NodeSeed(7UL, "graph/bed", 2UL);
            Assert.That(
                DrawsFrom(SfxRandomStream.ForNode(7UL, "graph/bed", 2UL), 64),
                Is.EqualTo(DrawsFrom(new SfxRandomStream(seed), 64)),
                "ForNode is the documented derivation, nothing more"
            );
        }

        [Test]
        public void EditingOneInstanceLeavesAnotherIdentical()
        {
            double[] secondBefore = DrawsFrom(SfxRandomStream.ForNode(7UL, "graph/second", 0UL), 32);

            // The edit: the first instance's path changes. Its own stream re-seeds, the sibling's must not move.
            ISfxRandomStream edited = SfxRandomStream.ForNode(7UL, "graph/first-edited", 0UL);
            double[] editedDraws = DrawsFrom(edited, 32);
            double[] originalDraws = DrawsFrom(SfxRandomStream.ForNode(7UL, "graph/first", 0UL), 32);
            double[] secondAfter = DrawsFrom(SfxRandomStream.ForNode(7UL, "graph/second", 0UL), 32);
            Assert.That(editedDraws, Is.Not.EqualTo(originalDraws), "a renamed node re-seeds its own stream");
            Assert.That(secondAfter, Is.EqualTo(secondBefore), "the sibling stream is untouched");
        }

        [Test]
        public void TwoInstancesOfOneModuleNeverShareAStream()
        {
            ISfxRandomStream first = SfxRandomStream.ForNode(3UL, "module/bed", 0UL);
            ISfxRandomStream second = SfxRandomStream.ForNode(3UL, "module/bed", 1UL);
            const int Draws = 10_000;
            double[] firstDraws = DrawsFrom(first, Draws);
            double[] secondDraws = DrawsFrom(second, Draws);

            Assert.That(Math.Abs(Pearson(firstDraws, secondDraws)), Is.LessThan(0.05d), "the first 10,000 draws stay decorrelated in either direction");
        }

        [TestCase(1UL)]
        [TestCase(42UL)]
        [TestCase(0x9E3779B97F4A7C15UL)]
        public void UniformDrawsStayInsideUnitRangeAndCenter(ulong seed)
        {
            SfxRandomStream stream = new(seed);
            double sum = 0d;
            double sumSquares = 0d;
            for (int index = 0; index < StatisticsDraws; ++index)
            {
                double draw = stream.NextDouble();
                Assert.That(0d <= draw, Is.True, "a draw fell below zero");
                Assert.That(draw < 1d, Is.True, "a draw reached one");
                sum += draw;
                sumSquares += draw * draw;
            }

            double mean = sum / StatisticsDraws;
            double variance = (sumSquares / StatisticsDraws) - (mean * mean);
            Assert.That(Math.Abs(mean - 0.5d), Is.LessThan(0.02d), "the mean sits near one half");
            Assert.That(Math.Abs(variance - (1d / 12d)), Is.LessThan(0.02d), "the variance sits near one twelfth");
        }

        [Test]
        public void ExponentialGapsAverageAtOneOverTheRate()
        {
            SfxRandomStream stream = new(21UL);
            double sum = 0d;
            for (int index = 0; index < StatisticsDraws; ++index)
            {
                double draw = stream.NextExponential(2d);
                Assert.That(0d <= draw, Is.True, "an exponential gap went negative");
                sum += draw;
            }

            Assert.That(Math.Abs((sum / StatisticsDraws) - 0.5d), Is.LessThan(0.01d), "the mean sits near 1 / rate");
        }

        [TestCase(0d, TestName = "A zero rate")]
        [TestCase(-2d, TestName = "A negative rate")]
        [TestCase(double.NaN, TestName = "A NaN rate")]
        public void AnInvalidExponentialRateConsumesNoDraw(double rate)
        {
            SfxRandomStream stream = new(11UL);
            RandomStreamState before = stream.Capture();

            Assert.That(stream.NextExponential(rate), Is.NaN, "invalid input has no gap to return");

            Assert.That(stream.Capture(), Is.EqualTo(before), "no draw was spent");
        }

        [TestCase(0d, 1d, TestName = "The standard normal")]
        [TestCase(10d, 2d, TestName = "A shifted, scaled normal")]
        public void GaussianDrawsMatchTheirMeanAndSpread(double mean, double standardDeviation)
        {
            SfxRandomStream stream = new(33UL);
            double sum = 0d;
            double sumSquares = 0d;
            for (int index = 0; index < StatisticsDraws; ++index)
            {
                double draw = stream.NextGaussian(mean, standardDeviation);
                sum += draw;
                sumSquares += draw * draw;
            }

            double sampleMean = sum / StatisticsDraws;
            double sampleVariance = (sumSquares / StatisticsDraws) - (sampleMean * sampleMean);
            Assert.That(Math.Abs(sampleMean - mean), Is.LessThan((0.05d * standardDeviation) + 0.01d), "the mean matches");
            Assert.That(
                Math.Abs(sampleVariance - (standardDeviation * standardDeviation)),
                Is.LessThan((0.06d * standardDeviation * standardDeviation) + 0.01d),
                "the spread matches"
            );
        }

        [TestCase(0d, TestName = "A zero deviation")]
        [TestCase(-1d, TestName = "A negative deviation")]
        [TestCase(double.NaN, TestName = "A NaN deviation")]
        public void AConstantGaussianConsumesNoDraw(double standardDeviation)
        {
            SfxRandomStream stream = new(11UL);
            RandomStreamState before = stream.Capture();

            Assert.That(stream.NextGaussian(5d, standardDeviation), Is.EqualTo(5d), "a constant lands on its mean");

            Assert.That(stream.Capture(), Is.EqualTo(before), "no draw was spent");
        }

        [Test]
        public void TheGaussianSpareKeepsTheDrawCountStable()
        {
            SfxRandomStream stream = new(7UL);
            stream.NextGaussian(0d, 1d);
            RandomStreamState before = stream.Capture();

            double second = stream.NextGaussian(0d, 1d);
            RandomStreamState after = stream.Capture();

            Assert.That(double.IsInfinity(second) || double.IsNaN(second), Is.False, "the spare draw is a real number");
            Assert.That(SameWords(before, after), Is.True, "the spare draw spent no uniform");
            Assert.That(double.IsNaN(after.SpareGaussian), Is.True, "the spare was consumed");
        }

        [TestCase(0d, false, TestName = "Probability zero never passes")]
        [TestCase(1d, true, TestName = "Probability one always passes")]
        [TestCase(-0.5d, false, TestName = "A negative probability is treated as zero")]
        [TestCase(1.5d, true, TestName = "A probability above one is treated as one")]
        [TestCase(double.NaN, false, TestName = "A NaN probability is treated as zero")]
        public void AnEndpointProbabilityAnswersWithoutADraw(double probability, bool expected)
        {
            SfxRandomStream stream = new(11UL);
            RandomStreamState before = stream.Capture();

            for (int index = 0; index < 100; ++index)
            {
                Assert.That(stream.NextBoolSparing(probability), Is.EqualTo(expected));
            }

            Assert.That(stream.Capture(), Is.EqualTo(before), "no draw was spent");
        }

        [Test]
        public void AMiddleProbabilityDrawsAtItsStatedRate()
        {
            SfxRandomStream stream = new(5UL);
            const int Attempts = 10_000;
            int passes = 0;
            for (int index = 0; index < Attempts; ++index)
            {
                if (stream.NextBoolSparing(0.3d))
                {
                    ++passes;
                }
            }

            Assert.That((double)passes / Attempts, Is.EqualTo(0.3d).Within(0.02d), "the pass rate tracks the probability");
        }

        [Test]
        public void NextFloatStaysInsideItsRange()
        {
            SfxRandomStream stream = new(19UL);
            for (int index = 0; index < 10_000; ++index)
            {
                float draw = stream.NextFloat(-3f, 7f);
                Assert.That(-3f <= draw, Is.True, "a draw fell below the minimum");
                Assert.That(draw <= 7f, Is.True, "a draw rose above the maximum");
            }
        }

        [TestCase(2f, 2f, false, TestName = "An empty range")]
        [TestCase(2f, -2f, false, TestName = "An inverted range")]
        [TestCase(float.NaN, 2f, true, TestName = "A NaN minimum")]
        public void AFixedOrInvalidFloatRangeAnswersWithoutADraw(float min, float max, bool expectsNaN)
        {
            SfxRandomStream stream = new(11UL);
            RandomStreamState before = stream.Capture();

            float draw = stream.NextFloat(min, max);
            if (expectsNaN)
            {
                Assert.That(float.IsNaN(draw), Is.True, "a NaN input stays NaN");
            }
            else
            {
                Assert.That(draw, Is.EqualTo(min), "the fixed range returns its minimum");
            }

            Assert.That(stream.Capture(), Is.EqualTo(before), "no draw was spent");
        }

        [Test]
        public void CaptureThenRestoreReproducesTheSameFutureStream()
        {
            SfxRandomStream stream = new(42UL);
            for (int index = 0; index < 17; ++index)
            {
                stream.NextDouble();
            }

            RandomStreamState saved = stream.Capture();
            double[] future = DrawsFrom(stream, 64);
            stream.Restore(saved);

            Assert.That(DrawsFrom(stream, 64), Is.EqualTo(future), "the restored stream repeats its future");
        }

        [Test]
        public void ARestoredSpareCarriesThePendingGaussianDraw()
        {
            SfxRandomStream stream = new(23UL);
            stream.NextGaussian(0d, 1d); // an odd count of Gaussian calls leaves the spare pending
            RandomStreamState saved = stream.Capture();
            Assert.That(double.IsNaN(saved.SpareGaussian), Is.False, "the capture holds a pending spare");

            double[] future = DrawsFrom(stream, 32);

            // Spend the spare, so only the restore can bring it back.
            stream.NextGaussian(0d, 1d);
            stream.Restore(saved);

            Assert.That(double.IsNaN(stream.Capture().SpareGaussian), Is.False, "the restore brought the pending spare back");
            Assert.That(DrawsFrom(stream, 32), Is.EqualTo(future), "the restored stream repeats its future");
        }

        [Test]
        public void TheStructGeneratorMatchesItsPinnedVector()
        {
            // First output of the published xoshiro256** step for seed 42, computed independently from the reference algorithm.
            SfxXoshiro256StarStar generator = new(42UL);
            Assert.That(generator.NextUInt64(), Is.EqualTo(0xBE15272CDF80B6C2UL), "the published step's first output");
        }

        [Test]
        public void TheDefaultStructSeedsItselfOnItsFirstDraw()
        {
            SfxXoshiro256StarStar generator = default;
            SfxXoshiro256StarStar zeroSeeded = new(0UL);
            for (int index = 0; index < 100; ++index)
            {
                Assert.That(generator.NextUInt64(), Is.EqualTo(zeroSeeded.NextUInt64()), "the default struct draws like the zero-seeded one");
            }

            SfxRandomStream stream = new(0UL);
            SfxXoshiro256StarStar fresh = new(0UL);
            Assert.That(stream.NextDouble(), Is.EqualTo(fresh.NextDouble()), "the zero seed makes a working stream");
        }

        [Test]
        public void ACaptureBeforeTheFirstDrawIsRestorable()
        {
            SfxXoshiro256StarStar generator = default;
            RandomStreamState saved = generator.Capture();
            Assert.That(saved.S0 == 0UL && saved.S1 == 0UL && saved.S2 == 0UL && saved.S3 == 0UL, Is.False, "the capture holds the seeded position, not zeros");

            generator.NextUInt64();
            generator.Restore(saved);

            SfxXoshiro256StarStar reference = new(0UL);
            Assert.That(generator.NextUInt64(), Is.EqualTo(reference.NextUInt64()), "the restored default replays its first draw");
        }

        [Test]
        public void EqualStatesHashAlikeAndStably()
        {
            RandomStreamState state = new(1UL, 2UL, 3UL, 4UL, 0.5d);
            int hash = state.GetHashCode();
            int nanHash = new RandomStreamState(1UL, 2UL, 3UL, 4UL, double.NaN).GetHashCode();
            SfxXoshiro256StarStar generator = new(7UL);
            int generatorHash = generator.GetHashCode();

            Assert.That(state.GetHashCode(), Is.EqualTo(hash), "one state keeps its hash");
            Assert.That(new RandomStreamState(1UL, 2UL, 3UL, 4UL, 0.5d).GetHashCode(), Is.EqualTo(hash), "equal states hash alike");
            Assert.That(new RandomStreamState(1UL, 2UL, 3UL, 4UL, double.NaN).GetHashCode(), Is.EqualTo(nanHash), "NaN spares hash alike");
            Assert.That(new SfxXoshiro256StarStar(7UL).GetHashCode(), Is.EqualTo(generatorHash), "equal generators hash alike");
        }

        [TestCase(double.NaN, 0.5d, TestName = "A NaN first uniform")]
        [TestCase(0.5d, double.NaN, TestName = "A NaN second uniform")]
        public void TheGaussianPairRejectsAnUnusableUniform(double firstUniform, double secondUniform)
        {
            SfxRandomMath.GaussianPairFromUniform(firstUniform, secondUniform, 5d, 2d, out double first, out double second);
            Assert.That(first, Is.EqualTo(5d), "the first draw falls back to the mean");
            Assert.That(second, Is.EqualTo(5d), "the second draw falls back to the mean");
        }

        [Test]
        public void NextFloatStaysFiniteAcrossTheWholeFloatRange()
        {
            SfxRandomStream stream = new(31UL);
            for (int index = 0; index < 10_000; ++index)
            {
                float draw = stream.NextFloat(float.MinValue, float.MaxValue);
                Assert.That(float.IsNaN(draw) || float.IsInfinity(draw), Is.False, "the whole range stayed finite");
            }
        }

        [Test]
        public void AnAllZeroStateIsRefusedWithoutMovingThePosition()
        {
            SfxRandomStream stream = new(13UL);
            stream.NextGaussian(0d, 1d); // a pending spare must survive a refused restore
            RandomStreamState before = stream.Capture();

            stream.Restore(default);

            Assert.That(stream.Capture(), Is.EqualTo(before), "the refused restore changed nothing");

            SfxXoshiro256StarStar generator = new(13UL);
            generator.NextUInt64();
            RandomStreamState saved = generator.Capture();

            generator.Restore(default);

            Assert.That(generator.Capture(), Is.EqualTo(saved), "the struct refused the zeroed state");
        }

        [Test]
        public void AForkCarriesItsOwnDeterministicFuture()
        {
            SfxRandomStream stream = new(99UL);
            for (int index = 0; index < 13; ++index)
            {
                stream.NextDouble();
            }

            ISfxRandomStream forkA = stream.Fork();
            ISfxRandomStream forkB = stream.Fork();
            double[] forkFuture = DrawsFrom(forkA, 64);
            double[] twinFuture = DrawsFrom(forkB, 64);
            double[] parentFuture = DrawsFrom(stream, 64);
            Assert.That(twinFuture, Is.EqualTo(forkFuture), "forks taken at the same position agree");
            Assert.That(parentFuture, Is.Not.EqualTo(forkFuture), "a fork does not follow its parent");
        }

        [Test]
        public void TheStructGeneratorFollowsThePublishedConversion()
        {
            SfxXoshiro256StarStar structGenerator = new(42UL);
            SfxRandomStream stream = new(42UL);
            for (int index = 0; index < 1_000; ++index)
            {
                double expected = (structGenerator.NextUInt64() >> 11) * (1d / 9007199254740992d);
                Assert.That(stream.NextDouble(), Is.EqualTo(expected), "the audio-path struct and the stream share one sequence");
            }

            RandomStreamState saved = structGenerator.Capture();
            structGenerator.NextUInt64();
            structGenerator.Restore(saved);
            Assert.That(structGenerator.Capture(), Is.EqualTo(saved), "the struct restores its own state");
        }
    }
}
