namespace WallstopStudios.DxAudio
{
    using System;
    using System.Runtime.CompilerServices;

    /// <summary>Drives a signal into a wave shaper and then a tone control, the way a distortion pedal does.</summary>
    /// <remarks>
    /// A hyperbolic tangent is the shape: gentle near zero, hard against one, and smooth everywhere, so the
    /// rounding it adds is not the fizz of a hard clip. Drive is pre-gain into that curve, so more drive means
    /// more of the signal sits in the bend.
    ///
    /// Tone is a one-pole low-pass after the shaper, because the harmonics drive adds land on top of the sound
    /// and a bright top is what makes distortion sound like a broken speaker rather than a loud guitar.
    /// </remarks>
    public sealed class DspDistortion : DspEffect
    {
        /// <summary>A cutoff at the top of hearing, which passes the shaper's top through.</summary>
        public const float OpenToneHz = 20_000f;

        private const float MinimumToneHz = 20f;

        private const float SmoothingSeconds = 0.01f;

        // Quadratic fit in x² for tanh(x) / x. The shaper error stays below 6e-7 on [-2.5, 2.5].
        private const float ShaperRangeSquared = 6.25f;
        private const float NumeratorConstant = 0.99999803f;
        private const float NumeratorLinear = 0.10855813f;
        private const float NumeratorQuadratic = 0.00091172634f;
        private const float DenominatorLinear = 0.44188027f;
        private const float DenominatorQuadratic = 0.01488993f;

        public override string DisplayName => "Distortion";

        /// <summary>Pre-gain into the wave shaper; one is the shape's own curve, higher is more bend.</summary>
        public float Drive
        {
            get => _drive;
            set
            {
                if (AudioMath.IsFinite(value))
                {
                    _drive = AudioMath.Clamp(value, 0.01f, 100f);
                }
            }
        }

        /// <summary>The corner of the tone control, in hertz.</summary>
        public float ToneHz
        {
            get => _toneHz;
            set
            {
                if (!AudioMath.IsFinite(value))
                {
                    return;
                }

                _toneHz = Math.Max(MinimumToneHz, value);
                _toneCoefficient = Coefficient(CappedToneHz(_toneHz, _sampleRate), _sampleRate);
            }
        }

        private float _drive;
        private float _toneHz;
        private float _toneCoefficient;
        private float _activeDrive;
        private float _activeToneCoefficient;
        private float _smoothingCoefficient;
        private float[] _toneState;
        private int _sampleRate;
        private int _channels;

        public DspDistortion()
        {
            _drive = 1f;
            _toneHz = OpenToneHz;
            _toneCoefficient = 1f;
            _activeDrive = 1f;
            _activeToneCoefficient = 1f;
            _smoothingCoefficient = 1f;
            _toneState = Array.Empty<float>();
        }

        private static float MaxToneHz(int sampleRate)
        {
            return 0.45f * sampleRate;
        }

        private static float CappedToneHz(float toneHz, int sampleRate)
        {
            return 0 < sampleRate ? Math.Min(toneHz, MaxToneHz(sampleRate)) : toneHz;
        }

