namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Closes on quiet audio and applies the same gain to every channel in a frame.</summary>
    public sealed class DspNoiseGate : DspEffect
    {
        public const float MinimumThresholdDecibels = -72f;
        public const float DefaultThresholdDecibels = -40f;
        public const float HysteresisDecibels = 6f;
        public const float MaximumTimeSeconds = 2f;
        public const float DefaultAttackSeconds = 0.002f;
        public const float DefaultReleaseSeconds = 0.08f;

        private const double GainTolerance = 0.00001d;
        private static readonly float CloseThresholdRatio = AudioMath.DecibelsToLinear(-HysteresisDecibels);

        public override string DisplayName => "Noise Gate";
        public override float GainReductionDecibels => AudioMath.LinearToDecibels(_lastGain);

        /// <summary>The peak level that opens the gate. It closes 6 dB below this level.</summary>
        public float ThresholdDecibels
        {
            get => _thresholdDecibels;
            set
            {
                _thresholdDecibels = ClampThreshold(value);
                _openThreshold = AudioMath.DecibelsToLinear(_thresholdDecibels);
            }
        }

        /// <summary>The opening time constant in seconds. Zero opens at once.</summary>
        public float AttackSeconds
        {
            get => _attackSeconds;
            set
            {
                _attackSeconds = ClampTime(value, DefaultAttackSeconds);
                _attackCoefficient = AudioMath.OnePoleCoefficient(_attackSeconds, _sampleRate);
            }
        }

        /// <summary>The closing time constant in seconds. Zero closes at once.</summary>
        public float ReleaseSeconds
        {
            get => _releaseSeconds;
            set
            {
                _releaseSeconds = ClampTime(value, DefaultReleaseSeconds);
                _releaseCoefficient = AudioMath.OnePoleCoefficient(_releaseSeconds, _sampleRate);
            }
        }

        private float _thresholdDecibels = DefaultThresholdDecibels;
        private float _attackSeconds = DefaultAttackSeconds;
        private float _releaseSeconds = DefaultReleaseSeconds;
        private float _openThreshold;
        private float _attackCoefficient;
        private float _releaseCoefficient;
        private int _sampleRate;
        private int _channels;
        private bool _open;

        // Keep the envelope in double so a slow attack can reach full gain at high sample rates.
        private double _gain;
        private float _lastGain;

        internal static float ClampThreshold(float value)
        {
            return AudioMath.IsFinite(value) ? AudioMath.Clamp(value, MinimumThresholdDecibels, 0f) : DefaultThresholdDecibels;
        }

        internal static float ClampTime(float value, float fallback)
        {
            return AudioMath.IsFinite(value) ? AudioMath.Clamp(value, 0f, MaximumTimeSeconds) : fallback;
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            float openThreshold = _openThreshold;
            float closeThreshold = openThreshold * CloseThresholdRatio;
            float attack = _attackCoefficient;
            float release = _releaseCoefficient;
            bool open = _open;
            double gain = _gain;
            int end = offset + count;
            for (int frame = offset; frame < end; frame += channels)
            {
                float threshold = open ? closeThreshold : openThreshold;
                int frameEnd = frame + channels;
                open = false;
                for (int sample = frame; sample < frameEnd; ++sample)
                {
                    if (threshold <= Math.Abs(buffer[sample]))
                    {
                        open = true;
                        break;
                    }
                }

                double target = open ? 1d : 0d;
                float coefficient = open ? attack : release;
                gain += (target - gain) * coefficient;
                if (Math.Abs(target - gain) < GainTolerance)
                {
                    // Finish the ramp before the gain reaches the denormal range.
                    gain = target;
                }

                float frameGain = (float)gain;
                for (int sample = frame; sample < frameEnd; ++sample)
                {
                    buffer[sample] *= frameGain;
                }
            }

            _open = open;
            _gain = gain;
            _lastGain = (float)gain;
        }

        public override void Reset()
        {
            _open = false;
            _gain = 0d;
            _lastGain = 0f;
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _openThreshold = AudioMath.DecibelsToLinear(_thresholdDecibels);
            _attackCoefficient = AudioMath.OnePoleCoefficient(_attackSeconds, sampleRate);
            _releaseCoefficient = AudioMath.OnePoleCoefficient(_releaseSeconds, sampleRate);
            Reset();
        }
    }
}
