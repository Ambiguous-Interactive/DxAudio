namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Optional pitch and level variation applied once when a cue starts.</summary>
    [Serializable]
    public struct SfxVariation
    {
        public const float PitchLimitCents = 1200f;
        public const float MinimumLevelDecibels = -24f;

        public bool UsePitchCents;
        public float MinimumPitchCents;
        public float MaximumPitchCents;
        public float MinimumAttenuationDecibels;
        public float MaximumAttenuationDecibels;

        public SfxVariation(bool usePitchCents, float minimumPitchCents, float maximumPitchCents, float minimumLevelDecibels, float maximumLevelDecibels)
        {
            UsePitchCents = usePitchCents;
            MinimumPitchCents = minimumPitchCents;
            MaximumPitchCents = maximumPitchCents;
            MinimumAttenuationDecibels = minimumLevelDecibels;
            MaximumAttenuationDecibels = maximumLevelDecibels;
        }

        public bool HasInvalidBounds =>
            (UsePitchCents && Invalid(MinimumPitchCents, MaximumPitchCents, -PitchLimitCents, PitchLimitCents))
            || Invalid(MinimumAttenuationDecibels, MaximumAttenuationDecibels, MinimumLevelDecibels, 0f);

        internal float Pitch(float legacyJitter, float draw)
        {
            if (!UsePitchCents)
            {
                return 1f + AudioMath.Lerp(-legacyJitter, legacyJitter, draw);
            }

            float cents = Sample(MinimumPitchCents, MaximumPitchCents, -PitchLimitCents, PitchLimitCents, draw);
            return (float)Math.Pow(2.0, cents / 1200.0);
        }

        private float LevelDecibels(float draw) => Sample(MinimumAttenuationDecibels, MaximumAttenuationDecibels, MinimumLevelDecibels, 0f, draw);

        internal float Volume(float baseVolume, float draw, out float levelDecibels)
        {
            levelDecibels = LevelDecibels(draw);
            return levelDecibels == 0f ? baseVolume : baseVolume * AudioMath.DecibelsToLinear(levelDecibels);
        }

        private static bool Invalid(float min, float max, float lower, float upper) =>
            !AudioMath.IsFinite(min) || !AudioMath.IsFinite(max) || min < lower || max < min || upper < max;

        private static float Sample(float min, float max, float lower, float upper, float draw)
        {
            min = AudioMath.IsFinite(min) ? AudioMath.Clamp(min, lower, upper) : 0f;
            max = AudioMath.IsFinite(max) ? AudioMath.Clamp(max, lower, upper) : 0f;
            return AudioMath.Lerp(Math.Min(min, max), Math.Max(min, max), draw);
        }
    }
}
