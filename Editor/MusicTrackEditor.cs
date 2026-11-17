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
        private const string StatusName = "DxAudioClipImportStatus";
        private const string BlockedMessage = "The clip's type did not change; a per-platform override or a read-only file may block it.";

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
            VisualElement import = new VisualElement();
            root.Add(import);
            RebuildClipImport(import, track);
            // IMGUI redrew this section on every repaint; a serialized change rebuilds it the same way.
            import.TrackSerializedObjectValue(serializedObject, _ => RebuildClipImport(import, track));
            return root;
        }

        /// <summary>Rebuilds the clip import section under <paramref name="container" />.</summary>
        public static VisualElement RebuildClipImport(VisualElement container, MusicTrack track)
        {
            AudioClip clip = track == null ? null : track.Clip;
            return RebuildClipImport(container, track, clip, null);
        }

        private static VisualElement RebuildClipImport(VisualElement container, MusicTrack track, AudioClip clip, string warning)
        {
            container.Clear();
            if (track == null || clip == null)
            {
                return container;
            }

            VisualElement section = DxAudioInspectorUi.AddSection(container, "Clip Import");
            section.Add(BuildClipImport(track, clip, container));
            if (warning != null)
            {
                section.Add(new HelpBox(warning, HelpBoxMessageType.Warning));
            }

            return container;
        }

        private static VisualElement BuildClipImport(MusicTrack track, AudioClip clip, VisualElement container)
        {
            VisualElement view = new VisualElement();
            if (!MusicClipImport.TryGetAppliedLoadType(clip, out MusicClipLoadType applied))
            {
                view.Add(new HelpBox("The clip's import type cannot be read.", HelpBoxMessageType.Warning));
                return view;
            }

            MusicClipLoadType resolved = track.ResolvedClipLoadType;
            if (applied == resolved)
            {
                view.Add(new Label($"{clip.length:0.#}s clip, imported as {applied}.") { name = StatusName });
                return view;
            }

            view.Add(new Label($"{clip.length:0.#}s clip, imported as {applied}; the track asks for {resolved}.") { name = StatusName });
            if (!MusicClipImport.CanApply(clip))
            {
                view.Add(new HelpBox("The clip has no editable importer, so its import type cannot be applied.", HelpBoxMessageType.Warning));
                return view;
            }

            Button apply = new Button(() => Apply(track, clip, container)) { text = $"Apply {resolved} to the clip" };
            view.Add(apply);
            return view;
        }

        private static void Apply(MusicTrack track, AudioClip clip, VisualElement container)
        {
            // A reimport can replace the loaded clip, so rebuild from the clip the asset database loads by path.
            string path = AssetDatabase.GetAssetPath(clip);
            MusicClipLoadType resolved = track.ResolvedClipLoadType;
            bool applied = MusicClipImport.TryApply(clip, resolved);
            AudioClip fresh = string.IsNullOrEmpty(path) ? clip : AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            RebuildClipImport(container, track, fresh, applied ? null : BlockedMessage);
        }
    }
}
