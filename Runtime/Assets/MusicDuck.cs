namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>One reason the music is held back, and how far: a dialogue, a menu, a level ending.</summary>
    /// <remarks>
    /// Taken with <see cref="MusicPlayer.Duck"/> and released with the lease that returns. Every live duck
    /// composes into the most restrictive of each setting, so systems duck and release independently.
    /// </remarks>
    [CreateAssetMenu(fileName = "MusicDuck", menuName = "DxAudio/Music Duck")]
    public sealed class MusicDuck : ScriptableObject, IMusicDuckReason
    {
        [SerializeField]
        [Tooltip(
            "One: a state that is on or off, such as a menu; asserting it again changes nothing and one release "
                + "ends it. Many: a claim several things can make at once, such as two speakers; the music "
                + "stays down until the last releases."
        )]
        private MusicDuckHolders _holders = MusicDuckHolders.One;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("Multiplier on the music's gain while held. The player's own volume setting is separate.")]
        private float _volumeScale = 0.5f;

        [Range(MusicAttenuation.OpenHighPassCutoffHz, MusicAttenuation.OpenLowPassCutoffHz)]
        [SerializeField]
        [Tooltip("Lower muffles more. 22000 leaves the low-pass doing nothing.")]
        private float _lowPassCutoffHz = MusicAttenuation.OpenLowPassCutoffHz;

        [Range(MusicAttenuation.OpenHighPassCutoffHz, MusicAttenuation.OpenLowPassCutoffHz)]
        [SerializeField]
        [Tooltip("Higher thins more. 10 leaves the high-pass doing nothing.")]
        private float _highPassCutoffHz = MusicAttenuation.OpenHighPassCutoffHz;

        [Min(0f)]
        [SerializeField]
        private float _applySeconds = 0.4f;

        [SerializeField]
        private MusicEase _applyEase = MusicEase.OutQuad;

        [Min(0f)]
        [SerializeField]
        private float _releaseSeconds = 0.8f;

        [SerializeField]
        private MusicEase _releaseEase = MusicEase.InOutSine;

        public MusicDuckHolders Holders => _holders;

        public float ApplySeconds => _applySeconds;

        public MusicEase ApplyEase => _applyEase;

        public float ReleaseSeconds => _releaseSeconds;

        public MusicEase ReleaseEase => _releaseEase;

        public MusicAttenuation Attenuation => new(_volumeScale, _lowPassCutoffHz, _highPassCutoffHz);

        /// <summary>Builds a duck in code, for tests and for games that author audio at runtime.</summary>
        public static MusicDuck Create(
            MusicDuckHolders holders,
            float volumeScale,
            float lowPassCutoffHz = MusicAttenuation.OpenLowPassCutoffHz,
            float highPassCutoffHz = MusicAttenuation.OpenHighPassCutoffHz,
            float applySeconds = 0.4f,
            float releaseSeconds = 0.8f
        )
        {
            MusicDuck duck = CreateInstance<MusicDuck>();
            duck._holders = holders;
            duck._volumeScale = volumeScale;
            duck._lowPassCutoffHz = lowPassCutoffHz;
            duck._highPassCutoffHz = highPassCutoffHz;
            duck._applySeconds = Mathf.Max(0f, applySeconds);
            duck._releaseSeconds = Mathf.Max(0f, releaseSeconds);
            return duck;
        }

        public override string ToString()
        {
            return name;
        }
    }
}
