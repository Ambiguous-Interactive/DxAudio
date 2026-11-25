namespace WallstopStudios.DxAudio
{
    using System;

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

        // Linear interpolation at this spacing bounds the ideal tanh error below 4e-7.
        private const int SamplesPerUnit = 512;
        private const int TableEnd = 10 * SamplesPerUnit;

        private const float MinimumToneHz = 20f;

        private const float SmoothingSeconds = 0.01f;
        private static readonly float[] Table;

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

        static DspDistortion()
        {
            Table = BuildTable();
        }

        private static float[] BuildTable()
        {
            float[] table = new float[TableEnd + 1];
            for (int index = 0; index <= TableEnd; ++index)
            {
                table[index] = (float)Math.Tanh((double)index / SamplesPerUnit);
            }
            return table;
        }

        private static float Shape(float sample)
        {
            float position = Math.Abs(sample) * SamplesPerUnit;
            if (position < TableEnd)
            {
                int index = (int)position;
                float first = Table[index];
                float shaped = first + (position - index) * (Table[index + 1] - first);
                return sample < 0f ? -shaped : shaped;
            }

            // tanh(10) already rounds to full scale at float precision.
            return float.IsNaN(sample) ? sample : (sample < 0f ? -1f : 1f);
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

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            if (channels == 2)
            {
                ProcessStereo(buffer, offset, offset + count);
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
