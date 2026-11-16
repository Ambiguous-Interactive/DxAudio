namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>A loop a <see cref="SfxPlayerMessaging"/> now holds, and the id that ends it.</summary>
    /// <remarks>
    /// Broadcast from the player's GameObject, because the request it answers is targeted and messages have no
    /// return value. Keep the id: it is the only way to end this loop, and stopping another loop's id stops nothing.
    /// </remarks>
    public readonly struct SfxLoopStartedMessage : IBroadcastMessage<SfxLoopStartedMessage>
    {
        /// <summary>The id <see cref="SfxLoopStopRequest"/> takes to end this loop.</summary>
        public readonly long loopId;

        /// <summary>The library key the loop plays.</summary>
        public readonly string key;

        /// <summary>Reports a loop the player now holds.</summary>
        /// <param name="loopId">The id that ends this loop.</param>
        /// <param name="key">The library key the loop plays.</param>
        public SfxLoopStartedMessage(long loopId, string key)
        {
            this.loopId = loopId;
            this.key = key;
        }
    }
}
