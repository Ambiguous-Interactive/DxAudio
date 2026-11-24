namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Measures BS.1770 integrated and EBU short-term loudness on the editor thread.</summary>
    internal sealed class MasterLoudness
    {
        private const double Calibration = -0.691d;
        private const int GateHops = 4;
        private const int ShortTermHops = 30;
        private const double SurroundWeight = 1.41d;
        private static readonly double AbsoluteGate = Math.Pow(10d, (-70d - Calibration) / 10d);

        /*
            Unity's listener channel order. BS.1770 Annex 1 weights quad and 5.x surrounds.
            Annex 3 weights 7.1 sides, with unity weighting for the rear pair.
        */
        private static readonly double[] Mono = { 1d };
        private static readonly double[] Stereo = { 1d, 1d };
        private static readonly double[] Quad = { 1d, 1d, SurroundWeight, SurroundWeight };
        private static readonly double[] Surround = { 1d, 1d, 1d, SurroundWeight, SurroundWeight };
        private static readonly double[] Surround51 = { 1d, 1d, 1d, 0d, SurroundWeight, SurroundWeight };
        private static readonly double[] Surround71 = { 1d, 1d, 1d, 0d, SurroundWeight, SurroundWeight, 1d, 1d };

        internal double? ShortTerm => _filledHops == ShortTermHops ? ToLufs(MeanOfLatest(ShortTermHops)) : null;

        internal double? Integrated
        {
            get
            {
                double? energy = _gate.Mean();
                return energy.HasValue ? ToLufs(energy.Value) : null;
            }
        }

        private readonly FilterState[] _shelfState = new FilterState[TruePeakFilter.MaxChannels];
        private readonly FilterState[] _highPassState = new FilterState[TruePeakFilter.MaxChannels];
        private readonly double[] _hops = new double[ShortTermHops];

        private readonly LoudnessGate _gate = new(AbsoluteGate);
        private double[] _weights;
        private Biquad _shelf;
        private Biquad _highPass;
        private int _sampleRate;
        private int _channels;
        private int _hopFrames;
        private int _frames;
        private double _energy;
        private int _nextHop;
        private int _filledHops;

        private static double ToLufs(double energy)
        {
            return Calibration + 10d * Math.Log10(energy);
        }

        internal void Observe(float[] samples, int offset, int count, int channels, int sampleRate)
        {
            if (_sampleRate != sampleRate || _channels != channels)
            {
                Configure(channels, sampleRate);
            }

            if (_weights == null)
            {
                return;
            }

            int end = offset + count - count % channels;
            for (int start = offset; start < end; )
            {
                int frames = Math.Min((end - start) / channels, _hopFrames - _frames);
                int stop = start + frames * channels;
                _energy += MeasureEnergy(samples, start, stop, channels);
                _frames += frames;
                if (_frames == _hopFrames)
                {
                    CompleteHop();
                }

                start = stop;
            }
        }

        internal void Reset()
        {
            Array.Clear(_shelfState, 0, _shelfState.Length);
            Array.Clear(_highPassState, 0, _highPassState.Length);
            _frames = 0;
            _energy = 0d;
            _nextHop = 0;
            _filledHops = 0;
            _gate.Clear();
        }

        private void Configure(int channels, int sampleRate)
        {
            Reset();
            _sampleRate = sampleRate;
            _channels = channels;
            _weights =
                4_000 <= sampleRate
                    ? channels switch
                    {
                        1 => Mono,
                        2 => Stereo,
                        4 => Quad,
                        5 => Surround,
                        6 => Surround51,
                        8 => Surround71,
                        _ => null,
                    }
                    : null;

            if (_weights != null)
            {
                _hopFrames = (int)Math.Round(sampleRate / 10d, MidpointRounding.AwayFromZero);
                /*
                    Bilinear design reproduces the BS.1770 Annex 1 tables at 48 kHz.
                    See FFmpeg's libavfilter/f_ebur128.c for the filter parameters.
                */
                double k = Math.Tan(Math.PI * 1681.974450955533d / sampleRate);
                double kOverQ = k / 0.7071752369554196d;
                double kSquared = k * k;
                double normal = 1d + kOverQ + kSquared;
                double high = Math.Pow(10d, 3.999843853973347d / 20d);
                double mid = Math.Pow(high, 0.4996667741545416d) * kOverQ;
                _shelf = new Biquad(
                    (high + mid + kSquared) / normal,
                    2d * (kSquared - high) / normal,
                    (high - mid + kSquared) / normal,
                    2d * (kSquared - 1d) / normal,
                    (1d - kOverQ + kSquared) / normal
                );

                k = Math.Tan(Math.PI * 38.13547087602444d / sampleRate);
                kOverQ = k / 0.5003270373238773d;
                kSquared = k * k;
                normal = 1d + kOverQ + kSquared;
                _highPass = new Biquad(1d, -2d, 1d, 2d * (kSquared - 1d) / normal, (1d - kOverQ + kSquared) / normal);
            }
        }

        private double MeasureEnergy(float[] samples, int start, int stop, int channels)
        {
            double energy = 0d;
            for (int channel = 0; channel < channels; ++channel)
            {
                double weight = _weights[channel];
                if (weight == 0d)
                {
                    continue;
                }

                FilterState shelf = _shelfState[channel];
                FilterState highPass = _highPassState[channel];
                double channelEnergy = 0d;
                for (int index = start + channel; index < stop; index += channels)
                {
                    float sample = samples[index];
                    double weighted = _shelf.Process(Math.Abs(sample) <= float.MaxValue ? sample : 0d, ref shelf);
                    weighted = _highPass.Process(weighted, ref highPass);
                    channelEnergy += weighted * weighted;
                }

                _shelfState[channel] = shelf;
                _highPassState[channel] = highPass;
                energy += weight * channelEnergy;
            }

            return energy;
        }

        private void CompleteHop()
        {
            _hops[_nextHop] = _energy / _hopFrames;
            _nextHop = (_nextHop + 1) % ShortTermHops;
            _filledHops = Math.Min(ShortTermHops, _filledHops + 1);
            _energy = 0d;
            _frames = 0;
            if (GateHops <= _filledHops)
            {
                _gate.Add(MeanOfLatest(GateHops));
            }
        }

        private double MeanOfLatest(int count)
        {
            double energy = 0d;
            int index = _nextHop;
            for (int hop = 0; hop < count; ++hop)
            {
                index = index == 0 ? ShortTermHops - 1 : index - 1;
                energy += _hops[index];
            }

            return energy / count;
        }

        private struct FilterState
        {
            internal double First;
            internal double Second;
        }

        private readonly struct Biquad
        {
            private readonly double _b0;
            private readonly double _b1;
            private readonly double _b2;
            private readonly double _a1;
            private readonly double _a2;

            internal Biquad(double b0, double b1, double b2, double a1, double a2)
            {
                _b0 = b0;
                _b1 = b1;
                _b2 = b2;
                _a1 = a1;
                _a2 = a2;
            }

            internal double Process(double sample, ref FilterState state)
            {
                double output = _b0 * sample + state.First;
                state.First = _b1 * sample - _a1 * output + state.Second;
                state.Second = _b2 * sample - _a2 * output;
                return output;
            }
        }
    }
}
