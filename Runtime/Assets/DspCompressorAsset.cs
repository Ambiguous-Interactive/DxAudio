namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A compressor stage: a threshold, a ratio, and the times its gain follows the signal.</summary>
    [CreateAssetMenu(fileName = "DspCompressor", menuName = "DxAudio/DSP/Compressor")]
    public sealed class DspCompressorAsset : DspEffectAsset
    {
        [Range(-60f, 0f)]
        [SerializeField]
        [Tooltip("The level where reduction starts, in dBFS. A cue sitting under it passes untouched.")]
        private float _thresholdDecibels = DspCompressor.DefaultThresholdDecibels;

        [Range(1f, 100f)]
        [SerializeField]
        [Tooltip("Decibels over the threshold for each decibel that leaves. Four is a gentle squeeze.")]
        private float _ratio = DspCompressor.DefaultRatio;

        [Min(0f)]
        [SerializeField]
        [Tooltip("How quickly the gain follows a louder signal. Set before audio starts: a new value is not ramped.")]
        private float _attackSeconds = 0.005f;

        [Min(0f)]
        [SerializeField]
        [Tooltip("How quickly the gain recovers after a quieter signal. Set before audio starts: a new value is not ramped.")]
        private float _releaseSeconds = 0.1f;

        [Range(-60f, 24f)]
        [SerializeField]
        [Tooltip("Gain added after the reduction. A lift can pass full scale, so put a limiter last if a ceiling is needed.")]
        private float _makeupDecibels;

        public float ThresholdDecibels => _thresholdDecibels;

        public float Ratio => _ratio;

        public float AttackSeconds => _attackSeconds;

        public float ReleaseSeconds => _releaseSeconds;

        public float MakeupDecibels => _makeupDecibels;

        /// <summary>Builds a compressor asset in code, for tests and for games that author audio at runtime.</summary>
        public static DspCompressorAsset Create(
            float thresholdDecibels = DspCompressor.DefaultThresholdDecibels,
            float ratio = DspCompressor.DefaultRatio,
            float attackSeconds = 0.005f,
            float releaseSeconds = 0.1f,
            float makeupDecibels = 0f
        )
        {
            DspCompressorAsset asset = CreateInstance<DspCompressorAsset>();
            asset._thresholdDecibels = Mathf.Clamp(thresholdDecibels, -60f, 0f);
            asset._ratio = Mathf.Clamp(ratio, 1f, 100f);
            asset._attackSeconds = Mathf.Max(0f, attackSeconds);
            asset._releaseSeconds = Mathf.Max(0f, releaseSeconds);
            asset._makeupDecibels = Mathf.Clamp(makeupDecibels, -60f, 24f);
            return asset;
        }

        public override string Describe()
        {
            return $"{_thresholdDecibels:0.#} dB at {_ratio:0.#}:1, attack {_attackSeconds * 1000f:0.#} ms, release {_releaseSeconds * 1000f:0.#} ms, makeup {_makeupDecibels:0.#} dB";
        }

        public override DspEffect CreateEffect()
        {
            return new DspCompressor
            {
                ThresholdDecibels = _thresholdDecibels,
                Ratio = _ratio,
                AttackSeconds = _attackSeconds,
                ReleaseSeconds = _releaseSeconds,
                MakeupDecibels = _makeupDecibels,
            };
        }
    }
}
