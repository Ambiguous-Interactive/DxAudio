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

        private const float MinimumToneHz = 20f;

        private float _drive;
        private float _toneHz;
        private float _toneCoefficient;
        private float[] _toneState;
        private int _sampleRate;
        private int _channels;

        public DspDistortion()
        {
            _drive = 1f;
            _toneHz = OpenToneHz;
            _toneCoefficient = 1f;
            _toneState = Array.Empty<float>();
        }

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
                _toneCoefficient = Coefficient(_toneHz, _sampleRate);
            }
        }

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
            _toneState = new float[channels];
            _toneCoefficient = Coefficient(Math.Min(_toneHz, MaxToneHz(sampleRate)), sampleRate);
            Reset();
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length < offset + count)
            {
                return;
            }

            int stride = channels;

            int frames = count / stride;
            float drive = _drive;
            float coefficient = _toneCoefficient;
            for (int frame = 0; frame < frames; frame++)
            {
                int frameStart = offset + (frame * stride);
                for (int channel = 0; channel < stride; channel++)
                {
                    int index = frameStart + channel;
                    float shaped = (float)Math.Tanh(drive * buffer[index]);
                    float filtered = _toneState[channel] + ((shaped - _toneState[channel]) * coefficient);
                    _toneState[channel] = filtered;
                    buffer[index] = filtered;
                }
            }
        }

        public override void Reset()
        {
            if (_toneState != null)
            {
                Array.Clear(_toneState, 0, _toneState.Length);
            }
        }

        private static float MaxToneHz(int sampleRate)
        {
            return 0.45f * sampleRate;
        }

        private static float Coefficient(float toneHz, int sampleRate)
        {
            if (sampleRate <= 0)
            {
                return 1f;
            }

            return AudioMath.OnePoleCoefficient(1f / (2f * (float)Math.PI * toneHz), sampleRate);
        }
    }
}
