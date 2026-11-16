namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>Mutes or unmutes every sound-effect player, for a settings toggle.</summary>
    /// <remarks>
    /// Untargeted, because a setting names no scene object and every player holds the same one. Muting detaches
    /// output and keeps voice, loop and timer state, so unmuting resumes in time instead of restarting: a settings
    /// toggle is not a stop.
    /// </remarks>
    public readonly struct SfxMuteRequest : IUntargetedMessage<SfxMuteRequest>
    {
        /// <summary>Whether sounds should be silenced.</summary>
        public readonly bool muted;

        /// <summary>Asks every effect player to take <paramref name="muted"/> as its mute state.</summary>
        /// <param name="muted">Whether sounds should be silenced.</param>
        public SfxMuteRequest(bool muted)
        {
            this.muted = muted;
        }
    }
}
