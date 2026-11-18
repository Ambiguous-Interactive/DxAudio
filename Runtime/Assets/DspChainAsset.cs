namespace WallstopStudios.DxAudio
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Effects in the order the signal meets them, authored as data and built per voice.</summary>
    /// <remarks>
    /// Order is the sound, so the list is kept in authoring order and built in that order. A chain asset is a recipe:
    /// two voices playing the same asset run their own effect instances, because the state an effect keeps belongs to
    /// the voice that is playing. The cue, the track or the player's bus names the asset; nothing here runs audio.
    /// </remarks>
    [CreateAssetMenu(fileName = "DspChain", menuName = "DxAudio/DSP/Chain")]
    public sealed class DspChainAsset : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Effects in the order the signal meets them, first at the top. Empty slots are skipped, not silenced.")]
        private List<DspEffectAsset> _effects = new();

        public IReadOnlyList<DspEffectAsset> Effects => _effects;

        public int Count => _effects.Count;

        /// <summary>Builds a chain with one fresh effect for every authored stage, ready for <c>TryPrepare</c>.</summary>
        public DspChain CreateChain()
        {
            DspChain chain = new();
            foreach (DspEffectAsset effect in _effects)
            {
                if (effect != null)
                {
                    chain.Add(effect.CreateEffect());
                }
            }

            return chain;
        }

        /// <summary>Builds a chain asset in code, for tests and for games that author audio at runtime.</summary>
        public static DspChainAsset Create(IEnumerable<DspEffectAsset> effects)
        {
            DspChainAsset asset = CreateInstance<DspChainAsset>();
            asset._effects = effects == null ? new List<DspEffectAsset>() : new List<DspEffectAsset>(effects);
            return asset;
        }
    }
}
