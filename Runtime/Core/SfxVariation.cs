namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Optional take selection, pitch, level and brightness settings for cue playback.</summary>
    [Serializable]
    public struct SfxVariation
    {
        public const float PitchLimitCents = 1200f;
        public const float MinimumLevelDecibels = -24f;
        public const float LowestLowPassHertz = 500f;
        public const float HighestLowPassHertz = 20_000f;

        public bool HasInvalidBounds =>
            (UsePitchCents && Invalid(MinimumPitchCents, MaximumPitchCents, -PitchLimitCents, PitchLimitCents))
            || Invalid(MinimumAttenuationDecibels, MaximumAttenuationDecibels, MinimumLevelDecibels, 0f)
            || (UseLowPass && Invalid(MinimumLowPassHertz, MaximumLowPassHertz, LowestLowPassHertz, HighestLowPassHertz));

        public bool UsePitchCents;
        public float MinimumPitchCents;
        public float MaximumPitchCents;
        public float MinimumAttenuationDecibels;
        public float MaximumAttenuationDecibels;

        /// <summary>Use each playable take once per cycle when picking a take for this cue.</summary>
        public bool UseShuffle;

        /// <summary>Apply a low-pass filter before the cue's effects.</summary>
        public bool UseLowPass;
        public float MinimumLowPassHertz;
        public float MaximumLowPassHertz;

        public SfxVariation(bool usePitchCents, float minimumPitchCents, float maximumPitchCents, float minimumLevelDecibels, float maximumLevelDecibels)
        {
            UsePitchCents = usePitchCents;
            MinimumPitchCents = minimumPitchCents;
            MaximumPitchCents = maximumPitchCents;
            MinimumAttenuationDecibels = minimumLevelDecibels;
            MaximumAttenuationDecibels = maximumLevelDecibels;
            UseShuffle = false;
            UseLowPass = false;
            MinimumLowPassHertz = 0f;
            MaximumLowPassHertz = 0f;
        }

        private static bool Invalid(float min, float max, float lower, float upper) =>
            !AudioMath.IsFinite(min) || !AudioMath.IsFinite(max) || min < lower || max < min || upper < max;

        private static float Sample(float min, float max, float lower, float upper, float draw)
        {
            min = AudioMath.IsFinite(min) ? AudioMath.Clamp(min, lower, upper) : 0f;
            max = AudioMath.IsFinite(max) ? AudioMath.Clamp(max, lower, upper) : 0f;
            return AudioMath.Lerp(Math.Min(min, max), Math.Max(min, max), draw);
        }

        internal float LowPassHertz(float draw)
        {
            if (!UseLowPass || !AudioMath.IsFinite(MinimumLowPassHertz) || !AudioMath.IsFinite(MaximumLowPassHertz))
            {
                return 0f;
            }

            float lower = AudioMath.Clamp(Math.Min(MinimumLowPassHertz, MaximumLowPassHertz), LowestLowPassHertz, HighestLowPassHertz);
            float upper = AudioMath.Clamp(Math.Max(MinimumLowPassHertz, MaximumLowPassHertz), LowestLowPassHertz, HighestLowPassHertz);
            if (lower == upper || draw <= 0f)
            {
                return lower;
            }

            return 1f <= draw ? upper : (float)(lower * Math.Pow(upper / lower, draw));
        }

        internal float Pitch(float legacyJitter, float draw)
        {
            if (!UsePitchCents)
            {
                return 1f + AudioMath.Lerp(-legacyJitter, legacyJitter, draw);
            }

            float cents = Sample(MinimumPitchCents, MaximumPitchCents, -PitchLimitCents, PitchLimitCents, draw);
            return (float)Math.Pow(2.0, cents / 1200.0);
        }

        internal float Volume(float baseVolume, float draw, out float levelDecibels)
        {
            levelDecibels = LevelDecibels(draw);
            return levelDecibels == 0f ? baseVolume : baseVolume * AudioMath.DecibelsToLinear(levelDecibels);
        }

        private float LevelDecibels(float draw) => Sample(MinimumAttenuationDecibels, MaximumAttenuationDecibels, MinimumLevelDecibels, 0f, draw);
    }
}
