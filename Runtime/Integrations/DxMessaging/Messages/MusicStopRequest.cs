namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>Asks every <see cref="MusicPlayerMessaging"/> to fade the music out.</summary>
    /// <remarks>
    /// Untargeted, for the same reason as <see cref="MusicPlayContextRequest"/>. It answers with
    /// <see cref="MusicStoppedMessage"/> when the player clears its selection.
    /// </remarks>
    public readonly struct MusicStopRequest : IUntargetedMessage<MusicStopRequest> { }
}
