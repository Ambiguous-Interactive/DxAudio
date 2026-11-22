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
        private bool _primed;
        private double _centerDelay;
        private double _depth;
        private double _phaseStep;
        private double _phase;
        private double _sine;
        private double _cosine;
        private double _sineStep;
        private double _cosineStep;
        private float _wetMix;

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (_wetMix == 0f || buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            float wetMix = _wetMix;
            float dryMix = 1f - wetMix;
            float[] history = _history;
            int historyFrames = _historyFrames;
            int silence = historyFrames * channels;
            int writeFrame = _writeFrame;
            bool primed = _primed;
            double centerDelay = _centerDelay;
            double depth = _depth;
            double phase = _phase;
            double phaseStep = _phaseStep;
            double sine = _sine;
            double cosine = _cosine;
            double sineStep = _sineStep;
            double cosineStep = _cosineStep;
            int end = offset + count;
            for (int sample = offset; sample < end; sample += channels)
            {
                double sweep = depth * sine;
                double firstDelay = centerDelay + sweep;
                double secondDelay = centerDelay - sweep;
                firstDelay = firstDelay < 1d ? 1d : firstDelay;
                secondDelay = secondDelay < 1d ? 1d : secondDelay;
                int firstFrames = (int)firstDelay;
                int secondFrames = (int)secondDelay;
                float firstFraction = (float)(firstDelay - firstFrames);
                float secondFraction = (float)(secondDelay - secondFrames);
                int first = (writeFrame - firstFrames) * channels;
                int firstOlder = first - channels;
                int second = (writeFrame - secondFrames) * channels;
                int secondOlder = second - channels;
                // Negative offsets reach unwritten history on the first trip through the ring.
                if (firstOlder < 0)
                {
                    first = first < 0 ? (primed ? first + silence : silence) : first;
                    firstOlder = primed ? firstOlder + silence : silence;
                }

                if (secondOlder < 0)
                {
                    second = second < 0 ? (primed ? second + silence : silence) : second;
                    secondOlder = primed ? secondOlder + silence : silence;
                }

                int write = writeFrame * channels;
                for (int channel = 0; channel < channels; ++channel)
                {
                    float input = buffer[sample + channel];
                    float a = history[first + channel] * (1f - firstFraction) + history[firstOlder + channel] * firstFraction;
                    float b = history[second + channel] * (1f - secondFraction) + history[secondOlder + channel] * secondFraction;
                    float wet = (channel & 1) == 0 ? a * (2f / 3f) + b * (1f / 3f) : a * (1f / 3f) + b * (2f / 3f);
                    history[write + channel] = input;
                    buffer[sample + channel] = input * dryMix + wet * wetMix;
                }

                ++writeFrame;
                if (writeFrame == historyFrames)
                {
                    writeFrame = 0;
                    primed = true;
                }

                phase += phaseStep;
                if (FullTurn <= phase)
                {
                    // Re-anchor the recurrence once per turn so rounding cannot accumulate.
                    phase -= FullTurn;
                    sine = Math.Sin(phase);
                    cosine = Math.Cos(phase);
                }
                else
                {
                    double nextSine = sine * cosineStep + cosine * sineStep;
                    cosine = cosine * cosineStep - sine * sineStep;
                    sine = nextSine;
                }
            }

            _writeFrame = writeFrame;
            _primed = primed;
            _phase = phase;
            _sine = sine;
            _cosine = cosine;
        }

        public override void Reset()
        {
            _writeFrame = 0;
            _primed = false;
            _phase = 0d;
            _sine = 0d;
            _cosine = 1d;
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            double centerDelay = CenterDelaySeconds * sampleRate;
            double depth = (double)_depthSeconds * sampleRate;
            float wetMix = _mix;
            int historyFrames = Math.Max(1, (int)Math.Ceiling(centerDelay + depth)) + 1;
            // The final frame stays silent for reads before this voice's history starts.
            _history = wetMix == 0f ? Array.Empty<float>() : new float[checked((historyFrames + 1) * channels)];
            _centerDelay = centerDelay;
            _depth = depth;
            _phaseStep = FullTurn * (_rateHertz % sampleRate) / sampleRate;
            _sineStep = Math.Sin(_phaseStep);
            _cosineStep = Math.Cos(_phaseStep);
            _wetMix = wetMix;
            _historyFrames = historyFrames;
            _channels = channels;
            Reset();
        }
    }
}
