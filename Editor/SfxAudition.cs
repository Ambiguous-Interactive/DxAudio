namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>Plays one cue take from the point the game would, through a hidden, unsaved source.</summary>
    /// <remarks>
    /// Auditioning answers the cue question the fields cannot show: what the take sounds like after its lead-in is
    /// skipped. The source belongs to no scene, so previewing adds nothing to save, and it stops when the editor
    /// leaves edit mode.
    /// </remarks>
    public static class SfxAudition
    {
        private const string PreviewName = "DxAudio Preview Source";

        private static AudioSource _previewSource;

        static SfxAudition()
        {
            AssemblyReloadEvents.beforeAssemblyReload += DestroySource;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>Plays <paramref name="variant" /> at <paramref name="cue" />'s volume and returns the source it used.</summary>
        /// <returns>The preview source, or null when the take has no clip to play.</returns>
        public static AudioSource Play(SfxCue cue, SfxVariant variant)
        {
            if (cue == null || variant == null || variant.Clip == null)
            {
                return null;
            }

            AudioSource source = Source();
            source.Stop();
            source.clip = variant.Clip;
            source.loop = false;
            source.volume = cue.Volume;
            // Pitch jitter belongs to a voice draw, and a loop never restarts where the preview started.
            source.pitch = 1f;
            source.time = cue.PlaybackStartSecondsFor(variant);
            source.Play();
            return source;
        }

        /// <summary>Stops the preview source, if one exists.</summary>
        public static void Stop()
        {
            if (_previewSource != null)
            {
                _previewSource.Stop();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                DestroySource();
            }
        }

        private static AudioSource Source()
        {
            if (_previewSource == null)
            {
                GameObject host = EditorUtility.CreateGameObjectWithHideFlags(PreviewName, HideFlags.HideAndDontSave, typeof(AudioSource));
                _previewSource = host.GetComponent<AudioSource>();
                if (_previewSource == null)
                {
                    _previewSource = host.AddComponent<AudioSource>();
                }
            }

            _previewSource.playOnAwake = false;
            _previewSource.spatialBlend = 0f;
            return _previewSource;
        }

        private static void DestroySource()
        {
            if (_previewSource != null)
            {
                Object.DestroyImmediate(_previewSource.gameObject);
                _previewSource = null;
            }
        }
    }
}
