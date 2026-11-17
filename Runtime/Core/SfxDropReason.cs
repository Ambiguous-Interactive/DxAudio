namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Why a cue did not play. Reported through <see cref="SfxPlayer.CueDropped"/>.</summary>
    public enum SfxDropReason
    {
        [Obsolete("Use a specific SfxDropReason value instead of Unknown.", true)]
        Unknown = 0,

        /// <summary>No cue, a key the library does not list, or a cue with no clip.</summary>
        NotPlayable = 1,

        /// <summary>The cue played inside its minimum interval. Expected during a burst; not a fault.</summary>
        Throttled = 2,

        /// <summary>Every voice was busy with a cue of equal or higher priority, or held by a loop.</summary>
        NoVoice = 3,

        /// <summary>A looping cue asked for a voice with the looping voice limit already reached.</summary>
        LoopLimit = 4,

        /// <summary>A looping cue sent to a one-shot method, or a one-shot cue sent to a looping one.</summary>
        WrongPlayMethod = 5,
    }
}
