namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;
    using UnityEngine;

    /// <summary>Asks one <see cref="SfxPlayerMessaging"/> to play a cue once.</summary>
    /// <remarks>
    /// Targeted, because a scene can hold more than one player and a play belongs to exactly one pool. Send it to
    /// the GameObject that carries both the player and its adapter:
    /// <c>SfxPlayRequest request = new("hit"); request.EmitGameObjectTargeted(playerObject);</c>
    /// </remarks>
    public readonly struct SfxPlayRequest : ITargetedMessage<SfxPlayRequest>
    {
        /// <summary>The library key the target player should play.</summary>
        public readonly string key;

        /// <summary>Where the sound was made. Read only when <see cref="isPositional"/> is set.</summary>
        public readonly Vector3 position;

        /// <summary>
        /// Whether the request named a position. A positional cue sent without one plays flat rather than at the
        /// world origin, which is the difference a request cannot express by leaving <see cref="position"/> at zero.
        /// </summary>
        public readonly bool isPositional;

        /// <summary>Asks for a flat play. A positional cue ignores the missing position.</summary>
        /// <param name="key">The library key to play.</param>
        public SfxPlayRequest(string key)
        {
            this.key = key;
            position = Vector3.zero;
            isPositional = false;
        }

        /// <summary>Asks for a play at <paramref name="position"/>. A flat cue ignores it.</summary>
        /// <param name="key">The library key to play.</param>
        /// <param name="position">Where the sound was made.</param>
        public SfxPlayRequest(string key, Vector3 position)
        {
            this.key = key;
            this.position = position;
            isPositional = true;
        }
    }
}
