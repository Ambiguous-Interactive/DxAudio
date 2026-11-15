namespace WallstopStudios.DxAudio.Editor
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Shows a cue's takes, where each begins, and plays one from the point the game would.</summary>
    /// <remarks>
    /// The default inspector still authors everything. This adds the two questions a cue raises by ear:
    /// is any take playable, and where does playback actually start after the clip's lead-in is skipped.
    /// Audition uses a hidden, unsaved source, so it adds nothing to the open scene.
    /// </remarks>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(SfxCue))]
    public sealed class SfxCueEditor : UnityEditor.Editor
    {
        private const string PreviewName = "DxAudio Preview Source";

        private static AudioSource _previewSource;

        static SfxCueEditor()
        {
            AssemblyReloadEvents.beforeAssemblyReload += DestroyPreviewSource;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // The take preview answers a question about one cue, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return;
            }

            SfxCue cue = (SfxCue)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Authoring Check", EditorStyles.boldLabel);
            List<string> findings = DxAudioValidation.Validate(cue);
            if (findings.Count == 0)
            {
                EditorGUILayout.HelpBox("Every take is ready to play.", MessageType.Info);
            }
            else
            {
                for (int index = 0; index < findings.Count; ++index)
                {
                    EditorGUILayout.HelpBox(findings[index], MessageType.Warning);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Takes", EditorStyles.boldLabel);
            IReadOnlyList<SfxVariant> variants = cue.Variants;
            if (variants.Count == 0)
            {
                EditorGUILayout.LabelField("No takes yet. Add one above.");
                return;
            }

            for (int index = 0; index < variants.Count; ++index)
            {
                DrawTake(cue, variants[index], index);
            }
        }

        private static void DrawTake(SfxCue cue, SfxVariant variant, int index)
        {
            if (variant == null)
            {
                EditorGUILayout.LabelField($"Take {index + 1}: empty");
                return;
            }

            AudioClip clip = variant.Clip;
            if (clip == null)
            {
                EditorGUILayout.LabelField($"Take {index + 1}: no clip");
                return;
            }

            EditorGUILayout.BeginHorizontal();
            float start = cue.PlaybackStartSecondsFor(variant);
            EditorGUILayout.LabelField(
                $"Take {index + 1}: {clip.name}",
                $"starts {start:0.###}s of {clip.length:0.###}s"
            );
            if (GUILayout.Button("Audition", GUILayout.Width(80f)))
            {
                Audition(cue, variant);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void Audition(SfxCue cue, SfxVariant variant)
        {
            AudioSource source = PreviewSource();
            source.Stop();
            source.clip = variant.Clip;
            source.loop = false;
            source.volume = cue.Volume;
            // Pitch jitter belongs to a voice draw, and a loop never restarts where the preview started.
            source.pitch = 1f;
            source.time = cue.PlaybackStartSecondsFor(variant);
            source.Play();
        }

        private void OnDisable()
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
                DestroyPreviewSource();
            }
        }

        private static AudioSource PreviewSource()
        {
            if (_previewSource == null)
            {
                GameObject host = EditorUtility.CreateGameObjectWithHideFlags(
                    PreviewName,
                    HideFlags.HideAndDontSave,
                    typeof(AudioSource)
                );
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

        private static void DestroyPreviewSource()
        {
            if (_previewSource != null)
            {
                Object.DestroyImmediate(_previewSource.gameObject);
                _previewSource = null;
            }
        }
    }
}
