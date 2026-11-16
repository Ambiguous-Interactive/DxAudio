namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>A duck a <see cref="MusicPlayerMessaging"/> now holds, and the id that releases it.</summary>
    /// <remarks>
    /// Broadcast from the player's GameObject. Keep the id: it releases this hold, and an id the player did not issue
    /// releases nothing. A <see cref="MusicDuckHolders.One"/> duck is one hold for every holder, so any holder's id
    /// ends it. A duck the player refused answers with no fact.
    /// </remarks>
    public readonly struct MusicDuckStartedMessage : IBroadcastMessage<MusicDuckStartedMessage>
    {
        /// <summary>The id <see cref="MusicUnduckRequest"/> takes to release this hold.</summary>
        public readonly long duckId;

        /// <summary>The duck the player holds.</summary>
        public readonly MusicDuck duck;

        /// <summary>Reports a duck the player now holds.</summary>
        /// <param name="duckId">The id that releases this hold.</param>
        /// <param name="duck">The duck the player holds.</param>
        public MusicDuckStartedMessage(long duckId, MusicDuck duck)
        {
            this.duckId = duckId;
            this.duck = duck;
        }
    }
}