        private static float Coefficient(float toneHz, int sampleRate)
        {
            if (sampleRate <= 0)
            {
                return 1f;
            }

            return AudioMath.OnePoleCoefficient(1f / (2f * (float)Math.PI * toneHz), sampleRate);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Shape(float sample)
        {
            float square = sample * sample;
            if (square <= ShaperRangeSquared)
            {
                float numerator = (NumeratorQuadratic * square + NumeratorLinear) * square + NumeratorConstant;
                float denominator = (DenominatorQuadratic * square + DenominatorLinear) * square + 1f;
                return sample * numerator / denominator;
            }

            return ShapeOutsideFit(sample, square);
        }

        private static float ShapeOutsideFit(float sample, float square)
        {
            // Outside [-8, 8], tanh is within 2.3e-7 of full scale.
            if (64f <= square)
            {
                return sample < 0f ? -1f : 1f;
            }

            return (float)Math.Tanh(sample);
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            if (channels == 2)
            {
                float drive = _activeDrive;
                float tone = _activeToneCoefficient;
                float smoothing = _smoothingCoefficient;
                // A float ramp can stop changing before it reaches the target.
                float nextDrive = drive + ((_drive - drive) * smoothing);
                float nextTone = tone + ((_toneCoefficient - tone) * smoothing);
                if (drive == nextDrive && tone == nextTone)
                {
                    ProcessSettledStereo(buffer, offset, offset + count, drive, tone);
                }
                else
                {
                    ProcessStereo(buffer, offset, offset + count);
                }

                return;
            }

            int stride = channels;

            int frames = count / stride;
            for (int frame = 0; frame < frames; ++frame)
            {
                _activeDrive += (_drive - _activeDrive) * _smoothingCoefficient;
                _activeToneCoefficient += (_toneCoefficient - _activeToneCoefficient) * _smoothingCoefficient;
                int frameStart = offset + (frame * stride);
                for (int channel = 0; channel < stride; ++channel)
                {
                    ref float sample = ref buffer[frameStart + channel];
                    ref float tone = ref _toneState[channel];
                    float shaped = Shape(_activeDrive * sample);
                    float filtered = tone + ((shaped - tone) * _activeToneCoefficient);
                    tone = filtered;
                    sample = filtered;
                }
            }
        }

        public override void Reset()
        {
            _activeDrive = _drive;
            _activeToneCoefficient = _toneCoefficient;
            if (_toneState != null)
            {
                Array.Clear(_toneState, 0, _toneState.Length);
            }
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _toneState = new float[channels];
            _toneCoefficient = Coefficient(CappedToneHz(_toneHz, sampleRate), sampleRate);
            _smoothingCoefficient = AudioMath.OnePoleCoefficient(SmoothingSeconds, sampleRate);
            Reset();
        }

        private void ProcessSettledStereo(float[] buffer, int offset, int end, float drive, float toneCoefficient)
        {
            float[] toneState = _toneState;
            float current = toneState[0];
            float other = toneState[1];

            for (int index = offset; index < end; ++index)
            {
                ref float sample = ref buffer[index];
                float driven = drive * sample;
                float square = driven * driven;
                float shaped;
                // Keep the common fit in this loop so it does not depend on Mono inlining.
                if (square <= ShaperRangeSquared)
                {
                    float numerator = (NumeratorQuadratic * square + NumeratorLinear) * square + NumeratorConstant;
                    float denominator = (DenominatorQuadratic * square + DenominatorLinear) * square + 1f;
                    shaped = driven * numerator / denominator;
                }
                else
                {
                    shaped = ShapeOutsideFit(driven, square);
                }

                float filtered = current + ((shaped - current) * toneCoefficient);
                sample = filtered;
                // The next interleaved sample uses the other channel's history.
                current = other;
                other = filtered;
            }

            // An even sample count restores left-first history order.
            toneState[0] = current;
            toneState[1] = other;
        }

        // Keep the stereo tone history and ramp values local across the block.
        private void ProcessStereo(float[] buffer, int offset, int end)
        {
            float activeDrive = _activeDrive;
            float activeTone = _activeToneCoefficient;
            float smoothing = _smoothingCoefficient;
            float[] toneState = _toneState;
            float left = toneState[0];
            float right = toneState[1];

            for (int index = offset; index < end; index += 2)
            {
                activeDrive += (_drive - activeDrive) * smoothing;
                activeTone += (_toneCoefficient - activeTone) * smoothing;
                ref float leftSample = ref buffer[index];
                ref float rightSample = ref buffer[index + 1];
                float shapedLeft = Shape(activeDrive * leftSample);
                float shapedRight = Shape(activeDrive * rightSample);
                left += (shapedLeft - left) * activeTone;
                right += (shapedRight - right) * activeTone;
                leftSample = left;
                rightSample = right;
            }

            toneState[0] = left;
            toneState[1] = right;
            _activeDrive = activeDrive;
            _activeToneCoefficient = activeTone;
        }
    }
}
