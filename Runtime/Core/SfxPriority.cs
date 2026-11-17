namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>How much a cue deserves one of the pool's voices when they are all busy.</summary>
    /// <remarks>
    /// Ordered least to most important: <see cref="SfxVoiceArbiter"/> compares these values directly, so a
    /// new value must be inserted at its rank rather than appended.
    ///
    /// This ranks what a cue may interrupt while the game runs, not how much the sound matters to make.
    /// A footstep is important to get right and still the first thing that should give way.
    /// </remarks>
    public enum SfxPriority
    {
        [Obsolete("Use a specific SfxPriority value instead of Unknown.", true)]
        Unknown = 0,

        /// <summary>Texture. Nobody notices if it never plays.</summary>
        Low = 1,

        /// <summary>The default. Most cues want this.</summary>
        Medium = 2,

        /// <summary>
        /// A beat the player is meant to hear: progress, success, failure. May take a voice from a lower cue.
        /// </summary>
        High = 3,
    }
}
