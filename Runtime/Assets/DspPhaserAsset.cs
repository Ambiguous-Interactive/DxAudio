namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A phaser stage with adjustable sweep rate and depth.</summary>
    [CreateAssetMenu(fileName = "DspPhaser", menuName = "Wallstop Studios/DxAudio/DSP/Phaser")]
    public sealed class DspPhaserAsset : DspEffectAsset
    {
        [Range(DspPhaser.MinimumRateHertz, DspPhaser.MaximumRateHertz)]
        [SerializeField]
        [Tooltip("Sweep cycles per second.")]
        private float _rateHertz = DspPhaser.DefaultRateHertz;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("Zero leaves the signal dry. One gives the strongest notches.")]
        private float _depth = DspPhaser.DefaultDepth;

        /// <summary>Builds a Phaser asset in code.</summary>
        public static DspPhaserAsset Create(float rateHertz = DspPhaser.DefaultRateHertz, float depth = DspPhaser.DefaultDepth)
        {
            DspPhaserAsset asset = CreateInstance<DspPhaserAsset>();
            asset._rateHertz = DspPhaser.ClampRate(rateHertz);
            asset._depth = DspPhaser.ClampDepth(depth);
            return asset;
        }

        public override string Describe()
        {
            return $"{_rateHertz:0.##} Hz, depth {_depth:P0}";
        }

        public override DspEffect CreateEffect()
        {
            return new DspPhaser { RateHertz = _rateHertz, Depth = _depth };
        }
    }
}
