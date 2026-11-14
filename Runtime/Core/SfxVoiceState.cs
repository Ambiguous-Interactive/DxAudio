namespace WallstopStudios.DxAudio
{
    /// <summary>What one pooled voice is doing, as much of it as choosing the next voice needs.</summary>
    /// <remarks>
    /// Deliberately not an <c>AudioSource</c>: the interesting part of a pool is its policy, and a policy
    /// that takes engine objects can only be tested by building a scene. <c>SfxPlayer</c> fills these
    /// in from its sources and <see cref="SfxVoiceArbiter"/> reads them.
    ///
    /// <see langword="default"/> is deliberately <b>not</b> usable, so a slot nobody described is never
    /// chosen. Handing out a voice the pool knows nothing about plays nothing and hides the failure.
    /// </remarks>
    public readonly struct SfxVoiceState
    {
        public static readonly SfxVoiceState Idle = new(
            isUsable: true,
            isPlaying: false,
            isHeld: false,
            SfxPriority.Medium,
            startedAtUnscaledTime: 0f
        );

        /// <summary>
        /// Whether the voice still exists. A destroyed <c>AudioSource</c> reports that it is not playing, so
        /// without this it would read as the freest voice in the pool and swallow every cue.
        /// </summary>
        public readonly bool isUsable;

        public readonly bool isPlaying;

        /// <summary>Whether a looping cue holds this voice until its handle is released.</summary>
        /// <remarks>
        /// Separate from <see cref="isPlaying"/>: a loop is occupied for as long as the state it stands for
        /// lasts, whatever the engine reports this frame. A stolen one-shot is a sound cut short; a stolen
        /// loop is silence for the rest of that state, so a held voice is never taken.
        /// </remarks>
        public readonly bool isHeld;

        public readonly SfxPriority priority;

        public readonly float startedAtUnscaledTime;

        public SfxVoiceState(
            bool isUsable,
            bool isPlaying,
            bool isHeld,
            SfxPriority priority,
            float startedAtUnscaledTime
        )
        {
            this.isUsable = isUsable;
            this.isPlaying = isPlaying;
            this.isHeld = isHeld;
            this.priority = priority;
            this.startedAtUnscaledTime = startedAtUnscaledTime;
        }

        public bool IsFree => isUsable && !isPlaying && !isHeld;

        /// <summary>Whether this voice may be taken from at all, before priority is considered.</summary>
        public bool CanBeTakenFrom => isUsable && !isHeld;
    }
}
