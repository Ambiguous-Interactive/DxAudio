namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>Ends the loop a <see cref="SfxLoopStartedMessage"/> named.</summary>
    /// <remarks>
    /// Targeted to the same GameObject that started the loop. An id this player did not mint stops nothing: the
    /// adapter forgets an id when it stops its loop, and the player's own lease stops a handle from an ended claim.
    /// </remarks>
    public readonly struct SfxLoopStopRequest : ITargetedMessage<SfxLoopStopRequest>
    {
        /// <summary>The id the starting player answered with.</summary>
        public readonly long loopId;

        /// <summary>Asks the target player to end the loop it minted as <paramref name="loopId"/>.</summary>
        /// <param name="loopId">The id the loop's <see cref="SfxLoopStartedMessage"/> carried.</param>
        public SfxLoopStopRequest(long loopId)
        {
            this.loopId = loopId;
        }
    }
}
