namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Sweeps two cancellation notches by mixing four all-pass sections with the dry signal.</summary>
    public sealed class DspPhaser : DspEffect
    {
        public const float MinimumRateHertz = 0.05f;
        public const float MaximumRateHertz = 5f;
        public const float DefaultRateHertz = 0.5f;
        public const float DefaultDepth = 0.75f;

        private const double LowCornerHertz = 200d;
        private const double HighCornerHertz = 2_000d;
        private const int MaximumSweepFrames = 64;

        public override string DisplayName => "Phaser";

        /// <summary>Sweep cycles per second. Changes apply at the next preparation.</summary>
        public float RateHertz
        {
            get => _rateHertz;
            set => _rateHertz = ClampRate(value);
        }

        /// <summary>Zero leaves the signal dry. One mixes equal dry and filtered signals. Changes apply at the next preparation.</summary>
        public float Depth
        {
            get => _depth;
            set => _depth = ClampDepth(value);
        }

        private float _rateHertz = DefaultRateHertz;
        private float _depth = DefaultDepth;
        private float _wet;
        private float _lowCoefficient;
        private float _coefficientSpan;
        private double _sweepPhaseStep;
        private int _sweepFrames;
        private SweepState _sweep;
        private ChannelHistory[] _history = Array.Empty<ChannelHistory>();

        internal static float ClampRate(float value)
        {
            return AudioMath.ClampFinite(value, MinimumRateHertz, MaximumRateHertz, DefaultRateHertz);
        }

        internal static float ClampDepth(float value)
        {
            return AudioMath.ClampFinite(value, 0f, 1f, DefaultDepth);
        }

        private static float Coefficient(double normalizedCorner)
        {
            double tangent = Math.Tan(Math.PI * normalizedCorner);
            return (float)((tangent - 1d) / (tangent + 1d));
        }

        private static float FlushTinyHistory(float value)
        {
            return Math.Abs(value) < 1e-20f ? 0f : value;
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            ChannelHistory[] history = _history;
            float wet = _wet;
            if (wet == 0f || buffer == null || channels <= 0 || channels != history.Length || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            SweepState sweep = _sweep;
            float dry = 1f - wet;
            int totalFrames = count / channels;
            // Adjacent channels share one sweep and keep independent filter histories.
            int pairs = channels / 2 + channels % 2;
            for (int pair = 0; pair < pairs; ++pair)
            {
                int channel = pair * 2;
                // In an odd layout the last pair reads the same channel twice.
                sweep = _sweep;
                int pairedChannel = Math.Min(channel + 1, channels - 1);
                float leftFirst = history[channel].First;
                float leftSecond = history[channel].Second;
                float leftThird = history[channel].Third;
                float leftFourth = history[channel].Fourth;
                float rightFirst = history[pairedChannel].First;
                float rightSecond = history[pairedChannel].Second;
                float rightThird = history[pairedChannel].Third;
                float rightFourth = history[pairedChannel].Fourth;
                // Advance before each read so the cursors stop on the last valid pair.
                int sample = offset + channel - channels;
                int pairedSample = offset + pairedChannel - channels;
                int framesRemaining = totalFrames;
                while (0 < framesRemaining)
                {
                    if (sweep.FramesLeft == 0)
                    {
                        AdvanceSweep(ref sweep);
                    }

                    int frames = Math.Min(sweep.FramesLeft, framesRemaining);
                    framesRemaining -= frames;
                    int stop = sample + channels * frames;
                    float coefficient = sweep.Coefficient;
                    float step = sweep.Step;
                    do
                    {
                        sample += channels;
                        pairedSample += channels;
                        ref float leftSample = ref buffer[sample];
                        ref float rightSample = ref buffer[pairedSample];
                        float left = leftSample;
                        float right = rightSample;

                        // Each first-order section keeps one delayed value.
                        float left1 = coefficient * left + leftFirst;
                        float right1 = coefficient * right + rightFirst;
                        leftFirst = left - coefficient * left1;
                        rightFirst = right - coefficient * right1;
                        float left2 = coefficient * left1 + leftSecond;
                        float right2 = coefficient * right1 + rightSecond;
                        leftSecond = left1 - coefficient * left2;
                        rightSecond = right1 - coefficient * right2;
                        float left3 = coefficient * left2 + leftThird;
                        float right3 = coefficient * right2 + rightThird;
                        leftThird = left2 - coefficient * left3;
                        rightThird = right2 - coefficient * right3;
                        float left4 = coefficient * left3 + leftFourth;
                        float right4 = coefficient * right3 + rightFourth;
                        leftFourth = left3 - coefficient * left4;
                        rightFourth = right3 - coefficient * right4;
                        leftSample = dry * left + wet * left4;
                        rightSample = dry * right + wet * right4;
                        coefficient += step;
                    } while (sample < stop);

                    sweep.Coefficient = coefficient;
                    sweep.FramesLeft -= frames;
                    if (sweep.FramesLeft == 0)
                    {
                        leftFirst = FlushTinyHistory(leftFirst);
                        leftSecond = FlushTinyHistory(leftSecond);
                        leftThird = FlushTinyHistory(leftThird);
                        leftFourth = FlushTinyHistory(leftFourth);
                        rightFirst = FlushTinyHistory(rightFirst);
                        rightSecond = FlushTinyHistory(rightSecond);
                        rightThird = FlushTinyHistory(rightThird);
                        rightFourth = FlushTinyHistory(rightFourth);
                    }
                }

                history[channel] = new ChannelHistory
                {
                    First = leftFirst,
                    Second = leftSecond,
                    Third = leftThird,
                    Fourth = leftFourth,
                };
                history[pairedChannel] = new ChannelHistory
                {
                    First = rightFirst,
                    Second = rightSecond,
                    Third = rightThird,
                    Fourth = rightFourth,
                };
            }

            _sweep = sweep;
        }

        public override void Reset()
        {
            Array.Clear(_history, 0, _history.Length);
            _sweep = new SweepState { Target = _lowCoefficient };
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            // Scale both corners together at low sample rates to keep the high corner below Nyquist.
            double highCorner = Math.Min(HighCornerHertz / sampleRate, 0.2d);
            _lowCoefficient = Coefficient(highCorner * (LowCornerHertz / HighCornerHertz));
            _coefficientSpan = Coefficient(highCorner) - _lowCoefficient;
            // Shorter low-rate spans clear history more often because those tails can decay faster.
            int maximumFrames = sampleRate < 32_000 ? 16 : MaximumSweepFrames;
            _sweepFrames = Math.Max(1, Math.Min(maximumFrames, sampleRate / (int)(16 * MaximumRateHertz)));
            _sweepPhaseStep = ((double)_rateHertz * _sweepFrames % sampleRate) / sampleRate;
            _wet = 0.5f * _depth;
            if (_history.Length != channels)
            {
                _history = new ChannelHistory[channels];
            }

            Reset();
        }

        private void AdvanceSweep(ref SweepState sweep)
        {
            // Anchor each ramp to the triangle and keep its position across partial blocks.
            sweep.Phase += _sweepPhaseStep;
            if (1d <= sweep.Phase)
            {
                sweep.Phase -= 1d;
            }

            sweep.Coefficient = sweep.Target;
            float triangle = (float)(1d - Math.Abs(1d - 2d * sweep.Phase));
            sweep.Target = _lowCoefficient + _coefficientSpan * triangle;
            sweep.Step = (sweep.Target - sweep.Coefficient) / _sweepFrames;
            sweep.FramesLeft = _sweepFrames;
        }

        private struct SweepState
        {
            public double Phase;
            public float Target;
            public float Coefficient;
            public float Step;
            public int FramesLeft;
        }

        private struct ChannelHistory
        {
            public float First;
            public float Second;
            public float Third;
            public float Fourth;
        }
    }
}
