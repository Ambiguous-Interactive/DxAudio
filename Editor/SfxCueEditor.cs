namespace WallstopStudios.DxAudio.Editor
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>Shows a cue's takes, where each begins, and plays one from the point the game would.</summary>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(SfxCue))]
    public sealed class SfxCueEditor : UnityEditor.Editor
    {
        internal const string AuditionName = "DxAudioAudition";

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

        internal static bool AppendTakes(SerializedObject asset, Object[] items)
        {
            asset.Update();
            SerializedProperty takes = asset.FindProperty("_variants");
            int count = takes.arraySize;
            foreach (Object item in items)
            {
                if (item is AudioClip clip && clip != null)
                {
                    int index = takes.arraySize;
                    takes.InsertArrayElementAtIndex(index);
                    SerializedProperty take = takes.GetArrayElementAtIndex(index);
                    take.FindPropertyRelative("_clip").objectReferenceValue = clip;
                    take.FindPropertyRelative("_startTimeSeconds").floatValue = 0f;
                    take.FindPropertyRelative("_attenuationDecibels").floatValue = 0f;
                }
            }

            if (count == takes.arraySize)
            {
                return false;
            }

            Undo.SetCurrentGroupName("Add Cue Takes");
            takes.isExpanded = true;
            return asset.ApplyModifiedProperties();
        }

        private static void RebuildAuthoring(VisualElement container, SfxCue cue)
        {
            container.Clear();
            VisualElement check = DxAudioInspectorUi.AddSection(container, "Authoring Check");
            DxAudioInspectorUi.AddFindings(check, DxAudioValidation.Validate(cue), "Every take is ready to play.");
            VisualElement effects = DxAudioInspectorUi.AddSection(container, "Effects");
            VisualElement rows = new VisualElement();
            effects.Add(rows);
            DxAudioInspectorUi.AddChainRows(rows, cue.Chain);
            DxAudioInspectorUi.RefreshWhilePlaying(rows, cue.Chain);
        }

        private static VisualElement BuildClipDrop(SerializedObject asset)
        {
            HelpBox drop = new("Drop AudioClips here to add takes.", HelpBoxMessageType.None) { name = "DxAudioTakeDrop" };
            drop.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                foreach (Object item in DragAndDrop.objectReferences)
                {
                    if (item is AudioClip clip && clip != null)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.StopPropagation();
                        return;
                    }
                }
            });
            drop.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (AppendTakes(asset, DragAndDrop.objectReferences))
                {
                    DragAndDrop.AcceptDrag();
                    evt.StopPropagation();
                }
            });
            return drop;
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

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            VisualElement settings = new();
            root.Add(settings);

            SerializedProperty script = serializedObject.FindProperty("m_Script");
            PropertyField scriptField = new(script);
            scriptField.SetEnabled(false);
            settings.Add(scriptField);

            VisualElement transport = new();
            settings.Add(transport);
            SerializedProperty takes = serializedObject.FindProperty("_variants");
            if (targets.Length == 1)
            {
                settings.Add(BuildClipDrop(serializedObject));
            }
            settings.Add(new PropertyField(takes, "Takes"));
            settings.Add(new PropertyField(serializedObject.FindProperty("_variation")));

            SerializedProperty field = serializedObject.GetIterator();
            bool enterChildren = true;
            while (field.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (field.name is not ("m_Script" or "_variants" or "_pitchJitter" or "_variation"))
                {
                    settings.Add(new PropertyField(field.Copy()));
                }
            }

            // The take preview answers a question about one cue, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return root;
            }

            SfxCue cue = (SfxCue)target;
            DxAudioAssetDetails.AddWorkspaceButton(root, cue);
            transport.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                if (evt.target == transport && transport.childCount == 0 && transport.GetFirstAncestorOfType<DxAudioAssetDetails>() == null)
                {
                    DspAuditionPanel audition = new(DxAudioAuthoringWindow.Open, null);
                    audition.SetTarget(cue);
                    transport.Add(audition);
                }
            });
            VisualElement authoring = new VisualElement();
            root.Add(authoring);
            RebuildAuthoring(authoring, cue);
            // IMGUI redrew this section on every repaint; a serialized change rebuilds it the same way.
            authoring.TrackSerializedObjectValue(serializedObject, _ => RebuildAuthoring(authoring, cue));
            return root;
        }

        private void OnDisable()
        {
            SfxAudition.DestroyPreview(target);
        }
    }
}
