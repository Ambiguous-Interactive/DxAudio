namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A tremolo stage with adjustable rate and depth.</summary>
    [CreateAssetMenu(fileName = "DspTremolo", menuName = "Wallstop Studios/DxAudio/DSP/Tremolo")]
    public sealed class DspTremoloAsset : DspEffectAsset
    {
        [Range(DspTremolo.MinimumRateHertz, DspTremolo.MaximumRateHertz)]
        [SerializeField]
        [Tooltip("Gain cycles per second.")]
        private float _rateHertz = DspTremolo.DefaultRateHertz;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("The largest drop from full gain. Zero leaves the signal unchanged.")]
        private float _depth = DspTremolo.DefaultDepth;

        /// <summary>Builds a Tremolo asset in code.</summary>
        public static DspTremoloAsset Create(float rateHertz = DspTremolo.DefaultRateHertz, float depth = DspTremolo.DefaultDepth)
        {
            DspTremoloAsset asset = CreateInstance<DspTremoloAsset>();
            asset._rateHertz = DspTremolo.ClampRate(rateHertz);
            asset._depth = DspTremolo.ClampDepth(depth);
            return asset;
        }

        public override string Describe()
        {
            return $"{_rateHertz:0.##} Hz, depth {_depth:P0}";
        }

        public override DspEffect CreateEffect()
        {
            return new DspTremolo { RateHertz = _rateHertz, Depth = _depth };
        }
    }
}
