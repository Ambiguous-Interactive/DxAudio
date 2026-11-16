namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>Asks every <see cref="MusicPlayerMessaging"/> to play the music for a context.</summary>
    /// <remarks>
    /// Untargeted, because a game normally has one music player and a context names a screen or level rather than a
    /// scene object. A context the library does not list changes nothing and answers with no fact.
    /// </remarks>
    public readonly struct MusicPlayContextRequest : IUntargetedMessage<MusicPlayContextRequest>
    {
        /// <summary>The music context to play.</summary>
        public readonly string context;

        /// <summary>Asks for <paramref name="context"/>'s music.</summary>
        /// <param name="context">The music context to play.</param>
        public MusicPlayContextRequest(string context)
        {
            this.context = context;
        }
    }
}
