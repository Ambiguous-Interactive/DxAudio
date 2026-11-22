namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Applies a shared triangle gain sweep to all channels.</summary>
    public sealed class DspTremolo : DspEffect
    {
        public const float MinimumRateHertz = 0.05f;
        public const float MaximumRateHertz = 20f;
        public const float DefaultRateHertz = 4f;
        public const float DefaultDepth = 0.5f;

        public override string DisplayName => "Tremolo";

        public override float GainReductionDecibels => AudioMath.LinearToDecibels(_lastGain);

        /// <summary>The gain cycles per second. Changes apply at the next preparation.</summary>
        public float RateHertz
        {
            get => _rateHertz;
            set => _rateHertz = ClampRate(value);
        }

        /// <summary>The largest drop from full gain, from zero to one. Changes apply at the next preparation.</summary>
        public float Depth
        {
            get => _depth;
            set => _depth = ClampDepth(value);
        }

        private float _rateHertz = DefaultRateHertz;
        private float _depth = DefaultDepth;
        private float _preparedDepth;
        private int _channels;
        private double _phaseStep;
        private double _phase;
        private float _lastGain = 1f;

        internal static float ClampRate(float value)
        {
            return AudioMath.ClampFinite(value, MinimumRateHertz, MaximumRateHertz, DefaultRateHertz);
        }

        internal static float ClampDepth(float value)
        {
            return AudioMath.ClampFinite(value, 0f, 1f, DefaultDepth);
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (
                _preparedDepth == 0f
                || buffer == null
                || channels <= 0
                || channels != _channels
                || offset < 0
                || count <= 0
                || count % channels != 0
                || buffer.Length - count < offset
            )
            {
                return;
            }

            double phase = _phase;
            double phaseStep = _phaseStep;
            double depth = _preparedDepth;
            float gain = 1f;
            int end = offset + count;
            for (int frame = offset; frame < end; frame += channels)
            {
                gain = (float)(1d - depth * (1d - Math.Abs(1d - 2d * phase)));
                int frameEnd = frame + channels;
                for (int sample = frame; sample < frameEnd; ++sample)
                {
                    buffer[sample] *= gain;
                }

                phase += phaseStep;
                if (1d <= phase)
                {
                    phase -= 1d;
                }
            }

            _phase = phase;
            _lastGain = gain;
        }

        public override void Reset()
        {
            _phase = 0d;
            _lastGain = 1f;
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            _channels = channels;
            _preparedDepth = _depth;
            _phaseStep = ((double)_rateHertz % sampleRate) / sampleRate;
            Reset();
        }
    }
}
