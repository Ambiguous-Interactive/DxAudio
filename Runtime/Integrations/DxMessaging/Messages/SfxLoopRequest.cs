namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;
    using UnityEngine;

    /// <summary>Asks one <see cref="SfxPlayerMessaging"/> to hold a loop and answer with its id.</summary>
    /// <remarks>
    /// A loop is a state, so it is started and ended rather than fired: a started loop answers with
    /// <see cref="SfxLoopStartedMessage"/>, and that id stops it through <see cref="SfxLoopStopRequest"/>. An id the
    /// player never minted stops nothing, so a stale answer from an ended run is harmless.
    /// </remarks>
    public readonly struct SfxLoopRequest : ITargetedMessage<SfxLoopRequest>
    {
        /// <summary>The library key of the looping cue.</summary>
        public readonly string key;

        /// <summary>Where the sound is made. Read only when <see cref="isPositional"/> is set.</summary>
        public readonly Vector3 position;

        /// <summary>Whether the request named a position.</summary>
        public readonly bool isPositional;

        /// <summary>Asks to hold the loop flat.</summary>
        /// <param name="key">The library key of the looping cue.</param>
        public SfxLoopRequest(string key)
        {
            this.key = key;
            position = Vector3.zero;
            isPositional = false;
        }

        /// <summary>Asks to hold the loop at <paramref name="position"/>.</summary>
        /// <param name="key">The library key of the looping cue.</param>
        /// <param name="position">Where the sound is made.</param>
        public SfxLoopRequest(string key, Vector3 position)
        {
            this.key = key;
            this.position = position;
            isPositional = true;
        }
    }
}
