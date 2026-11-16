namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>A cue that did not play, and why, as a fact any listener can observe.</summary>
    /// <remarks>
    /// Broadcast from the player's GameObject. This is the messaging form of <see cref="SfxPlayer.CueDropped"/>, so
    /// telemetry, a tutorial hint and a diagnostics overlay all read one fact instead of subscribing to the player
    /// directly. <see cref="cue"/> is <see langword="null"/> when the request named no cue the library lists, which
    /// is the one drop that cannot report a key.
    /// </remarks>
    public readonly struct SfxCueDroppedMessage : IBroadcastMessage<SfxCueDroppedMessage>
    {
        /// <summary>The cue that did not play, or <see langword="null"/> when no cue was found.</summary>
        public readonly SfxCue cue;

        /// <summary>Why it did not play.</summary>
        public readonly SfxDropReason reason;

        /// <summary>Reports a dropped cue.</summary>
        /// <param name="cue">The cue that did not play, or <see langword="null"/> when no cue was found.</param>
        /// <param name="reason">Why it did not play.</param>
        public SfxCueDroppedMessage(SfxCue cue, SfxDropReason reason)
        {
            this.cue = cue;
            this.reason = reason;
        }
    }
}
