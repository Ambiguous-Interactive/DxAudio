namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>Shows the load type a track asks for next to the clip's own, and applies it on request.</summary>
    /// <remarks>
    /// The default inspector still authors every field. The clip's import settings decide how much memory a track
    /// holds, so this shows the resolved choice and offers the one edit that writes it into the clip.
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

            MusicClipLoadType resolved = track.ResolvedClipLoadType;
            MusicClipLoadType applied = MusicClipImport.AppliedLoadType(clip);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clip Load Type", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"{clip.length:0.#}s clip",
                resolved == applied ? applied.ToString() : $"imported as {applied}; the track asks for {resolved}"
            );

            if (resolved == applied)
            {
                return;
            }

            if (GUILayout.Button($"Apply {resolved} to the clip"))
            {
                MusicClipImport.TryApply(clip, resolved);
            }
        }
    }
}
