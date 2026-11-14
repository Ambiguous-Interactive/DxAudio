namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>The handful of numeric helpers the audio types share.</summary>
    /// <remarks>
    /// Written here rather than taken from <c>Mathf</c> so the core types compile without the engine and
    /// their tests run on plain .NET.
    /// </remarks>
    public static class AudioMath
    {
        /// <summary>The level a mixer treats as silence, in decibels.</summary>
        public const float SilentDecibels = -80f;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return max < value ? max : value;
        }

        public static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        public static float Lerp(float from, float to, float t)
        {
            return from + ((to - from) * Clamp01(t));
        }

        /// <summary>The remainder of <paramref name="value"/> over <paramref name="divisor"/>, never negative.</summary>
        public static int PositiveMod(int value, int divisor)
        {
            int remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }

        /// <summary>A linear gain as mixer decibels, with zero and below as <see cref="SilentDecibels"/>.</summary>
        public static float LinearToDecibels(float linear)
        {
            if (linear <= 0f)
            {
                return SilentDecibels;
            }

            float decibels = 20f * (float)Math.Log10(linear);
            return Math.Max(SilentDecibels, decibels);
        }

        /// <summary>Mixer decibels as a linear gain, with <see cref="SilentDecibels"/> and below as zero.</summary>
        public static float DecibelsToLinear(float decibels)
        {
            if (decibels <= SilentDecibels)
            {
                return 0f;
            }

            return (float)Math.Pow(10.0, decibels / 20.0);
        }
    }
}
