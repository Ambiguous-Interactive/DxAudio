namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A limiter stage: a ceiling, the lookahead that lets it anticipate a peak, and a release.</summary>
    [CreateAssetMenu(fileName = "DspLimiter", menuName = "DxAudio/DSP/Limiter")]
    public sealed class DspLimiterAsset : DspEffectAsset
    {
        [Range(-48f, 0f)]
        [SerializeField]
        [Tooltip("The loudest sample that leaves the limiter, in dBFS. Leave room under zero for inter-sample peaks.")]
        private float _ceilingDecibels = DspLimiter.DefaultCeilingDecibels;

        [Min(0f)]
        [SerializeField]
        [Tooltip("How far ahead the gain comes down, so a peak is turned down before it arrives. A new value applies at the next play.")]
        private float _lookaheadSeconds = 0.005f;

        [Min(0f)]
        [SerializeField]
        [Tooltip("How quickly the gain returns after a peak. Too short pumps; too long stays quiet.")]
        private float _releaseSeconds = 0.05f;

        /// <summary>The loudest sample that leaves this stage, in dBFS.</summary>
        public float CeilingDecibels => _ceilingDecibels;

        public float LookaheadSeconds => _lookaheadSeconds;

        public float ReleaseSeconds => _releaseSeconds;

        /// <summary>Builds a limiter asset in code, for tests and for games that author audio at runtime.</summary>
        public static DspLimiterAsset Create(float ceilingDecibels = DspLimiter.DefaultCeilingDecibels, float lookaheadSeconds = 0.005f, float releaseSeconds = 0.05f)
        {
            DspLimiterAsset asset = CreateInstance<DspLimiterAsset>();
            asset._ceilingDecibels = Mathf.Clamp(ceilingDecibels, -48f, 0f);
            asset._lookaheadSeconds = Mathf.Max(0f, lookaheadSeconds);
            asset._releaseSeconds = Mathf.Max(0f, releaseSeconds);
            return asset;
        }

        public override string Describe()
        {
            return $"ceiling {_ceilingDecibels:0.#} dB, lookahead {_lookaheadSeconds * 1000f:0.#} ms, release {_releaseSeconds * 1000f:0.#} ms";
        }

        public override DspEffect CreateEffect()
        {
            return new DspLimiter
            {
                Ceiling = AudioMath.DecibelsToLinear(_ceilingDecibels),
                LookaheadSeconds = _lookaheadSeconds,
                ReleaseSeconds = _releaseSeconds,
            };
        }
    }
}
