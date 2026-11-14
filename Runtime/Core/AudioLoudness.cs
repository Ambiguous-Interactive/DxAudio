namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>The gain that brings a measured clip to a loudness target, under a peak ceiling.</summary>
    /// <remarks>
    /// For setting cue volumes from measurements (LUFS and true peak, from any loudness meter) instead of by
    /// ear, so a mix stays balanced as clips are swapped. Targets are stated at the listener: the bus gain a
    /// cue plays through is subtracted, or a cue lands one bus's makeup too loud.
    ///
    /// The gain is never above one, because a cue volume cannot boost. A clip quieter than its target needs
    /// the bus or the clip itself raised, and <see cref="IsPeakLimited"/> says when the ceiling, not the
    /// target, decided the gain.
    /// </remarks>
    public static class AudioLoudness
    {
        /// <summary>A common ceiling for true peaks at the listener, in dBFS.</summary>
        public const float DefaultPeakCeilingDbfs = -3f;

        /// <summary>The linear gain for a clip measured at <paramref name="measuredLufs"/> to reach <paramref name="targetLufs"/>.</summary>
        public static float GainFor(
            float measuredLufs,
            float measuredPeakDbfs,
            float targetLufs,
            float busDb,
            float peakCeilingDbfs = DefaultPeakCeilingDbfs
        )
        {
            float toTargetDb = targetLufs - busDb - measuredLufs;
            float toCeilingDb = peakCeilingDbfs - busDb - measuredPeakDbfs;
            float gainDb = Math.Min(Math.Min(toTargetDb, toCeilingDb), 0f);
            return (float)Math.Pow(10.0, gainDb / 20.0);
        }

        /// <summary>Whether the peak ceiling, not the loudness target, sets the gain.</summary>
        public static bool IsPeakLimited(
            float measuredLufs,
            float measuredPeakDbfs,
            float targetLufs,
            float busDb,
            float peakCeilingDbfs = DefaultPeakCeilingDbfs
        )
        {
            float toTargetDb = targetLufs - busDb - measuredLufs;
            float toCeilingDb = peakCeilingDbfs - busDb - measuredPeakDbfs;
            return toCeilingDb < toTargetDb && toCeilingDb < 0f;
        }

        /// <summary>How loud a clip is heard at <paramref name="gain"/> through a bus of <paramref name="busDb"/>.</summary>
        /// <returns><see langword="false"/> for a gain of zero or less, which is heard as nothing.</returns>
        public static bool TryGetHeardLufs(float measuredLufs, float gain, float busDb, out float heardLufs)
        {
            if (gain <= 0f)
            {
                heardLufs = default;
                return false;
            }

            heardLufs = measuredLufs + (20f * (float)Math.Log10(gain)) + busDb;
            return true;
        }
    }
}
