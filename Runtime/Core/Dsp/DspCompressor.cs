namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Turns a signal down once it passes a threshold, by an amount that grows with the ratio.</summary>
    /// <remarks>
    /// A ratio of four means four decibels over the threshold leave as one: the loud part gets quieter without
    /// the quiet part moving. Attack and release smooth the gain, so a sudden hit is caught over a few
    /// milliseconds and does not choke, and the level comes back without pumping.
    ///
    /// The detector reads the peak of each frame, not the loudness of a passage, so it reacts to transients the
    /// way a drum hit needs. Makeup gain is applied after the reduction, which is where a mix uses it.
    /// </remarks>
    public sealed class DspCompressor : DspEffect
    {
        public const float DefaultThresholdDecibels = -24f;
        public const float DefaultRatio = 4f;

        private const float MinimumMakeupDecibels = -60f;
        private const float MaximumMakeupDecibels = 24f;

        private float _thresholdDecibels;
        private float _ratio;
        private float _attackSeconds;
        private float _releaseSeconds;
        private float _makeupDecibels;
        private int _sampleRate;
        private float _thresholdLinear;
        private float _makeupLinear;
        private float _attackCoefficient;
        private float _releaseCoefficient;
        private int _channels;
        private float _gainDecibels;

        public DspCompressor()
        {
            _thresholdDecibels = DefaultThresholdDecibels;
            _ratio = DefaultRatio;
            _attackSeconds = 0.005f;
            _releaseSeconds = 0.1f;
            _makeupDecibels = 0f;
            Recompute();
        }

        public override string DisplayName => "Compressor";

        /// <summary>The level where reduction starts, in dBFS.</summary>
        public float ThresholdDecibels
        {
            get => _thresholdDecibels;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _thresholdDecibels = value;
                    Recompute();
                }
            }
        }

        /// <summary>Decibels over the threshold for each decibel that leaves, clamped to one or more.</summary>
        public float Ratio
        {
            get => _ratio;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _ratio = AudioMath.Clamp(value, 1f, 100f);
                    Recompute();
                }
            }
        }

        /// <summary>How quickly the gain follows a louder signal, in seconds; zero follows at once.</summary>
        public float AttackSeconds
        {
            get => _attackSeconds;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _attackSeconds = Math.Max(0f, value);
                    Recompute();
                }
            }
        }

        /// <summary>How quickly the gain recovers after a quieter signal, in seconds.</summary>
        public float ReleaseSeconds
        {
            get => _releaseSeconds;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _releaseSeconds = Math.Max(0f, value);
                    Recompute();
                }
            }
        }

        /// <summary>Gain added after the reduction, in decibels.</summary>
        /// <remarks>
        /// A lift is a gain, so makeup can take a full-scale input above full scale; that is what the knob is for.
        /// A ceiling belongs to a limiter after the compressor, not to the compressor.
        /// </remarks>
        public float MakeupDecibels
        {
            get => _makeupDecibels;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _makeupDecibels = AudioMath.Clamp(value, MinimumMakeupDecibels, MaximumMakeupDecibels);
                    Recompute();
                }
            }
        }

        /// <summary>How far the gain is currently held down, in decibels; zero is untouched.</summary>
        public float GainReductionDecibels => _gainDecibels;

        public override void Prepare(int sampleRate, int maxBlockSize, int channels)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (channels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            _sampleRate = sampleRate;
            _channels = channels;
            Recompute();
            Reset();
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            int stride = channels;

            int frames = count / stride;
            float slope = 1f - (1f / _ratio);
            for (int frame = 0; frame < frames; frame++)
            {
                int frameStart = offset + (frame * stride);
                float peak = 0f;
                for (int channel = 0; channel < stride; channel++)
                {
                    float magnitude = Math.Abs(buffer[frameStart + channel]);
                    if (peak < magnitude)
                    {
                        peak = magnitude;
                    }
                }

                float targetDecibels = 0f;
                if (_thresholdLinear < peak)
                {
                    float overDecibels = AudioMath.LinearToDecibels(peak) - _thresholdDecibels;
                    targetDecibels = -overDecibels * slope;
                }

                // The time constants are decibels, so the attack reaches its reduction in the time its name says.
                // A linear gain would arrive at half of it, because 15 dB is not halfway toward a gain of zero.
                float coefficient = targetDecibels < _gainDecibels ? _attackCoefficient : _releaseCoefficient;
                _gainDecibels += (targetDecibels - _gainDecibels) * coefficient;
                float gain = AudioMath.DecibelsToLinear(_gainDecibels) * _makeupLinear;
                for (int channel = 0; channel < stride; channel++)
                {
                    int index = frameStart + channel;
                    buffer[index] *= gain;
                }
            }
        }

        public override void Reset()
        {
            _gainDecibels = 0f;
        }

        private void Recompute()
        {
            _thresholdLinear = AudioMath.DecibelsToLinear(_thresholdDecibels);
            _makeupLinear = (float)Math.Pow(10.0, _makeupDecibels / 20.0);
            _attackCoefficient = AudioMath.OnePoleCoefficient(_attackSeconds, _sampleRate);
            _releaseCoefficient = AudioMath.OnePoleCoefficient(_releaseSeconds, _sampleRate);
        }
    }
}
