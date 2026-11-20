namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>What has to happen to the voices when new music is asked for.</summary>
    public enum MusicHandOffKind
    {
        [Obsolete("Use a specific MusicHandOffKind value instead of Unknown.", true)]
        Unknown = 0,

        /// <summary>Nothing is audible: fade the new music in from silence.</summary>
        StartFromSilence = 1,

        /// <summary>Other music is audible: fade it out while the new music fades in.</summary>
        Crossfade = 2,

        /// <summary>The same music is already audible: leave it alone rather than restarting it.</summary>
        KeepSounding = 3,

        /// <summary>
        /// The asked-for music is still fading out: fade that voice back in from where it is, so returning to
        /// a screen a moment after leaving it continues the music instead of restarting it.
        /// </summary>
        ReclaimOutgoing = 4,
    }

    /// <summary>Decides how new music takes over from what is playing.</summary>
    public static class MusicHandOff
    {
        /// <param name="sameAsSounding">Whether the asked-for music is the music on the main voice.</param>
        /// <param name="soundingIsAudible">Whether the main voice is playing and not on its way out.</param>
        /// <param name="incomingIsFadingOut">Whether the asked-for music is on a voice that is fading out.</param>
        public static MusicHandOffKind Decide(bool sameAsSounding, bool soundingIsAudible, bool incomingIsFadingOut)
        {
            if (soundingIsAudible && sameAsSounding)
            {
                return MusicHandOffKind.KeepSounding;
            }

            if (incomingIsFadingOut)
            {
                return MusicHandOffKind.ReclaimOutgoing;
            }

            return soundingIsAudible ? MusicHandOffKind.Crossfade : MusicHandOffKind.StartFromSilence;
        }
    }

    /// <summary>When a fading-out voice has finished and may be stopped.</summary>
    /// <remarks>
    /// A deadline rather than a check on volume, because a voice whose fade was retargeted halfway is quiet
    /// without being finished. Nullable rather than a sentinel: "no deadline" is the absence of a value.
    /// </remarks>
    public sealed class MusicStopSchedule
    {
        public bool IsScheduled => _deadline.HasValue;
        private float? _deadline;

        public void Arm(float now, float fadeSeconds)
        {
            _deadline = now + Math.Max(0f, fadeSeconds);
        }

        public void Cancel()
        {
            _deadline = null;
        }

        /// <summary>Whether the deadline has passed. A passed deadline is spent, so this is true once.</summary>
        public bool TryFinish(float now)
        {
            // A lifted comparison against null is false, so an unscheduled stop is never due.
            if (!(_deadline <= now))
            {
                return false;
            }

            Cancel();
            return true;
        }
    }
}
