namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Adds a decaying room tail to the immediate dry signal.</summary>
    public sealed class DspReverb : DspEffect
    {
        public const float MinimumDecaySeconds = 0.15f;
        public const float MaximumDecaySeconds = 3f;
        public const float DefaultDecaySeconds = 0.9f;
        public const float DefaultWetLevel = 0.25f;

        private const int ReferenceRate = 48_000;
        private const float TailFloor = 1e-20f;

        public override string DisplayName => "Reverb";

        /// <summary>The nominal time for reflections to fall by 60 dB. Changes apply at the next preparation.</summary>
        public float DecaySeconds
        {
            get => _decaySeconds;
            set => _decaySeconds = AudioMath.ClampFinite(value, MinimumDecaySeconds, MaximumDecaySeconds, DefaultDecaySeconds);
        }

        /// <summary>The gain of the added reflections. Changes apply at the next preparation.</summary>
        public float WetLevel
        {
            get => _wetLevel;
            set => _wetLevel = AudioMath.ClampFinite(value, 0f, 1f, DefaultWetLevel);
        }

        private float _decaySeconds = DefaultDecaySeconds;
        private float _wetLevel = DefaultWetLevel;
        private float _preparedWetLevel;
        private int _channels;
        private float[] _history = Array.Empty<float>();
        private DelayLine _lineA;
        private DelayLine _lineB;
        private DelayLine _lineC;
        private DelayLine _lineD;

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            float wetLevel = _preparedWetLevel;
            if (wetLevel == 0f || buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            float[] history = _history;
            float gainA = _lineA.Gain;
            float gainB = _lineB.Gain;
            float gainC = _lineC.Gain;
            float gainD = _lineD.Gain;
            int stop = offset + count;
            while (offset < stop)
            {
                int a = _lineA.Position;
                int b = _lineB.Position;
                int c = _lineC.Position;
                int d = _lineD.Position;
                int length = Math.Min(stop - offset, Math.Min(Math.Min(_lineA.End - a, _lineB.End - b), Math.Min(_lineC.End - c, _lineD.End - d)));
                int end = offset + length;

                // Each segment ends before a line wraps. Interleaved channels keep independent histories.
                for (int sample = offset; sample < end; ++sample)
                {
                    float readA = history[a];
                    float readB = history[b];
                    float readC = history[c];
                    float readD = history[d];
                    float reflection = (readA + readB + readC + readD) * 0.5f;
                    float dry = buffer[sample];
                    // Subtracting half the sum forms an orthogonal Householder feedback matrix.
                    float feed = dry * 0.5f - reflection;
                    // Add and remove a tiny floor so quiet tails cannot leave subnormal samples in the feedback path.
                    history[a++] = ((readA + feed) * gainA + TailFloor) - TailFloor;
                    history[b++] = ((readB + feed) * gainB + TailFloor) - TailFloor;
                    history[c++] = ((readC + feed) * gainC + TailFloor) - TailFloor;
                    history[d++] = ((readD + feed) * gainD + TailFloor) - TailFloor;
                    buffer[sample] = dry + reflection * wetLevel;
                }

                _lineA.AdvanceTo(a);
                _lineB.AdvanceTo(b);
                _lineC.AdvanceTo(c);
                _lineD.AdvanceTo(d);
                offset = end;
            }
        }

        public override void Reset()
        {
            Array.Clear(_history, 0, _history.Length);
            _lineA.Reset();
            _lineB.Reset();
            _lineC.Reset();
            _lineD.Reset();
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            _channels = channels;
            _preparedWetLevel = _wetLevel;
            if (_preparedWetLevel == 0f)
            {
                _history = Array.Empty<float>();
                return;
            }

            // Spread the first reflections across about 17 to 43 ms. The lengths at 48 kHz are coprime.
            double gainExponent = -3d / (sampleRate * (double)_decaySeconds);
            _lineA = new DelayLine(0, 827, sampleRate, channels, gainExponent);
            _lineB = new DelayLine(_lineA.End, 1_123, sampleRate, channels, gainExponent);
            _lineC = new DelayLine(_lineB.End, 1_493, sampleRate, channels, gainExponent);
            _lineD = new DelayLine(_lineC.End, 2_053, sampleRate, channels, gainExponent);
            _history = new float[_lineD.End];
        }

        private struct DelayLine
        {
            internal readonly int Start;
            internal readonly int End;
            internal readonly float Gain;
            internal int Position;

            internal DelayLine(int start, int referenceFrames, int sampleRate, int channels, double gainExponent)
            {
                int frames = Math.Max(1, (int)Math.Round((double)referenceFrames * sampleRate / ReferenceRate));
                Start = start;
                End = checked(start + frames * channels);
                Gain = (float)Math.Pow(10d, frames * gainExponent);
                Position = start;
            }

            internal void AdvanceTo(int position)
            {
                Position = position == End ? Start : position;
            }

            internal void Reset()
            {
                Position = Start;
            }
        }
    }
}
