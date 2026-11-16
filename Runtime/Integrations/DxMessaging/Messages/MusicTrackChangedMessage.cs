namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>The music a <see cref="MusicPlayerMessaging"/> now plays.</summary>
    /// <remarks>
    /// Broadcast from the player's GameObject, because a play request is untargeted and messages have no return value.
    /// The track is the selection the player announced, which can be the context's music or an override, so a listener
    /// that follows music follows every change rather than only the ones it asked for.
    /// </remarks>
    public readonly struct MusicTrackChangedMessage : IBroadcastMessage<MusicTrackChangedMessage>
    {
        /// <summary>The track the player now plays.</summary>
        public readonly MusicTrack track;

        /// <summary>Reports the track the player now plays.</summary>
        /// <param name="track">The track the player now plays.</param>
        public MusicTrackChangedMessage(MusicTrack track)
        {
            this.track = track;
        }
    }
}
