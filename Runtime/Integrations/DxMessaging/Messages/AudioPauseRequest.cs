namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;

    /// <summary>Pauses or resumes every sound-effect and music player, for a pause menu.</summary>
    /// <remarks>
    /// Untargeted, because a pause names no scene object and every player holds the same state. A pause holds each
    /// voice at its playhead and keeps loops, leases and mute state, so resuming continues rather than restarts.
    /// </remarks>
    public readonly struct AudioPauseRequest : IUntargetedMessage<AudioPauseRequest>
    {
        /// <summary>Whether audio should be held.</summary>
        public readonly bool paused;

        /// <summary>Asks every player to take <paramref name="paused"/> as its pause state.</summary>
        /// <param name="paused">Whether audio should be held.</param>
        public AudioPauseRequest(bool paused)
        {
            this.paused = paused;
        }
    }
}
