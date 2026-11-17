namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Shows the load type a clip's import settings hold beside the one a track asks for.</summary>
    /// <remarks>
    /// The default fields still author every value. The clip holds one load type, so this shows the resolved choice
    /// next to the imported one and offers the write that makes them agree.
    /// </remarks>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(MusicTrack))]
    public sealed class MusicTrackEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            // The load type is one clip's setting, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return root;
            }

            MusicTrack track = (MusicTrack)target;
            if (track.Clip == null)
            {
                return root;
            }

            DxAudioInspectorUi.AddSection(root, "Clip Import").Add(BuildClipImport(track));
            return root;
        }

        /// <summary>Shows the clip's imported load type beside the track's and offers the write that agrees them.</summary>
        public static VisualElement BuildClipImport(MusicTrack track)
        {
            VisualElement view = new VisualElement();
            if (track == null || track.Clip == null)
            {
                view.Add(new HelpBox("The track has no clip, so there is no import type to check.", HelpBoxMessageType.None));
                return view;
            }

            AudioClip clip = track.Clip;
            if (!MusicClipImport.TryGetAppliedLoadType(clip, out MusicClipLoadType applied))
            {
                view.Add(new HelpBox("The clip's import type cannot be read.", HelpBoxMessageType.Warning));
                return view;
            }

            MusicClipLoadType resolved = track.ResolvedClipLoadType;
            if (applied == resolved)
            {
                view.Add(new Label($"{clip.length:0.#}s clip, imported as {applied}."));
                return view;
            }

            view.Add(new Label($"{clip.length:0.#}s clip, imported as {applied}; the track asks for {resolved}."));
            if (!MusicClipImport.CanApply(clip))
            {
                view.Add(new HelpBox("The clip has no editable importer, so its import type cannot be applied.", HelpBoxMessageType.Warning));
                return view;
            }

            Button apply = new Button(() => Apply(track, view)) { text = $"Apply {resolved} to the clip" };
            view.Add(apply);
            return view;
        }

        private static void Apply(MusicTrack track, VisualElement view)
        {
            AudioClip clip = track.Clip;
            MusicClipLoadType resolved = track.ResolvedClipLoadType;
            if (!MusicClipImport.TryApply(clip, resolved))
            {
                view.Add(new HelpBox("The clip's type did not change; a per-platform override or a read-only file may block it.", HelpBoxMessageType.Warning));
                return;
            }

            float length = clip.length;
            view.Clear();
            view.Add(new Label($"{length:0.#}s clip, imported as {resolved}."));
        }
    }
}
