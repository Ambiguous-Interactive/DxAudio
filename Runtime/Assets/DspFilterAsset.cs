namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A filter stage: the band it shapes, where its corner sits, how narrow the band is and how far it lifts.</summary>
    [CreateAssetMenu(fileName = "DspFilter", menuName = "DxAudio/DSP/Filter")]
    public sealed class DspFilterAsset : DspEffectAsset
    {
        /// <summary>The band this stage shapes.</summary>
        public DspFilterMode Mode => _mode;

        /// <summary>The corner of the band, in hertz.</summary>
        public float FrequencyHz => _frequencyHz;

        /// <summary>How narrow the band is.</summary>
        public float Quality => _quality;

        /// <summary>How far the Peak or Bell mode moves its band, in decibels.</summary>
        public float PeakDecibels => _peakDecibels;

        [SerializeField]
        [Tooltip("Which part of the spectrum the stage keeps. The default, Low Pass, muffles the sound above the corner.")]
        private DspFilterMode _mode = DspFilterMode.LowPass;

        [Range(DspFilter.MinimumFrequencyHz, DspFilter.MaximumFrequencyHz)]
        [SerializeField]
        [Tooltip("The corner of the band, in hertz. The prepared rate caps it below the Nyquist frequency.")]
        private float _frequencyHz = DspFilter.DefaultFrequencyHz;

        [Range(DspFilter.MinimumQuality, DspFilter.MaximumQuality)]
        [SerializeField]
        [Tooltip("How narrow the band is. A higher value narrows it and lifts the corner of a low or high pass; 0.71 is flat.")]
        private float _quality = DspFilter.DefaultQuality;

        [Range(DspFilter.MinimumPeakDecibels, DspFilter.MaximumPeakDecibels)]
        [SerializeField]
        [Tooltip("How far the Peak or Bell mode moves its band, in decibels. Every other mode ignores it.")]
        private float _peakDecibels;

        /// <summary>Builds a filter asset in code, for tests and for games that author audio at runtime.</summary>
        public static DspFilterAsset Create(
            DspFilterMode mode = DspFilterMode.LowPass,
            float frequencyHz = DspFilter.DefaultFrequencyHz,
            float quality = DspFilter.DefaultQuality,
            float peakDecibels = 0f
        )
        {
            DspFilterAsset asset = CreateInstance<DspFilterAsset>();
            asset._mode = mode;
            asset._frequencyHz = AudioMath.IsFinite(frequencyHz)
                ? Mathf.Clamp(frequencyHz, DspFilter.MinimumFrequencyHz, DspFilter.MaximumFrequencyHz)
                : DspFilter.DefaultFrequencyHz;
            asset._quality = AudioMath.IsFinite(quality) ? Mathf.Clamp(quality, DspFilter.MinimumQuality, DspFilter.MaximumQuality) : DspFilter.DefaultQuality;
            asset._peakDecibels = AudioMath.IsFinite(peakDecibels) ? Mathf.Clamp(peakDecibels, DspFilter.MinimumPeakDecibels, DspFilter.MaximumPeakDecibels) : 0f;
            return asset;
        }

        private static string DescribeMode(DspFilterMode mode)
        {
            switch (mode)
            {
                case DspFilterMode.LowPass:
                    return "low pass";
                case DspFilterMode.HighPass:
                    return "high pass";
                case DspFilterMode.BandPass:
                    return "band pass";
                case DspFilterMode.Notch:
                    return "notch";
                case DspFilterMode.Peak:
                    return "peak";
                case DspFilterMode.Bell:
                    return "bell";
                default:
                    return "unset";
            }
        }

        public override string Describe()
        {
            string corner = $"{DescribeMode(_mode)} {_frequencyHz:0.#} Hz, Q {_quality:0.##}";
            return _mode == DspFilterMode.Peak || _mode == DspFilterMode.Bell ? $"{corner}, {_peakDecibels:+0.#;-0.#;0} dB" : corner;
        }

        public override DspEffect CreateEffect()
        {
            return new DspFilter
            {
                Mode = _mode,
                FrequencyHz = _frequencyHz,
                Quality = _quality,
                PeakDecibels = _peakDecibels,
            };
        }
    }
}
