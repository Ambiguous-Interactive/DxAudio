namespace WallstopStudios.DxAudio.Editor
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Shows a cue's takes, where each begins, and plays one from the point the game would.</summary>
    /// <remarks>
    /// The default fields still author everything. This adds the two questions a cue raises by ear: is any take
    /// playable, and where does playback actually start after the clip's lead-in is skipped. Audition uses a hidden,
    /// unsaved source, so it adds nothing to the open scene.
    /// </remarks>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(SfxCue))]
    public sealed class SfxCueEditor : UnityEditor.Editor
    {
        private const string AuditionName = "DxAudioAudition";

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            // The take preview answers a question about one cue, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return root;
            }

            SfxCue cue = (SfxCue)target;
            VisualElement authoring = new VisualElement();
            root.Add(authoring);
            RebuildAuthoring(authoring, cue);
            // IMGUI redrew this section on every repaint; a serialized change rebuilds it the same way.
            authoring.TrackSerializedObjectValue(serializedObject, _ => RebuildAuthoring(authoring, cue));
            return root;
        }

        private static void RebuildAuthoring(VisualElement container, SfxCue cue)
        {
            container.Clear();
            VisualElement check = DxAudioInspectorUi.AddSection(container, "Authoring Check");
            DxAudioInspectorUi.AddFindings(check, DxAudioValidation.Validate(cue), "Every take is ready to play.");
            DxAudioInspectorUi.AddSection(container, "Takes").Add(BuildTakes(cue));
        }

        /// <summary>The take list the cue inspector shows.</summary>
        public static VisualElement BuildTakes(SfxCue cue)
        {
            VisualElement list = new VisualElement();
            if (cue == null)
            {
                list.Add(new Label("There is no cue to show."));
                return list;
            }

            IReadOnlyList<SfxVariant> variants = cue.Variants;
            if (variants.Count == 0)
            {
                list.Add(new Label("No takes yet. Add one above."));
                return list;
            }

            for (int index = 0; index < variants.Count; ++index)
            {
                list.Add(BuildTake(cue, variants[index], index));
            }

            return list;
        }

        private static VisualElement BuildTake(SfxCue cue, SfxVariant variant, int index)
        {
            if (variant == null)
            {
                VisualElement empty = DxAudioInspectorUi.Row();
                DxAudioInspectorUi.AddText(empty, $"Take {index + 1}: empty");
                return empty;
            }

            AudioClip clip = variant.Clip;
            if (clip == null)
            {
                VisualElement silent = DxAudioInspectorUi.Row();
                DxAudioInspectorUi.AddText(silent, $"Take {index + 1}: no clip");
                return silent;
            }

            VisualElement row = DxAudioInspectorUi.Row();
            float start = cue.PlaybackStartSecondsFor(variant);
            DxAudioInspectorUi.AddText(row, $"Take {index + 1}: {clip.name}, starts {start:0.###}s of {clip.length:0.###}s");

            Button audition = new Button(() => SfxAudition.Play(cue, variant)) { text = "Audition", name = AuditionName };
            audition.style.width = 80f;
            row.Add(audition);
            return row;
        }

        private void OnDisable()
        {
            // One source serves every cue inspector, so closing any of them stops the preview.
            SfxAudition.Stop();
        }
    }
}
