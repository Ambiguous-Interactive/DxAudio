namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Adds decaying repeats to the immediate dry signal.</summary>
    public sealed class DspDelay : DspEffect
    {
        public const float MinimumDelaySeconds = 0.00025f;
        public const float MaximumDelaySeconds = 0.5f;
        public const float DefaultDelaySeconds = 0.15f;
        public const float MaximumFeedback = 0.9f;
        public const float DefaultFeedback = 0.35f;
        public const float DefaultEchoLevel = 0.25f;

        public override string DisplayName => "Delay";

        /// <summary>The time between repeats. Changes apply at the next preparation.</summary>
        public float DelaySeconds
        {
            get => _delaySeconds;
            set => _delaySeconds = AudioMath.ClampFinite(value, MinimumDelaySeconds, MaximumDelaySeconds, DefaultDelaySeconds);
        }

        /// <summary>The fraction of each repeat fed back to the delay line.</summary>
        public float Feedback
        {
            get => _feedback;
            set => _feedback = AudioMath.ClampFinite(value, 0f, MaximumFeedback, DefaultFeedback);
        }

        /// <summary>The linear gain of the added echo. The dry signal always stays at unity.</summary>
        public float EchoLevel
        {
            get => _echoLevel;
            set => _echoLevel = AudioMath.ClampFinite(value, 0f, 1f, DefaultEchoLevel);
        }

        private float _delaySeconds = DefaultDelaySeconds;
        private float _feedback = DefaultFeedback;
        private float _echoLevel = DefaultEchoLevel;
        private float[] _history = Array.Empty<float>();
        private int _channels;
        private int _position;
        private bool _primed;

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            if (buffer == null || channels <= 0 || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            float feedback = _feedback;
            float echoLevel = _echoLevel;
            float[] history = _history;
            int end = offset + count;
            int position = _position;
            if (!_primed)
            {
                // Overwrite the old history on the first trip through the ring so a voice reset takes constant time.
                int prefix = Math.Min(count, history.Length - position);
                Array.Copy(buffer, offset, history, position, prefix);
                offset += prefix;
                position += prefix;
                if (position == history.Length)
                {
                    position = 0;
                    _primed = true;
                }
            }

            for (int sample = offset; sample < end; ++sample)
            {
                ref float output = ref buffer[sample];
                ref float tap = ref history[position];
                float input = output;
                float delayed = tap;
                tap = input + delayed * feedback;
                output = input + delayed * echoLevel;
                ++position;
                if (position == history.Length)
                {
                    position = 0;
                }
            }

            _position = position;
        }

        public override void Reset()
        {
            _position = 0;
            _primed = false;
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            int frames = Math.Max(1, (int)Math.Round((double)_delaySeconds * sampleRate, MidpointRounding.AwayFromZero));
            _history = new float[checked(frames * channels)];
            _channels = channels;
            Reset();
        }
    }
}
