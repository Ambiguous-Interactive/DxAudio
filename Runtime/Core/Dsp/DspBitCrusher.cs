namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Drops a signal to a chosen bit depth and holds each sample for a chosen number of frames.</summary>
    /// <remarks>
    /// Two separate losses make the sound: rounding each sample to a step is the grit, and repeating a sample is
    /// the aliasing that reads as a lower sample rate. Both are wanted here, and both are exact, so the effect is
    /// easy to reason about: an output sample is always a step of the range, or the last one written.
    ///
    /// The steps span silence to full scale at both ends, so five bits give exactly thirty-two levels, quietest
    /// first; one bit gives only the two ends of the range.
    /// </remarks>
    public sealed class DspBitCrusher : DspEffect
    {
        public const int DefaultBits = 8;
        public const int DefaultRateReduction = 1;

        private const int MinimumBits = 1;
        private const int MaximumBits = 24;

        private int _bits;
        private int _rateReduction;
        private int _levels;
        private float _step;
        private float[] _held;
        private int _heldFor;
        private int _channels;

        public DspBitCrusher()
        {
            _bits = DefaultBits;
            _rateReduction = DefaultRateReduction;
            _held = Array.Empty<float>();
            Recompute();
        }

        public override string DisplayName => "Bit Crusher";

        /// <summary>How many bits each sample keeps, from one to twenty-four.</summary>
        public int Bits
        {
            get => _bits;
            set
            {
                _bits = Math.Min(MaximumBits, Math.Max(MinimumBits, value));
                Recompute();
            }
        }

        /// <summary>How many frames each kept sample lasts; one rewrites every frame.</summary>
        public int RateReduction
        {
            get => _rateReduction;
            set => _rateReduction = Math.Max(1, value);
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

            _channels = channels;
            _held = new float[channels];
            Reset();
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            int stride = channels;

            // One configuration for the whole block: a knob set on the main thread must not split a block
            // between two steps.
            float step = _step;
            int levels = _levels;
            int frames = count / stride;
            for (int frame = 0; frame < frames; frame++)
            {
                int frameStart = offset + (frame * stride);
                if (_heldFor <= 0)
                {
                    for (int channel = 0; channel < stride; channel++)
                    {
                        _held[channel] = Quantize(buffer[frameStart + channel], step, levels);
                    }

                    _heldFor = _rateReduction;
                }

                for (int channel = 0; channel < stride; channel++)
                {
                    buffer[frameStart + channel] = _held[channel];
                }

                _heldFor--;
            }
        }

        public override void Reset()
        {
            _heldFor = 0;
            if (_held != null)
            {
                Array.Clear(_held, 0, _held.Length);
            }
        }

        private static float Quantize(float sample, float step, int levels)
        {
            // Bounded first: a wild sample would otherwise become a level index far outside the range of an int.
            float bounded = AudioMath.Clamp(sample, -1f, 1f);
            int level = (int)Math.Round((bounded + 1f) / step);
            if (level < 0)
            {
                level = 0;
            }

            int loudest = levels - 1;
            if (loudest < level)
            {
                level = loudest;
            }

            // Mono rounds the top level a hair above one, so the range is enforced, not assumed.
            return AudioMath.Clamp(-1f + (level * step), -1f, 1f);
        }

        private void Recompute()
        {
            // Two ends of the range plus the levels between them, so the count is exactly two to the bits.
            _levels = 1 << _bits;
            _step = 2f / (_levels - 1);
        }
    }
}
