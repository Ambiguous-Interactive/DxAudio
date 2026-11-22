namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A delay stage with adjustable spacing, feedback, and echo level.</summary>
    [CreateAssetMenu(fileName = "DspDelay", menuName = "Wallstop Studios/DxAudio/DSP/Delay")]
    public sealed class DspDelayAsset : DspEffectAsset
    {
        [Range(DspDelay.MinimumDelaySeconds, DspDelay.MaximumDelaySeconds)]
        [SerializeField]
        [Tooltip("The time between repeats in seconds.")]
        private float _delaySeconds = DspDelay.DefaultDelaySeconds;

        [Range(0f, DspDelay.MaximumFeedback)]
        [SerializeField]
        [Tooltip("The fraction of each repeat fed back into the delay. Zero gives one repeat.")]
        private float _feedback = DspDelay.DefaultFeedback;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("The level of the first repeat. The dry signal stays at full level.")]
        private float _echoLevel = DspDelay.DefaultEchoLevel;

        /// <summary>Builds a Delay asset in code.</summary>
        public static DspDelayAsset Create(
            float delaySeconds = DspDelay.DefaultDelaySeconds,
            float feedback = DspDelay.DefaultFeedback,
            float echoLevel = DspDelay.DefaultEchoLevel
        )
        {
            DspDelayAsset asset = CreateInstance<DspDelayAsset>();
            asset._delaySeconds = AudioMath.ClampFinite(delaySeconds, DspDelay.MinimumDelaySeconds, DspDelay.MaximumDelaySeconds, DspDelay.DefaultDelaySeconds);
            asset._feedback = AudioMath.ClampFinite(feedback, 0f, DspDelay.MaximumFeedback, DspDelay.DefaultFeedback);
            asset._echoLevel = AudioMath.ClampFinite(echoLevel, 0f, 1f, DspDelay.DefaultEchoLevel);
            return asset;
        }

        public override string Describe()
        {
            return $"{_delaySeconds * 1000f:0.##} ms, feedback {_feedback:P0}, echo {_echoLevel:P0}";
        }

        public override DspEffect CreateEffect()
        {
            return new DspDelay
            {
                DelaySeconds = _delaySeconds,
                Feedback = _feedback,
                EchoLevel = _echoLevel,
            };
        }
    }
}
