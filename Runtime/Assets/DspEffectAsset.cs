namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>One stage of a chain, authored as data, so a chain is an asset like a cue or a track.</summary>
    /// <remarks>
    /// An asset is a recipe, not a live effect: every voice that plays it builds its own <see cref="DspEffect" />,
    /// because an effect keeps delay lines and envelopes that two voices must not share. A voice reads the asset once,
    /// the first time it meets it, and keeps those settings for as long as it lives, so an edit reaches only the voices
    /// that have not met the asset yet.
    /// </remarks>
    public abstract class DspEffectAsset : ScriptableObject
    {
        /// <summary>The settings this stage holds, as one line an inspector shows.</summary>
        public abstract string Describe();

        /// <summary>Builds an effect carrying this asset's settings, ready for <see cref="DspEffect.Prepare" />.</summary>
        public abstract DspEffect CreateEffect();
    }
}
