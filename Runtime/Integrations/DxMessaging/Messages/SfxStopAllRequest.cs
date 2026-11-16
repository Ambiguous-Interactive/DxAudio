namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>Asks one <see cref="SfxPlayerMessaging"/> to stop every voice it plays, loops included.</summary>
    /// <remarks>
    /// For a scene change or a menu that silences the world. A stop is not a pause: the voices are released, so a
    /// loop that should resume is stopped through <see cref="SfxLoopStopRequest"/> and requested again.
    /// </remarks>
    public readonly struct SfxStopAllRequest : ITargetedMessage<SfxStopAllRequest> { }
}
