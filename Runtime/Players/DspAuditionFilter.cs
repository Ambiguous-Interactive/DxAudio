#if UNITY_EDITOR
namespace WallstopStudios.DxAudio
{
    using System;
    using UnityEngine;

    /// <summary>Receives native Unity audio blocks for the editor's temporary DSP audition source.</summary>
    [ExecuteAlways]
    [AddComponentMenu("")]
    internal sealed class DspAuditionFilter : MonoBehaviour
    {
        private volatile Action<float[], int> _process;

        internal void SetProcessor(Action<float[], int> process)
        {
            _process = process;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            Action<float[], int> process = _process;
            if (process == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            process(data, channels);
        }
    }
}
#endif
