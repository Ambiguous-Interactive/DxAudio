namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>Shows the load type a clip's import settings hold beside the one a track asks for.</summary>
    /// <remarks>
    /// The default inspector still authors every field. The clip holds one load type, so this shows the resolved
    /// choice next to the imported one and offers the write that makes them agree.
    /// </remarks>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(MusicTrack))]
    public sealed class MusicTrackEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // The load type is one clip's setting, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return;
            }

            MusicTrack track = (MusicTrack)target;
            AudioClip clip = track.Clip;
            if (clip == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clip Import", EditorStyles.boldLabel);
            if (!MusicClipImport.TryGetAppliedLoadType(clip, out MusicClipLoadType applied))
            {
                EditorGUILayout.HelpBox("The clip's import type cannot be read.", MessageType.Warning);
                return;
            }

            MusicClipLoadType resolved = track.ResolvedClipLoadType;
            EditorGUILayout.LabelField($"{clip.length:0.#}s clip", applied == resolved ? applied.ToString() : $"imported as {applied}; the track asks for {resolved}");

            if (applied == resolved)
            {
                return;
            }

            if (!MusicClipImport.CanApply(clip))
            {
                EditorGUILayout.HelpBox("The clip has no editable importer, so its import type cannot be applied.", MessageType.Warning);
                return;
            }

            if (GUILayout.Button($"Apply {resolved} to the clip") && !MusicClipImport.TryApply(clip, resolved))
            {
                EditorGUILayout.HelpBox("The clip's type did not change; a per-platform override or a read-only file may block it.", MessageType.Warning);
            }
        }
    }
}
