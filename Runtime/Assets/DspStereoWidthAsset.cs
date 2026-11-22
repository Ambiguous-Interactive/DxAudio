namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>A stereo width stage with adjustable side gain.</summary>
    [CreateAssetMenu(fileName = "DspStereoWidth", menuName = "Wallstop Studios/DxAudio/DSP/Stereo Width")]
    public sealed class DspStereoWidthAsset : DspEffectAsset
    {
        [Range(DspStereoWidth.MinimumWidth, DspStereoWidth.MaximumWidth)]
        [SerializeField]
        [Tooltip("Zero folds stereo to mono. One keeps the original width. Two doubles the channel difference.")]
        private float _width = DspStereoWidth.DefaultWidth;

        /// <summary>Builds a Stereo Width asset in code.</summary>
        public static DspStereoWidthAsset Create(float width = DspStereoWidth.DefaultWidth)
        {
            DspStereoWidthAsset asset = CreateInstance<DspStereoWidthAsset>();
            asset._width = DspStereoWidth.ClampWidth(width);
            return asset;
        }

        public override string Describe()
        {
            return $"Width {DspStereoWidth.ClampWidth(_width):P0}";
        }

        public override DspEffect CreateEffect()
        {
            return new DspStereoWidth { Width = _width };
        }
    }
}
