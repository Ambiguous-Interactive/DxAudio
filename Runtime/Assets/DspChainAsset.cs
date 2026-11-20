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
        public IReadOnlyList<DspEffectAsset> Effects => _effects;

        public int Count => _effects.Count;

        [SerializeField]
        [Tooltip("Effects in the order the signal meets them, first at the top. Empty slots are skipped, not silenced.")]
        private List<DspEffectAsset> _effects = new();

        /// <summary>Builds a chain asset in code, for tests and for games that author audio at runtime.</summary>
        public static DspChainAsset Create(IEnumerable<DspEffectAsset> effects)
        {
            DspChainAsset asset = CreateInstance<DspChainAsset>();
            asset._effects = effects == null ? new List<DspEffectAsset>() : new List<DspEffectAsset>(effects);
            return asset;
        }

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

#if UNITY_EDITOR
        internal DrawnEqSettings DrawnEq => _drawnEq;

        [SerializeField, HideInInspector]
        private DrawnEqSettings _drawnEq;

        internal void SetDrawnEq(DrawnEqSettings settings, List<DspEffectAsset> effects)
        {
            _drawnEq = settings;
            _effects = effects;
        }

        [System.Serializable]
        internal struct DrawnEqStage
        {
            internal DspFilterAsset Filter => _filter;
            internal int PrecedingManualStages => _precedingManualStages;

            [SerializeField]
            private DspFilterAsset _filter;

            [SerializeField]
            private int _precedingManualStages;

            internal DrawnEqStage(DspFilterAsset filter, int precedingManualStages)
            {
                _filter = filter;
                _precedingManualStages = precedingManualStages;
            }
        }

        [System.Serializable]
        internal sealed class DrawnEqSettings
        {
            internal float[] Target => _target;
            internal int SampleRate => _sampleRate;
            internal int BandLimit => _bandLimit;
            internal DrawnEqStage[] GeneratedStages => _generatedStages;

            [SerializeField]
            private float[] _target;

            [SerializeField]
            private int _sampleRate;

            [SerializeField]
            private int _bandLimit;

            [SerializeField]
            private DrawnEqStage[] _generatedStages;

            internal DrawnEqSettings(float[] target, int sampleRate, int bandLimit, DrawnEqStage[] generatedStages)
            {
                _target = (float[])target.Clone();
                _sampleRate = sampleRate;
                _bandLimit = bandLimit;
                _generatedStages = generatedStages;
            }
        }
#endif
    }
}
