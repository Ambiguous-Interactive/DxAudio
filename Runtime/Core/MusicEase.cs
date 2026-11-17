namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>The shape of a fade or a duck, from start (0) to finish (1).</summary>
    /// <remarks>
    /// A small closed set rather than a tweening package's, so the package depends on nothing. Each value is
    /// one formula in <see cref="MusicEasing.Evaluate"/>.
    /// </remarks>
    public enum MusicEase
    {
        [Obsolete("A fade must declare its ease; Unknown is the value an unset field holds.", true)]
        Unknown = 0,

        Linear = 1,
        InQuad = 2,
        OutQuad = 3,
        InOutQuad = 4,
        InSine = 5,
        OutSine = 6,
        InOutSine = 7,
        InCubic = 8,
        OutCubic = 9,
        InOutCubic = 10,
    }

    public static class MusicEasing
    {
        /// <summary>The current member for an ease stored before the Unknown sentinel existed, where 0 was Linear.</summary>
        internal static MusicEase FromStoredBeforeUnknown(int stored)
        {
            return 0 <= stored && stored <= 9 ? (MusicEase)(stored + 1) : default;
        }

        /// <summary>How far through <paramref name="ease"/> the fade is at <paramref name="t"/>, both in [0, 1].</summary>
        public static float Evaluate(MusicEase ease, float t)
        {
            float x = AudioMath.Clamp01(t);
            switch (ease)
            {
                case MusicEase.InQuad:
                    return x * x;
                case MusicEase.OutQuad:
                    return 1f - ((1f - x) * (1f - x));
                case MusicEase.InOutQuad:
                    return x < 0.5f ? 2f * x * x : 1f - (Square((-2f * x) + 2f) / 2f);
                case MusicEase.InSine:
                    return 1f - (float)Math.Cos(x * Math.PI / 2.0);
                case MusicEase.OutSine:
                    return (float)Math.Sin(x * Math.PI / 2.0);
                case MusicEase.InOutSine:
                    return -((float)Math.Cos(Math.PI * x) - 1f) / 2f;
                case MusicEase.InCubic:
                    return x * x * x;
                case MusicEase.OutCubic:
                    return 1f - Cube(1f - x);
                case MusicEase.InOutCubic:
                    return x < 0.5f ? 4f * x * x * x : 1f - (Cube((-2f * x) + 2f) / 2f);
                default:
                    return x;
            }
        }

        private static float Square(float value)
        {
            return value * value;
        }

        private static float Cube(float value)
        {
            return value * value * value;
        }
    }
}
