namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class DspDistortionTimingTests
    {
        private const int Voices = 8;
        private const int Variants = 4;
        private const int Passes = DspRenderWorkload.Passes;
        private const float Drive = 20f;
        private const float ToneHz = 3_000f;

        // Rotating this order balances each position and immediate predecessor within a pass.
        private static readonly int[] PassOrder = { 0, 1, 3, 2 };
        private static readonly string[] Names = { "empty", "linear shaper", "fixed controls", "production" };

        private static double Median(long[] sorted)
        {
            int count = sorted.Length;
            return (sorted[(count - 1) / 2] + (double)sorted[count / 2]) * 0.5d;
        }

        [Test]
        public void RecordDistortionStageCosts()
        {
            DspRenderWorkload[] workloads =
            {
                new(Voices, "Distortion", bursts: false, enabled: false),
                new(Voices, "Distortion", bursts: false, enabled: true, createEffect: () => new FixedControlDistortion(linear: true)),
                new(Voices, "Distortion", bursts: false, enabled: true, createEffect: () => new FixedControlDistortion(linear: false)),
                new(Voices, "Distortion", bursts: false, enabled: true),
            };

            long[,] elapsed = new long[Passes, Variants];
            ulong?[,] cycles = new ulong?[Passes, Variants];
            void MeasurePass(int pass, int record)
            {
                for (int position = 0; position < Variants; ++position)
                {
                    int variant = (PassOrder[position] + pass) % Variants;
                    (elapsed[record, variant], cycles[record, variant]) = DspHostTests.MeasureDspRenderPass(workloads[variant], Voices);
                }
            }

            for (int pass = 0; pass < DspRenderWorkload.WarmupPasses; ++pass)
            {
                MeasurePass(pass, 0);
            }

            ReadOnlySpan<float> production = workloads[3].Output;
            ReadOnlySpan<float> fixedControls = workloads[2].Output;
            ReadOnlySpan<float> linearShaper = workloads[1].Output;
            bool differsFromLinear = false;
            int sampleCount = production.Length;
            for (int sample = 0; sample < sampleCount; ++sample)
            {
                Assert.That(fixedControls[sample], Is.EqualTo(production[sample]).Within(1e-6f), "The prepared controls must already be settled.");
                differsFromLinear = differsFromLinear || 0.1f < Math.Abs(production[sample] - linearShaper[sample]);
            }

            Assert.That(differsFromLinear, Is.True, "The workload must exercise nonlinear shaping.");
            Assert.That(Passes % Variants, Is.Zero, "Every workload must run equally often in each position.");
            for (int pass = 0; pass < Passes; ++pass)
            {
                MeasurePass(pass, pass);
            }

            TestContext.Out.WriteLine(
                $"DxAudio Distortion timing: {Voices} stereo voices, {DspRenderWorkload.Rate} Hz, {DspRenderWorkload.Frames} frames, "
                    + $"{DspRenderWorkload.WarmupPasses} warmups, {Passes} passes on {SystemInfo.processorType}. Stopwatch frequency {Stopwatch.Frequency} Hz."
            );

            double tickMicroseconds = 1_000_000d / Stopwatch.Frequency;
            for (int variant = 0; variant < Variants; ++variant)
            {
                long[] ticks = new long[Passes];
                long[] differences = new long[Passes];
                int reference = Math.Max(0, variant - 1);
                for (int pass = 0; pass < Passes; ++pass)
                {
                    ticks[pass] = elapsed[pass, variant];
                    differences[pass] = ticks[pass] - elapsed[pass, reference];
                }

                Array.Sort(ticks);
                Array.Sort(differences);
                double median = Median(ticks) * tickMicroseconds;
                double p95 = ticks[(95 * Passes + 99) / 100 - 1] * tickMicroseconds;
                double pairedMedian = Median(differences) * tickMicroseconds;
                TestContext.Out.WriteLine(
                    FormattableString.Invariant(
                        $"DxAudio Distortion timing: {variant} {Names[variant]}, p50 {median:F2} us, p95 {p95:F2} us, median paired difference from {Names[reference]} {pairedMedian:F2} us."
                    )
                );
            }

            TestContext.Out.WriteLine("DxAudio Distortion samples: pass followed by variant/ticks/cycles in execution order. A dash indicates unavailable cycles.");
            StringBuilder row = new();
            for (int pass = 0; pass < Passes; ++pass)
            {
                row.Clear();
                row.Append("DxAudio Distortion samples: ").Append(pass);
                for (int position = 0; position < Variants; ++position)
                {
                    int variant = (PassOrder[position] + pass) % Variants;
                    row.Append(',').Append(variant).Append('/').Append(elapsed[pass, variant]).Append('/');
                    ulong? measuredCycles = cycles[pass, variant];
                    if (measuredCycles.HasValue)
                    {
                        row.Append(measuredCycles.Value);
                    }
                    else
                    {
                        row.Append('-');
                    }
                }

                TestContext.Out.WriteLine(row.ToString());
            }
        }

        /*
         * Match the production stereo history and tone coefficient with the controls held at their prepared values.
         * The linear control removes tanh while keeping drive, the filter, the writes, and the same chain dispatch.
         */
        private sealed class FixedControlDistortion : DspEffect
        {
            public override string DisplayName => "Distortion timing control";

            private readonly bool _linear;
            private float _left;
            private float _right;
            private float _tone;

            internal FixedControlDistortion(bool linear)
            {
                _linear = linear;
            }

            public override void Process(float[] buffer, int offset, int count, int channels)
            {
                float left = _left;
                float right = _right;
                float tone = _tone;
                int end = offset + count;
                if (_linear)
                {
                    for (int index = offset; index < end; index += 2)
                    {
                        ref float leftSample = ref buffer[index];
                        ref float rightSample = ref buffer[index + 1];
                        float shapedLeft = Drive * leftSample;
                        float shapedRight = Drive * rightSample;
                        left += (shapedLeft - left) * tone;
                        right += (shapedRight - right) * tone;
                        leftSample = left;
                        rightSample = right;
                    }
                }
                else
                {
                    for (int index = offset; index < end; index += 2)
                    {
                        ref float leftSample = ref buffer[index];
                        ref float rightSample = ref buffer[index + 1];
                        float shapedLeft = (float)Math.Tanh(Drive * leftSample);
                        float shapedRight = (float)Math.Tanh(Drive * rightSample);
                        left += (shapedLeft - left) * tone;
                        right += (shapedRight - right) * tone;
                        leftSample = left;
                        rightSample = right;
                    }
                }

                _left = left;
                _right = right;
            }

            public override void Reset()
            {
                _left = 0f;
                _right = 0f;
            }

            protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
            {
                _tone = AudioMath.OnePoleCoefficient(1f / (2f * (float)Math.PI * ToneHz), sampleRate);
                Reset();
            }
        }
    }
}
