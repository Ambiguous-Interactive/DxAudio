namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A bit crusher stage: the bit depth a sample keeps, and how many frames each kept sample lasts.</summary>
    [CreateAssetMenu(fileName = "DspBitCrusher", menuName = "Wallstop Studios/DxAudio/DSP/Bit Crusher")]
    public sealed class DspBitCrusherAsset : DspEffectAsset
    {
        public int Bits => _bits;

        public int RateReduction => _rateReduction;

        [Range(1, 24)]
        [SerializeField]
        [Tooltip("How many bits each sample keeps. Eight is audible grit; one keeps only the two ends of the range.")]
        private int _bits = DspBitCrusher.DefaultBits;

        [Min(1)]
        [SerializeField]
        [Tooltip("How many frames each kept sample lasts. One rewrites every frame; four is a rough, lower sample rate.")]
        private int _rateReduction = DspBitCrusher.DefaultRateReduction;

        /// <summary>Builds a bit crusher asset in code, for tests and for games that author audio at runtime.</summary>
        public static DspBitCrusherAsset Create(int bits = DspBitCrusher.DefaultBits, int rateReduction = DspBitCrusher.DefaultRateReduction)
        {
            DspBitCrusherAsset asset = CreateInstance<DspBitCrusherAsset>();
            asset._bits = Mathf.Clamp(bits, 1, 24);
            asset._rateReduction = Mathf.Max(1, rateReduction);
            return asset;
        }

        public override string Describe()
        {
            return $"{_bits} bit, every {_rateReduction} frame{(_rateReduction == 1 ? string.Empty : "s")}";
        }

        public override DspEffect CreateEffect()
        {
            return new DspBitCrusher { Bits = _bits, RateReduction = _rateReduction };
        }
    }
}
