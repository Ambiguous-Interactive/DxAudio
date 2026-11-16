namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>Releases the duck a <see cref="MusicDuckStartedMessage"/> named.</summary>
    /// <remarks>
    /// Targeted to the same GameObject that took the duck. An id this player did not issue releases nothing, and an
    /// id whose holder was destroyed releases nothing a second time. A <see cref="MusicDuckHolders.One"/> duck is one
    /// hold for every holder, so its id ends the duck rather than one share of it.
    /// </remarks>
    public readonly struct MusicUnduckRequest : ITargetedMessage<MusicUnduckRequest>
    {
        /// <summary>The id the ducking player answered with.</summary>
        public readonly long duckId;

        /// <summary>Asks the target player to release the duck it issued as <paramref name="duckId"/>.</summary>
        /// <param name="duckId">The id the duck's <see cref="MusicDuckStartedMessage"/> carried.</param>
        public MusicUnduckRequest(long duckId)
        {
            this.duckId = duckId;
        }
    }
}
