namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A pitch shifter stage: how far it moves the pitch, and how long the read head sweeps.</summary>
    [CreateAssetMenu(fileName = "DspPitchShifter", menuName = "DxAudio/DSP/Pitch Shifter")]
    public sealed class DspPitchShifterAsset : DspEffectAsset
    {
        [Range(DspPitchShifter.MinimumSemitones, DspPitchShifter.MaximumSemitones)]
        [SerializeField]
        [Tooltip("How far the pitch moves, in semitones. Zero delays the signal by half the window and changes nothing else.")]
        private float _semitones;

        [Range(DspPitchShifter.MinimumWindowSeconds, DspPitchShifter.MaximumWindowSeconds)]
        [SerializeField]
        [Tooltip("How long each read head sweeps. The delay is half this window, so a short window lowers the latency and adds warble.")]
        private float _windowSeconds = DspPitchShifter.DefaultWindowSeconds;

        /// <summary>How far this stage moves the pitch, in semitones.</summary>
        public float Semitones => _semitones;

        public float WindowSeconds => _windowSeconds;

        /// <summary>Builds a pitch shifter asset in code, for tests and for games that author audio at runtime.</summary>
        public static DspPitchShifterAsset Create(float semitones = 0f, float windowSeconds = DspPitchShifter.DefaultWindowSeconds)
        {
            DspPitchShifterAsset asset = CreateInstance<DspPitchShifterAsset>();
            asset._semitones = Mathf.Clamp(semitones, DspPitchShifter.MinimumSemitones, DspPitchShifter.MaximumSemitones);
            asset._windowSeconds = Mathf.Clamp(windowSeconds, DspPitchShifter.MinimumWindowSeconds, DspPitchShifter.MaximumWindowSeconds);
            return asset;
        }

        public override string Describe()
        {
            return $"pitch {_semitones:0.##} st, window {_windowSeconds * 1000f:0.#} ms";
        }

        public override DspEffect CreateEffect()
        {
            return new DspPitchShifter { Semitones = _semitones, WindowSeconds = _windowSeconds };
        }
    }
}
