namespace WallstopStudios.DxAudio
{
    /// <summary>Scales the difference between stereo channels while keeping their average.</summary>
    public sealed class DspStereoWidth : DspEffect
    {
        public const float MinimumWidth = 0f;
        public const float MaximumWidth = 2f;
        public const float DefaultWidth = 1f;

        public override string DisplayName => "Stereo Width";

        /// <summary>The side gain from zero to two. One bypasses the effect. Changes apply at the next preparation.</summary>
        public float Width
        {
            get => _width;
            set => _width = ClampWidth(value);
        }

        private float _width = DefaultWidth;
        private float _preparedWidth = DefaultWidth;

        internal static float ClampWidth(float value)
        {
            return AudioMath.ClampFinite(value, MinimumWidth, MaximumWidth, DefaultWidth);
        }

        public override void Process(float[] buffer, int offset, int count, int channels)
        {
            float width = _preparedWidth;
            if (width == DefaultWidth || channels != 2 || buffer == null || offset < 0 || count <= 0 || count % 2 != 0 || buffer.Length - count < offset)
            {
                return;
            }

            double sideGain = width * 0.5d;
            int end = offset + count;
            for (int index = offset; index < end; index += 2)
            {
                double left = buffer[index];
                double right = buffer[index + 1];
                double mid = (left + right) * 0.5d;
                double side = (left - right) * sideGain;
                buffer[index] = (float)(mid + side);
                buffer[index + 1] = (float)(mid - side);
            }
        }

        protected override void PrepareCore(int sampleRate, int maxBlockSize, int channels)
        {
            _preparedWidth = channels == 2 ? _width : DefaultWidth;
        }
    }
}
