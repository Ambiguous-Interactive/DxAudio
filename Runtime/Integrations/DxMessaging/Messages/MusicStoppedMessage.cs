namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>The music a <see cref="MusicPlayerMessaging"/> played has stopped.</summary>
    /// <remarks>
    /// Broadcast from the player's GameObject when the selection clears, the sounding voice loses its clip, or a
    /// one-shot track reaches its end. A listener uses it to leave a music-driven scene, such as a title screen,
    /// without polling the player.
    /// </remarks>
    public readonly struct MusicStoppedMessage : IBroadcastMessage<MusicStoppedMessage> { }
}
