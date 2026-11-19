namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Shapes the spectrum around one corner: a low pass, high pass, band pass, notch or peak.</summary>
    /// <remarks>
    /// The core is a topology-preserving state-variable filter, so the same two integrators answer every mode and the
    /// filter stays stable while a knob moves. A low pass keeps everything below the corner and rolls off above it, a
    /// high pass does the reverse, a band pass keeps a window around the corner, a notch removes it, and a peak lifts or
    /// cuts it by <see cref="PeakDecibels" /> while leaving the rest alone.
    ///
    /// <see cref="FrequencyHz" />, <see cref="Quality" /> and <see cref="PeakDecibels" /> are read every frame and
    /// reached over a <see cref="SmoothingSeconds" /> time constant, because a corner that jumps clicks. The mode is a
    /// choice of output rather than a coefficient: it is read once per block and moves no poles, but the output itself
    /// steps, so switch it at a quiet moment. A non-finite sample latches both integrators until <see cref="Reset" />,
    /// because the core feeds its own state back.
    /// </remarks>
    public sealed class DspFilter : DspEffect
    {
        /// <summary>The corner a new filter starts at, in hertz.</summary>
        public const float DefaultFrequencyHz = 1_000f;

        /// <summary>A quality that gives a flat passband and no lift at the corner, the usual starting point.</summary>
        public const float DefaultQuality = 0.7071f;

        /// <summary>The highest corner an author may choose, in hertz; a lower rate caps it further.</summary>
        public const float MaximumFrequencyHz = 20_000f;

        /// <summary>The lowest corner a filter accepts, in hertz.</summary>
        public const float MinimumFrequencyHz = 10f;

        /// <summary>The lowest quality a filter accepts, which makes the widest band.</summary>
        public const float MinimumQuality = 0.1f;

        /// <summary>The highest quality a filter accepts, which makes the narrowest band.</summary>
        public const float MaximumQuality = 20f;

        /// <summary>The lowest peak gain a filter accepts, in decibels.</summary>
        public const float MinimumPeakDecibels = -24f;

        /// <summary>The highest peak gain a filter accepts, in decibels.</summary>
        public const float MaximumPeakDecibels = 24f;

        /// <summary>How long a knob move takes to close about 63% of its step, in seconds.</summary>
        private const float SmoothingSeconds = 0.01f;

        private DspFilterMode _mode;
        private float _frequencyHz;
        private float _quality;
        private float _peakDecibels;
        private float _targetG;
        private float _targetK;
        private float _targetPeakGain;
        private float _activeG;
        private float _activeK;
        private float _activePeakGain;
        private float _smoothingCoefficient;
        private float[] _integratorOne;
        private float[] _integratorTwo;
        private int _sampleRate;
        private int _channels;

        public DspFilter()
        {
            _mode = DspFilterMode.LowPass;
            _frequencyHz = DefaultFrequencyHz;
            _quality = DefaultQuality;
            _peakDecibels = 0f;
            _targetG = 0f;
            _targetK = 1f / DefaultQuality;
            _targetPeakGain = 1f;
            _activeG = 0f;
            _activeK = _targetK;
            _activePeakGain = 1f;
            _smoothingCoefficient = 1f;
            _integratorOne = Array.Empty<float>();
            _integratorTwo = Array.Empty<float>();
        }

        public override string DisplayName => "Filter";

        /// <summary>Which part of the spectrum the stage keeps.</summary>
        public DspFilterMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        /// <summary>The corner of the band, in hertz, capped below the Nyquist rate of the prepared block.</summary>
        public float FrequencyHz
        {
            get => _frequencyHz;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _frequencyHz = Math.Max(MinimumFrequencyHz, value);
                    RecomputeTargets();
                }
            }
        }

        /// <summary>
        /// How narrow the band is: higher narrows it and lifts the corner of a low pass or a high pass, lower is wider
        /// and gentler.
        /// </summary>
        /// <remarks>
        /// A band pass stays at unity in its centre and a notch stays a null at every quality; those two only narrow.
        /// </remarks>
        public float Quality
        {
            get => _quality;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _quality = AudioMath.Clamp(value, MinimumQuality, MaximumQuality);
                    _targetK = 1f / _quality;
                }
            }
        }

        /// <summary>How far <see cref="DspFilterMode.Peak" /> lifts the band, in decibels; every other mode ignores it.</summary>
        public float PeakDecibels
        {
            get => _peakDecibels;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _peakDecibels = AudioMath.Clamp(value, MinimumPeakDecibels, MaximumPeakDecibels);
                    _targetPeakGain = AudioMath.DecibelsToLinear(_peakDecibels);
                }
            }
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _integratorOne = new float[channels];
            _integratorTwo = new float[channels];
            _smoothingCoefficient = AudioMath.OnePoleCoefficient(SmoothingSeconds, sampleRate);
            RecomputeTargets();
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

            // A knob move is a ramp, not a step: each frame follows its target a little, so a sweep does not click.
            float g = _activeG;
            float k = _activeK;
            float peakGain = _activePeakGain;
            float smoothing = _smoothingCoefficient;
            DspFilterMode mode = _mode;
            for (int frame = 0; frame < frames; frame++)
            {
                g += (_targetG - g) * smoothing;
                k += (_targetK - k) * smoothing;
                peakGain += (_targetPeakGain - peakGain) * smoothing;

                // The integrator coefficients follow from the smoothed corner every frame, so a moving corner never
                // jumps between two sets of poles.
                float a1 = 1f / (1f + (g * (g + k)));
                float a2 = g * a1;
                float a3 = g * a2;

                int frameStart = offset + (frame * stride);
                for (int channel = 0; channel < stride; channel++)
                {
                    int index = frameStart + channel;
                    float input = buffer[index];
                    float v3 = input - _integratorTwo[channel];
                    float band = (a1 * _integratorOne[channel]) + (a2 * v3);
                    float low = _integratorTwo[channel] + (a2 * _integratorOne[channel]) + (a3 * v3);
                    _integratorOne[channel] = (2f * band) - _integratorOne[channel];
                    _integratorTwo[channel] = (2f * low) - _integratorTwo[channel];
                    buffer[index] = Shape(mode, input, band, low, k, peakGain);
                }
            }

            _activeG = g;
            _activeK = k;
            _activePeakGain = peakGain;
        }

        public override void Reset()
        {
            _activeG = _targetG;
            _activeK = _targetK;
            _activePeakGain = _targetPeakGain;
            if (_integratorOne != null)
            {
                Array.Clear(_integratorOne, 0, _integratorOne.Length);
            }

            if (_integratorTwo != null)
            {
                Array.Clear(_integratorTwo, 0, _integratorTwo.Length);
            }
        }

        private static float Shape(DspFilterMode mode, float input, float band, float low, float k, float peakGain)
        {
            switch (mode)
            {
                case DspFilterMode.LowPass:
                    return low;
                case DspFilterMode.HighPass:
                    return input - (k * band) - low;
                case DspFilterMode.BandPass:
                    // The band output of this core peaks at the quality; the damping turns that into unity.
                    return k * band;
                case DspFilterMode.Notch:
                    return input - (k * band);
                case DspFilterMode.Peak:
                    return input + ((peakGain - 1f) * k * band);
                default:
                    // An unset mode passes the block through rather than guessing a band the author did not ask for.
                    return input;
            }
        }

        private void RecomputeTargets()
        {
            if (_sampleRate <= 0)
            {
                // Nothing has prepared a rate yet; PrepareCore recomputes this before the first block.
                return;
            }

            // A corner at or above Nyquist makes the tangent run away, so it stops at 45% of the rate.
            float hertz = Math.Min(_frequencyHz, 0.45f * _sampleRate);
            _targetG = (float)Math.Tan((Math.PI * hertz) / _sampleRate);
        }
    }
}
