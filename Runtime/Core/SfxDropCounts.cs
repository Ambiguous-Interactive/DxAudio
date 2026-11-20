namespace WallstopStudios.DxAudio
{
    /// <summary>How many cues were dropped, one count per reason, for a diagnostic.</summary>
    /// <remarks>
    /// A world value, not a running total: <see cref="SfxPlayer.DropCounts"/> builds it from the counters the
    /// player keeps, so reading it allocates nothing and can run every frame while an overlay is open.
    /// </remarks>
    public readonly struct SfxDropCounts
    {
        public static readonly SfxDropCounts None = default;

        /// <summary>Every cue that did not play, whatever the reason.</summary>
        public int Total => notPlayable + throttled + noVoice + loopLimit + wrongPlayMethod;

        /// <summary>How many cues were dropped for <paramref name="reason"/>.</summary>
        public int this[SfxDropReason reason]
        {
            get
            {
                switch (reason)
                {
                    case SfxDropReason.NotPlayable:
                        return notPlayable;
                    case SfxDropReason.Throttled:
                        return throttled;
                    case SfxDropReason.NoVoice:
                        return noVoice;
                    case SfxDropReason.LoopLimit:
                        return loopLimit;
                    case SfxDropReason.WrongPlayMethod:
                        return wrongPlayMethod;
                    default:
                        return 0;
                }
            }
        }

        public readonly int notPlayable;
        public readonly int throttled;
        public readonly int noVoice;
        public readonly int loopLimit;
        public readonly int wrongPlayMethod;

        public SfxDropCounts(int notPlayable, int throttled, int noVoice, int loopLimit, int wrongPlayMethod)
        {
            this.notPlayable = notPlayable;
            this.throttled = throttled;
            this.noVoice = noVoice;
            this.loopLimit = loopLimit;
            this.wrongPlayMethod = wrongPlayMethod;
        }
    }
}
