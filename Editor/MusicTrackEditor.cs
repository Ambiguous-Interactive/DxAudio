namespace WallstopStudios.DxAudio.Editor
{
    using System;
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
            container.Clear();
            if (track.Clip == null)
            {
                return container;
            }

            VisualElement section = DxAudioInspectorUi.AddSection(container, "Clip Import");
            section.Add(
                BuildClipImport(
                    track,
                    message =>
                    {
                        // Rebuild the whole section, so a second blocked click replaces its warning instead of stacking.
                        RebuildClipImport(container, track);
                        if (message != null)
                        {
                            container.Add(new HelpBox(message, HelpBoxMessageType.Warning));
                        }
                    }
                )
            );
            return container;
        }

        private static VisualElement BuildClipImport(MusicTrack track, Action<string> report)
        {
            VisualElement view = new VisualElement();
            AudioClip clip = track.Clip;
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

            Button apply = new Button(() => Apply(track, report)) { text = $"Apply {resolved} to the clip" };
            view.Add(apply);
            return view;
        }

        private static void Apply(MusicTrack track, Action<string> report)
        {
            MusicClipLoadType resolved = track.ResolvedClipLoadType;
            bool applied = MusicClipImport.TryApply(track.Clip, resolved);
            string message = applied ? null : "The clip's type did not change; a per-platform override or a read-only file may block it.";
            report(message);
        }
    }
}
