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

        private const int ReferenceRate = 24_000;
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
        private float[] _pending = Array.Empty<float>();
        private float[] _wet = Array.Empty<float>();
        private bool _hasPending;
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
            float[] pending = _pending;
            float[] wet = _wet;
            float gainA = _lineA.Gain;
            float gainB = _lineB.Gain;
            float gainC = _lineC.Gain;
            float gainD = _lineD.Gain;
            bool hasPending = _hasPending;
            int stop = offset + count;
            while (offset < stop)
            {
                int a = _lineA.Position;
                int b = _lineB.Position;
                int c = _lineC.Position;
                int d = _lineD.Position;
                int remaining = Math.Min(Math.Min(_lineA.End - a, _lineB.End - b), Math.Min(_lineC.End - c, _lineD.End - d));
                int length = (int)Math.Min(stop - offset, 2L * remaining - (hasPending ? channels : 0));
                int end = offset + length;

                // Keep each channel's pending dry sample and held tail in locals until the next ring boundary.
                for (int channel = 0; channel < channels; ++channel)
                {
                    int readAIndex = a + channel;
                    int readBIndex = b + channel;
                    int readCIndex = c + channel;
                    int readDIndex = d + channel;
                    float firstDry = pending[channel];
                    float tail = wet[channel];
                    int frame = offset;
                    if (!hasPending)
                    {
                        firstDry = buffer[frame + channel];
                        buffer[frame + channel] = firstDry + tail;
                        frame += channels;
                    }

                    while (frame < end)
                    {
                        float readA = history[readAIndex];
                        float readB = history[readBIndex];
                        float readC = history[readCIndex];
                        float readD = history[readDIndex];
                        float reflection = (readA + readB + readC + readD) * 0.5f;
                        float dry = buffer[frame + channel];
                        // Subtracting half the sum forms an orthogonal Householder feedback matrix.
                        float feed = (dry + firstDry) * 0.25f - reflection;
                        // Add and remove a tiny floor to suppress subnormal samples in the feedback path.
                        history[readAIndex] = ((readA + feed) * gainA + TailFloor) - TailFloor;
                        history[readBIndex] = ((readB + feed) * gainB + TailFloor) - TailFloor;
                        history[readCIndex] = ((readC + feed) * gainC + TailFloor) - TailFloor;
                        history[readDIndex] = ((readD + feed) * gainD + TailFloor) - TailFloor;
                        tail = reflection * wetLevel;
                        buffer[frame + channel] = dry + tail;
                        readAIndex += channels;
                        readBIndex += channels;
                        readCIndex += channels;
                        readDIndex += channels;
                        frame += channels;

                        if (frame < end)
                        {
                            firstDry = buffer[frame + channel];
                            buffer[frame + channel] = firstDry + tail;
                            frame += channels;
                        }
                    }

                    pending[channel] = firstDry;
                    wet[channel] = tail;
                }

                // Each completed pair advances every ring by one frame.
                int framesWithPending = length / channels + (hasPending ? 1 : 0);
                int advance = (framesWithPending / 2) * channels;
                _lineA.AdvanceTo(a + advance);
                _lineB.AdvanceTo(b + advance);
                _lineC.AdvanceTo(c + advance);
                _lineD.AdvanceTo(d + advance);
                hasPending = (framesWithPending & 1) != 0;
                offset = end;
            }

            _hasPending = hasPending;
        }

        public override void Reset()
        {
            Array.Clear(_history, 0, _history.Length);
            Array.Clear(_wet, 0, _wet.Length);
            _hasPending = false;
            _lineA.Reset();
            _lineB.Reset();
            _lineC.Reset();
            _lineD.Reset();
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            _channels = channels;
            _preparedWetLevel = _wetLevel;
            _hasPending = false;
            if (_preparedWetLevel == 0f)
            {
                _history = Array.Empty<float>();
                _pending = Array.Empty<float>();
                _wet = Array.Empty<float>();
                return;
            }

            // The lines spread the first reflections across about 17 to 43 ms.
            double networkRate = sampleRate * 0.5d;
            double gainExponent = -3d / (networkRate * _decaySeconds);
            _lineA = new DelayLine(0, 419, networkRate, channels, gainExponent);
            _lineB = new DelayLine(_lineA.End, 563, networkRate, channels, gainExponent);
            _lineC = new DelayLine(_lineB.End, 751, networkRate, channels, gainExponent);
            _lineD = new DelayLine(_lineC.End, 1_031, networkRate, channels, gainExponent);
            _history = new float[_lineD.End];
            _pending = new float[channels];
            _wet = new float[channels];
        }

        private struct DelayLine
        {
            internal readonly int Start;
            internal readonly int End;
            internal readonly float Gain;
            internal int Position;

            internal DelayLine(int start, int referenceFrames, double sampleRate, int channels, double gainExponent)
            {
                int frames = Math.Max(1, (int)Math.Round(referenceFrames * sampleRate / ReferenceRate));
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
