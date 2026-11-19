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
        private static Object _previewOwner;

        static SfxAudition()
        {
            AssemblyReloadEvents.beforeAssemblyReload += DestroyPreview;
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

            return PlayClip(cue, variant.Clip, cue.Volume, cue.PlaybackStartSecondsFor(variant));
        }

        internal static AudioSource Play(MusicTrack track)
        {
            return track == null || track.Clip == null ? null : PlayClip(track, track.Clip, track.VolumeScale, track.StartTimeSeconds);
        }

        private static AudioSource PlayClip(Object owner, AudioClip clip, float volume, float start)
        {
            DspAuditionPanel.StopCurrent();
            AudioSource source = Source();
            _previewOwner = owner;
            source.Stop();
            source.clip = clip;
            source.loop = false;
            source.volume = volume;
            // Pitch jitter belongs to a voice draw, and a loop never restarts where the preview started.
            source.pitch = 1f;
            source.time = start;
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

        /// <summary>Destroys the preview source, so it does not outlive a test or an editor session.</summary>
        public static void DestroyPreview()
        {
            if (_previewSource != null)
            {
                Object.DestroyImmediate(_previewSource.gameObject);
                _previewSource = null;
            }

            _previewOwner = null;
        }

        internal static void DestroyPreview(Object owner)
        {
            if (owner != null && _previewOwner == owner)
            {
                DestroyPreview();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode)
            {
                DestroyPreview();
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
    }
}
