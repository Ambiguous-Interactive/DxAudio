namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
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
            DxAudioAssetDetails.AddWorkspaceButton(root, track);
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

            VisualElement problems = DxAudioInspectorUi.AddSection(container, "Problems");
            IReadOnlyList<DxAudioValidation.Finding> findings = DxAudioValidation.Find(track, clip);
            problems.Add(new DxAudioInspectorUi.ProblemList(findings, "No active problems.", index => FindFix(findings[index], container, track, clip)));
            VisualElement effects = DxAudioInspectorUi.AddSection(container, "Effects");
            VisualElement rows = new VisualElement();
            effects.Add(rows);
            DxAudioInspectorUi.AddChainRows(rows, track.Chain);
            DxAudioInspectorUi.RefreshWhilePlaying(rows, track.Chain);
            if (clip == null)
            {
                container.Add(new HelpBox("Assign a clip to audition this track.", HelpBoxMessageType.Info));
                return;
            }

            VisualElement transport = DxAudioInspectorUi.AddSection(container, "Clip Preview");
            transport.Add(new Label($"Starts at {track.StartTimeSeconds:0.###}s of {clip.length:0.###}s."));
            VisualElement controls = DxAudioInspectorUi.Row();
            controls.Add(new Button(() => SfxAudition.Play(track)) { text = "Audition Clip" });
            controls.Add(new Button(SfxAudition.Stop) { text = "Stop" });
            transport.Add(controls);

            VisualElement section = DxAudioInspectorUi.AddSection(container, "Clip Import");
            section.Add(BuildClipImport(track, clip, container));
            if (_warning != null)
            {
                section.Add(new HelpBox(_warning, HelpBoxMessageType.Warning));
            }
        }

        private Action FindFix(DxAudioValidation.Finding finding, VisualElement container, MusicTrack track, AudioClip clip)
        {
            return finding.Rule switch
            {
                DxAudioValidation.Rule.NoTrackClip => () => DxAudioInspectorUi.FocusProperty(container.parent, "_clip"),
                DxAudioValidation.Rule.ClipLoadType => MusicClipImport.CanApply(clip) ? () => Write(track, clip, container) : null,
                _ => () => DxAudioAuthoringWindow.Open(finding.Asset),
            };
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

            Button apply = new Button(() => Write(track, clip, container)) { text = $"Apply {resolved} to the clip", name = "DxAudioApplyClipImport" };
            view.Add(apply);
            return view;
        }

        private void OnDisable()
        {
            SfxAudition.DestroyPreview(target);
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
