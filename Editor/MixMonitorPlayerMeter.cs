namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Numerics;

    internal sealed class MixMonitorPlayerMeter
    {
        internal const int SampleCount = 1024;

        internal MixMonitorPeakChannel Left => _left;
        internal MixMonitorPeakChannel Right => _right;
        internal ReadOnlySpan<float> Spectrum => _spectrum;

        private readonly float[] _leftMix = new float[SampleCount];
        private readonly float[] _rightMix = new float[SampleCount];
        private readonly float[] _spectrum;
        private readonly Complex[] _transform;
        private readonly Complex[] _rotations;
        private readonly double[] _window;
        private MixMonitorPeakChannel _left;
        private MixMonitorPeakChannel _right;

        internal MixMonitorPlayerMeter(bool spectrum = false)
        {
            if (!spectrum)
            {
                _spectrum = Array.Empty<float>();
                return;
            }

            _spectrum = new float[SampleCount / 2 + 1];
            _transform = new Complex[SampleCount];
            _rotations = new Complex[SampleCount / 2];
            _window = new double[SampleCount];
            for (int index = 0; index < SampleCount; ++index)
            {
                double phase = 2d * Math.PI * index / SampleCount;
                // Periodic Hann, normalized for the amplitude of a tone centered in a bin.
                _window[index] = (0.5d - 0.5d * Math.Cos(phase)) * (4d / SampleCount);
                if (index < _rotations.Length)
                {
                    _rotations[index] = Complex.FromPolarCoordinates(1d, -phase);
                }
            }
        }

        private static float Peak(float[] mix)
        {
            float peak = 0f;
            foreach (float sample in mix)
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }
            return peak;
        }

        internal void AddChannel(float[] samples, bool right, int offset = 0, int stride = 1)
        {
            float[] mix = right ? _rightMix : _leftMix;
            for (int index = 0; index < SampleCount; ++index)
            {
                float sample = samples[offset];
                offset += stride;
                if (-float.MaxValue <= sample && sample <= float.MaxValue)
                {
                    mix[index] += sample;
                }
            }
        }

        internal void FinishSample(double now)
        {
            float left = Peak(_leftMix);
            float right = Peak(_rightMix);
            _left.Sample(left, hasSignal: true, now);
            _right.Sample(right, hasSignal: true, now);

            int binCount = _spectrum.Length;
            if (0 < binCount)
            {
                Array.Clear(_spectrum, 0, binCount);
                if (0f < left || 0f < right)
                {
                    AccumulateSpectrum(_leftMix);
                    AccumulateSpectrum(_rightMix);
                    for (int bin = 0; bin < binCount; ++bin)
                    {
                        _spectrum[bin] = (float)Math.Sqrt(_spectrum[bin] * 0.5f);
                    }
                }
            }

            ClearMix();
        }

        internal void Clear()
        {
            _left = default;
            _right = default;
            Array.Clear(_spectrum, 0, _spectrum.Length);
            ClearMix();
        }

        private void ClearMix()
        {
            Array.Clear(_leftMix, 0, _leftMix.Length);
            Array.Clear(_rightMix, 0, _rightMix.Length);
        }

        private void AccumulateSpectrum(float[] mix)
        {
            // Order the windowed samples by reversed index, then combine adjacent frequency transforms.
            for (int index = 0, reversed = 0; index < SampleCount; ++index)
            {
                _transform[reversed] = mix[index] * _window[index];
                int bit = SampleCount >> 1;
                while ((reversed & bit) != 0)
                {
                    reversed ^= bit;
                    bit >>= 1;
                }
                reversed ^= bit;
            }

            for (int half = 1; half < SampleCount; half *= 2)
            {
                int width = half * 2;
                int stride = SampleCount / width;
                for (int start = 0; start < SampleCount; start += width)
                {
                    for (int offset = 0; offset < half; ++offset)
                    {
                        int lower = start + offset;
                        int upper = lower + half;
                        Complex even = _transform[lower];
                        Complex odd = _transform[upper] * _rotations[offset * stride];
                        _transform[lower] = even + odd;
                        _transform[upper] = even - odd;
                    }
                }
            }

            int binCount = _spectrum.Length;
            for (int bin = 0; bin < binCount; ++bin)
            {
                Complex value = _transform[bin];
                double power = value.Real * value.Real + value.Imaginary * value.Imaginary;
                // DC and Nyquist have no corresponding negative-frequency contribution.
                _spectrum[bin] += (float)(bin == 0 || bin == SampleCount / 2 ? power * 0.25d : power);
            }
        }
    }
}
