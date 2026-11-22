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
        private const int HistoryFlushMask = 15;

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
        private double _phase;
        private double _phaseStep;
        private int _flushFrame;
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

            double phase = _phase;
            int flushFrame = _flushFrame;
            double phaseStep = _phaseStep;
            float lowCoefficient = _lowCoefficient;
            float coefficientSpan = _coefficientSpan;
            float dry = 1f - wet;
            int end = offset + count;
            for (int channel = 0; channel < channels; ++channel)
            {
                phase = _phase;
                flushFrame = _flushFrame;
                ref ChannelHistory state = ref history[channel];
                float first = state.First;
                float second = state.Second;
                float third = state.Third;
                float fourth = state.Fourth;
                for (int frame = offset; frame < end; frame += channels)
                {
                    int sample = frame + channel;
                    float sweep = (float)(1d - Math.Abs(1d - 2d * phase));
                    float coefficient = lowCoefficient + coefficientSpan * sweep;
                    float input = buffer[sample];

                    // Each first-order section uses the same coefficient and keeps one delayed value.
                    float output1 = coefficient * input + first;
                    first = input - coefficient * output1;
                    float output2 = coefficient * output1 + second;
                    second = output1 - coefficient * output2;
                    float output3 = coefficient * output2 + third;
                    third = output2 - coefficient * output3;
                    float output4 = coefficient * output3 + fourth;
                    fourth = output3 - coefficient * output4;
                    buffer[sample] = dry * input + wet * output4;

                    // The cadence follows the stream across blocks and clears tiny tails before they reach denormals.
                    flushFrame = (flushFrame + 1) & HistoryFlushMask;
                    if (flushFrame == 0)
                    {
                        first = FlushTinyHistory(first);
                        second = FlushTinyHistory(second);
                        third = FlushTinyHistory(third);
                        fourth = FlushTinyHistory(fourth);
                    }

                    phase += phaseStep;
                    if (1d <= phase)
                    {
                        phase -= 1d;
                    }
                }

                state.First = first;
                state.Second = second;
                state.Third = third;
                state.Fourth = fourth;
            }

            _phase = phase;
            _flushFrame = flushFrame;
        }

        public override void Reset()
        {
            Array.Clear(_history, 0, _history.Length);
            _phase = 0d;
            _flushFrame = 0;
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            // Scale both corners together at low sample rates to keep the high corner below Nyquist.
            double highCorner = Math.Min(HighCornerHertz / sampleRate, 0.2d);
            _lowCoefficient = Coefficient(highCorner * (LowCornerHertz / HighCornerHertz));
            _coefficientSpan = Coefficient(highCorner) - _lowCoefficient;
            _phaseStep = ((double)_rateHertz % sampleRate) / sampleRate;
            _wet = 0.5f * _depth;
            if (_history.Length != channels)
            {
                _history = new ChannelHistory[channels];
            }

            Reset();
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
