namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A noise gate stage with an adjustable threshold, attack, and release.</summary>
    [CreateAssetMenu(fileName = "DspNoiseGate", menuName = "Wallstop Studios/DxAudio/DSP/Noise Gate")]
    public sealed class DspNoiseGateAsset : DspEffectAsset
    {
        [Range(DspNoiseGate.MinimumThresholdDecibels, 0f)]
        [SerializeField]
        [Tooltip("The peak level that opens the gate, in dBFS.")]
        private float _thresholdDecibels = DspNoiseGate.DefaultThresholdDecibels;

        [Range(0f, DspNoiseGate.MaximumTimeSeconds)]
        [SerializeField]
        [Tooltip("The opening time constant in seconds. Zero opens at once.")]
        private float _attackSeconds = DspNoiseGate.DefaultAttackSeconds;

        [Range(0f, DspNoiseGate.MaximumTimeSeconds)]
        [SerializeField]
        [Tooltip("The closing time constant in seconds. Zero closes at once.")]
        private float _releaseSeconds = DspNoiseGate.DefaultReleaseSeconds;

        /// <summary>Builds a Noise Gate asset in code.</summary>
        public static DspNoiseGateAsset Create(
            float thresholdDecibels = DspNoiseGate.DefaultThresholdDecibels,
            float attackSeconds = DspNoiseGate.DefaultAttackSeconds,
            float releaseSeconds = DspNoiseGate.DefaultReleaseSeconds
        )
        {
            DspNoiseGateAsset asset = CreateInstance<DspNoiseGateAsset>();
            asset._thresholdDecibels = DspNoiseGate.ClampThreshold(thresholdDecibels);
            asset._attackSeconds = DspNoiseGate.ClampTime(attackSeconds, DspNoiseGate.DefaultAttackSeconds);
            asset._releaseSeconds = DspNoiseGate.ClampTime(releaseSeconds, DspNoiseGate.DefaultReleaseSeconds);
            return asset;
        }

        public override string Describe()
        {
            return $"{_thresholdDecibels:0.#} dBFS, attack {_attackSeconds * 1000f:0.##} ms, release {_releaseSeconds * 1000f:0.##} ms";
        }

        public override DspEffect CreateEffect()
        {
            return new DspNoiseGate
            {
                ThresholdDecibels = _thresholdDecibels,
                AttackSeconds = _attackSeconds,
                ReleaseSeconds = _releaseSeconds,
            };
        }
    }
}
