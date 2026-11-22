namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A chorus stage with adjustable modulation rate, depth, and mix.</summary>
    [CreateAssetMenu(fileName = "DspChorus", menuName = "Wallstop Studios/DxAudio/DSP/Chorus")]
    public sealed class DspChorusAsset : DspEffectAsset
    {
        [Range(DspChorus.MinimumRateHertz, DspChorus.MaximumRateHertz)]
        [SerializeField]
        [Tooltip("Modulation cycles per second.")]
        private float _rateHertz = DspChorus.DefaultRateHertz;

        [Range(0f, DspChorus.MaximumDepthSeconds)]
        [SerializeField]
        [Tooltip("The maximum change from the center delay, in seconds.")]
        private float _depthSeconds = DspChorus.DefaultDepthSeconds;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("The fraction of the output from the delayed taps. Zero leaves the dry signal unchanged.")]
        private float _mix = DspChorus.DefaultMix;

        /// <summary>Builds a Chorus asset in code.</summary>
        public static DspChorusAsset Create(float rateHertz = DspChorus.DefaultRateHertz, float depthSeconds = DspChorus.DefaultDepthSeconds, float mix = DspChorus.DefaultMix)
        {
            DspChorusAsset asset = CreateInstance<DspChorusAsset>();
            asset._rateHertz = AudioMath.ClampFinite(rateHertz, DspChorus.MinimumRateHertz, DspChorus.MaximumRateHertz, DspChorus.DefaultRateHertz);
            asset._depthSeconds = AudioMath.ClampFinite(depthSeconds, 0f, DspChorus.MaximumDepthSeconds, DspChorus.DefaultDepthSeconds);
            asset._mix = AudioMath.ClampFinite(mix, 0f, 1f, DspChorus.DefaultMix);
            return asset;
        }

        public override string Describe()
        {
            return $"{_rateHertz:0.##} Hz, depth {_depthSeconds * 1000f:0.##} ms, mix {_mix:P0}";
        }

        public override DspEffect CreateEffect()
        {
            return new DspChorus
            {
                RateHertz = _rateHertz,
                DepthSeconds = _depthSeconds,
                Mix = _mix,
            };
        }
    }
}
