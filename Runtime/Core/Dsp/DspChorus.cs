namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Mixes two modulated copies of each channel with the immediate dry signal.</summary>
    public sealed class DspChorus : DspEffect
    {
        public const float MinimumRateHertz = 0.05f;
        public const float MaximumRateHertz = 5f;
        public const float DefaultRateHertz = 0.8f;
        public const float MaximumDepthSeconds = 0.008f;
        public const float DefaultDepthSeconds = 0.003f;
        public const float DefaultMix = 0.35f;

        private const double CenterDelaySeconds = 0.015;
        private const double FullTurn = 2d * Math.PI;

        public override string DisplayName => "Chorus";

        /// <summary>The modulation cycles per second. Changes apply at the next preparation.</summary>
        public float RateHertz
        {
            get => _rateHertz;
            set => _rateHertz = AudioMath.ClampFinite(value, MinimumRateHertz, MaximumRateHertz, DefaultRateHertz);
        }

        /// <summary>The maximum change in delay from 15 ms. Changes apply at the next preparation.</summary>
        public float DepthSeconds
        {
            get => _depthSeconds;
            set => _depthSeconds = AudioMath.ClampFinite(value, 0f, MaximumDepthSeconds, DefaultDepthSeconds);
        }

        /// <summary>The fraction of the output from the delay taps. Changes apply at the next preparation.</summary>
        public float Mix
        {
            get => _mix;
            set => _mix = AudioMath.ClampFinite(value, 0f, 1f, DefaultMix);
        }

        private float _rateHertz = DefaultRateHertz;
        private float _depthSeconds = DefaultDepthSeconds;
        private float _mix = DefaultMix;
        private float[] _history = Array.Empty<float>();
        private int _channels;
        private int _historyFrames;
        private int _writeFrame;
        private int _availableFrames;
        private double _centerDelay;
        private double _depth;
        private double _phaseStep;
        private double _phase;
        private float _wetMix;

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (_wetMix == 0f || buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            float dryMix = 1f - _wetMix;
            int end = offset + count;
            for (int sample = offset; sample < end; sample += channels)
            {
                double sweep = _depth * Math.Sin(_phase);
                double firstDelay = Math.Max(1d, _centerDelay + sweep);
                double secondDelay = Math.Max(1d, _centerDelay - sweep);
                int firstFrames = (int)firstDelay;
                int secondFrames = (int)secondDelay;
                float firstFraction = (float)(firstDelay - firstFrames);
                float secondFraction = (float)(secondDelay - secondFrames);
                int first = HistoryOffset(firstFrames);
                int firstOlder = HistoryOffset(firstFrames + 1);
                int second = HistoryOffset(secondFrames);
                int secondOlder = HistoryOffset(secondFrames + 1);
                int write = _writeFrame * channels;

                for (int channel = 0; channel < channels; ++channel)
                {
                    float input = buffer[sample + channel];
                    float a = Interpolate(first, firstOlder, firstFraction, channel);
                    float b = Interpolate(second, secondOlder, secondFraction, channel);
                    float wet = channel % 2 == 0 ? a * (2f / 3f) + b / 3f : a / 3f + b * (2f / 3f);
                    _history[write + channel] = input;
                    buffer[sample + channel] = input * dryMix + wet * _wetMix;
                }

                ++_writeFrame;
                if (_writeFrame == _historyFrames)
                {
                    _writeFrame = 0;
                }

                if (_availableFrames < _historyFrames)
                {
                    ++_availableFrames;
                }

                _phase += _phaseStep;
                if (FullTurn <= _phase)
                {
                    _phase -= FullTurn;
                }
            }
        }

        public override void Reset()
        {
            _writeFrame = 0;
            _availableFrames = 0;
            _phase = 0d;
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            double centerDelay = CenterDelaySeconds * sampleRate;
            double depth = (double)_depthSeconds * sampleRate;
            float wetMix = _mix;
            int historyFrames = Math.Max(1, (int)Math.Ceiling(centerDelay + depth)) + 1;
            _history = wetMix == 0f ? Array.Empty<float>() : new float[checked(historyFrames * channels)];
            _centerDelay = centerDelay;
            _depth = depth;
            _phaseStep = FullTurn * (_rateHertz % sampleRate) / sampleRate;
            _wetMix = wetMix;
            _historyFrames = historyFrames;
            _channels = channels;
            Reset();
        }

        private int HistoryOffset(int delay)
        {
            // Old frames stay unread until the current voice has overwritten them.
            if (_availableFrames < delay)
            {
                return -1;
            }

            int frame = _writeFrame - delay;
            return (frame < 0 ? frame + _historyFrames : frame) * _channels;
        }

        private float Interpolate(int newer, int older, float fraction, int channel)
        {
            float recent = newer < 0 ? 0f : _history[newer + channel];
            float previous = older < 0 ? 0f : _history[older + channel];
            return recent * (1f - fraction) + previous * fraction;
        }
    }
}
