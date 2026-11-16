namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Core.Messages;
    using UnityEngine;

    /// <summary>Asks one <see cref="MusicPlayerMessaging"/> to duck the music and answer with its id.</summary>
    /// <remarks>
    /// Targeted, because ducks belong to one player's stack. The holder is how a game ties a duck to the object that
    /// wanted it: destroying that object releases the duck on the next frame, the same way a direct
    /// <see cref="MusicPlayer.Duck"/> call does. The answer is <see cref="MusicDuckStartedMessage"/>.
    /// </remarks>
    public readonly struct MusicDuckRequest : ITargetedMessage<MusicDuckRequest>
    {
        /// <summary>The duck to hold.</summary>
        public readonly MusicDuck duck;

        /// <summary>Who holds it, or <see langword="null"/> for a state nobody can destroy.</summary>
        /// <remarks>A <see cref="MusicDuckHolders.One"/> duck is one state, so it ignores this.</remarks>
        public readonly Object holder;

        /// <summary>Asks for a duck with no holder.</summary>
        /// <param name="duck">The duck to hold.</param>
        public MusicDuckRequest(MusicDuck duck)
        {
            this.duck = duck;
            holder = null;
        }

        /// <summary>Asks for a duck held by <paramref name="holder"/>.</summary>
        /// <param name="duck">The duck to hold.</param>
        /// <param name="holder">The object whose destruction releases the duck.</param>
        public MusicDuckRequest(MusicDuck duck, Object holder)
        {
            this.duck = duck;
            this.holder = holder;
        }
    }
}
