namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A distortion stage: pre-gain into the wave shaper, and the tone control after it.</summary>
    [CreateAssetMenu(fileName = "DspDistortion", menuName = "DxAudio/DSP/Distortion")]
    public sealed class DspDistortionAsset : DspEffectAsset
    {
        public float Drive => _drive;

        public float ToneHz => _toneHz;

        [Range(0.01f, 100f)]
        [SerializeField]
        [Tooltip("Pre-gain into the shape. One is its own curve; higher bends more of the signal.")]
        private float _drive = 1f;

        [Min(20f)]
        [SerializeField]
        [Tooltip("The corner of the tone control, in hertz. Lower is darker; twenty kilohertz passes the shaper's top.")]
        private float _toneHz = DspDistortion.OpenToneHz;

        /// <summary>Builds a distortion asset in code, for tests and for games that author audio at runtime.</summary>
        public static DspDistortionAsset Create(float drive = 1f, float toneHz = DspDistortion.OpenToneHz)
        {
            DspDistortionAsset asset = CreateInstance<DspDistortionAsset>();
            asset._drive = Mathf.Clamp(drive, 0.01f, 100f);
            asset._toneHz = Mathf.Max(20f, toneHz);
            return asset;
        }

        public override string Describe()
        {
            return $"drive {_drive:0.##}, tone {_toneHz:0.#} Hz";
        }

        public override DspEffect CreateEffect()
        {
            return new DspDistortion { Drive = _drive, ToneHz = _toneHz };
        }
    }
}
