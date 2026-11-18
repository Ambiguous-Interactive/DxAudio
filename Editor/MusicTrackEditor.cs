namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Shows the load type a clip's import settings hold beside the one a track asks for.</summary>
    /// <remarks>
    /// The default fields still author every value. The clip holds one load type, so this shows the resolved choice
    /// next to the imported one and offers the write that makes them agree. A blocked write keeps its explanation
    /// across the rebuild that its own reimport triggers.
    /// </remarks>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(MusicTrack))]
    public sealed class MusicTrackEditor : UnityEditor.Editor
    {
        private const string StatusName = "DxAudioClipImportStatus";
        private const string BlockedMessage = "The clip's type did not change; a per-platform override or a read-only file may block it.";

        private string _warning;

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
            Rebuild(import, track);
            // IMGUI redrew this section on every repaint; a serialized change rebuilds it the same way.
            import.TrackSerializedObjectValue(serializedObject, _ => Rebuild(import, track));
            return root;
        }

        private void Rebuild(VisualElement container, MusicTrack track)
        {
            Rebuild(container, track, track == null ? null : track.Clip);
        }

        private void Rebuild(VisualElement container, MusicTrack track, AudioClip clip)
        {
            container.Clear();
            if (track == null)
            {
                return;
            }

            VisualElement effects = DxAudioInspectorUi.AddSection(container, "Effects");
            DxAudioInspectorUi.AddChainRows(effects, track.Chain);
            if (clip == null)
            {
                return;
            }

            VisualElement section = DxAudioInspectorUi.AddSection(container, "Clip Import");
            section.Add(BuildClipImport(track, clip, container));
            if (_warning != null)
            {
                section.Add(new HelpBox(_warning, HelpBoxMessageType.Warning));
            }
        }

        private VisualElement BuildClipImport(MusicTrack track, AudioClip clip, VisualElement container)
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
                // Settling the mismatch any other way also settles the blocked write that reported it.
                _warning = null;
                view.Add(new Label($"{clip.length:0.#}s clip, imported as {applied}.") { name = StatusName });
                return view;
            }

            view.Add(new Label($"{clip.length:0.#}s clip, imported as {applied}; the track asks for {resolved}.") { name = StatusName });
            if (!MusicClipImport.CanApply(clip))
            {
                view.Add(new HelpBox("The clip has no editable importer, so its import type cannot be applied.", HelpBoxMessageType.Warning));
                return view;
            }

            Button apply = new Button(() => Write(track, clip, container)) { text = $"Apply {resolved} to the clip" };
            view.Add(apply);
            return view;
        }

        private void Write(MusicTrack track, AudioClip clip, VisualElement container)
        {
            // A reimport can replace the loaded clip, so rebuild from the clip the asset database loads by path.
            string path = AssetDatabase.GetAssetPath(clip);
            MusicClipLoadType resolved = track.ResolvedClipLoadType;
            bool applied = MusicClipImport.TryApply(clip, resolved);
            AudioClip fresh = string.IsNullOrEmpty(path) ? clip : AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            _warning = applied ? null : BlockedMessage;
            Rebuild(container, track, fresh);
        }
    }
}
