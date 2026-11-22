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

        /// <summary>Whether <paramref name="value"/> is a real number, so a bad knob cannot poison a filter or envelope.</summary>
        /// <remarks>
        /// A serialized field can carry a NaN or an infinity. Every recursive stage keeps state, so one bad value
        /// would not be a one-block glitch: the effect would stay dead until it is reset. Setters refuse one rather
        /// than pass it on.
        /// </remarks>
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// The per-sample coefficient of a one-pole smoother that closes about 63% of a step in
        /// <paramref name="seconds"/>.
        /// </summary>
        /// <remarks>
        /// A knob move that lands on the next block clicks; smoothing it per sample turns the step into a short
        /// ramp. A zero time yields a coefficient of one, so the smoother lands on its target at once instead of
        /// dividing by zero.
        /// </remarks>
        public static float OnePoleCoefficient(float seconds, int sampleRate)
        {
            if (seconds <= 0f || sampleRate <= 0)
            {
                return 1f;
            }

            /*
                The step is computed in double: a float product overflows for a long time constant, and the
                exponential then rounds to exactly one. A tiny step uses its series, 1 - exp(-x) ~ x, which keeps a
                very long smoother moving rather than freezing it at zero.
            */
            double step = 1.0 / ((double)seconds * sampleRate);
            return step < 1e-8 ? (float)step : (float)(1.0 - Math.Exp(-step));
        }

        internal static float ClampFinite(float value, float minimum, float maximum, float fallback)
        {
            return IsFinite(value) ? Clamp(value, minimum, maximum) : fallback;
        }
    }
}
