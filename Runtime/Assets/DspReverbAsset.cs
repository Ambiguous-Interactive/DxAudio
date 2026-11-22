namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A room reverb with adjustable decay and wet level.</summary>
    [CreateAssetMenu(fileName = "DspReverb", menuName = "Wallstop Studios/DxAudio/DSP/Reverb")]
    public sealed class DspReverbAsset : DspEffectAsset
    {
        [Range(DspReverb.MinimumDecaySeconds, DspReverb.MaximumDecaySeconds)]
        [SerializeField]
        [Tooltip("The time in seconds for the reflections to fall by 60 dB.")]
        private float _decaySeconds = DspReverb.DefaultDecaySeconds;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("The level of the reflections. The dry signal stays at full level.")]
        private float _wetLevel = DspReverb.DefaultWetLevel;

        /// <summary>Builds a Reverb asset in code.</summary>
        public static DspReverbAsset Create(float decaySeconds = DspReverb.DefaultDecaySeconds, float wetLevel = DspReverb.DefaultWetLevel)
        {
            DspReverbAsset asset = CreateInstance<DspReverbAsset>();
            asset._decaySeconds = AudioMath.ClampFinite(decaySeconds, DspReverb.MinimumDecaySeconds, DspReverb.MaximumDecaySeconds, DspReverb.DefaultDecaySeconds);
            asset._wetLevel = AudioMath.ClampFinite(wetLevel, 0f, 1f, DspReverb.DefaultWetLevel);
            return asset;
        }

        public override string Describe()
        {
            return $"{_decaySeconds:0.##} s decay, wet {_wetLevel:P0}";
        }

        public override DspEffect CreateEffect()
        {
            return new DspReverb { DecaySeconds = _decaySeconds, WetLevel = _wetLevel };
        }
    }
}
