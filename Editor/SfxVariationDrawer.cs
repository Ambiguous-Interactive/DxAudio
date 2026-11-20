namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine.UIElements;

    [CustomPropertyDrawer(typeof(SfxVariation))]
    internal sealed class SfxVariationDrawer : PropertyDrawer
    {
        internal static void SetLowPass(SerializedObject asset, string variationPath, bool enabled)
        {
            const string UndoName = "Change Low-Pass Variation";
            Undo.SetCurrentGroupName(UndoName);
            UnityEngine.Object[] targets = asset.targetObjects;
            Undo.RegisterCompleteObjectUndo(targets, UndoName);
            foreach (UnityEngine.Object target in targets)
            {
                using SerializedObject edit = new(target);
                SerializedProperty variation = edit.FindProperty(variationPath);
                variation.FindPropertyRelative(nameof(SfxVariation.UseLowPass)).boolValue = enabled;
                SerializedProperty lower = variation.FindPropertyRelative(nameof(SfxVariation.MinimumLowPassHertz));
                SerializedProperty upper = variation.FindPropertyRelative(nameof(SfxVariation.MaximumLowPassHertz));
                if (enabled && lower.floatValue == 0f && upper.floatValue == 0f)
                {
                    lower.floatValue = 8_000f;
                    upper.floatValue = 16_000f;
                }
                edit.ApplyModifiedPropertiesWithoutUndo();
            }
            asset.Update();
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            VisualElement root = new() { name = "DxAudioSfxVariation" };
            root.Add(new Label("Playback Variation"));
            SerializedObject asset = property.serializedObject;
            SerializedProperty legacyJitter = asset.FindProperty("_pitchJitter");
            SerializedProperty usePitch = property.FindPropertyRelative(nameof(SfxVariation.UsePitchCents));
            SerializedProperty minPitch = property.FindPropertyRelative(nameof(SfxVariation.MinimumPitchCents));
            SerializedProperty maxPitch = property.FindPropertyRelative(nameof(SfxVariation.MaximumPitchCents));
            SerializedProperty minLevel = property.FindPropertyRelative(nameof(SfxVariation.MinimumAttenuationDecibels));
            SerializedProperty maxLevel = property.FindPropertyRelative(nameof(SfxVariation.MaximumAttenuationDecibels));
            SerializedProperty useShuffle = property.FindPropertyRelative(nameof(SfxVariation.UseShuffle));
            SerializedProperty useLowPass = property.FindPropertyRelative(nameof(SfxVariation.UseLowPass));
            SerializedProperty minCutoff = property.FindPropertyRelative(nameof(SfxVariation.MinimumLowPassHertz));
            SerializedProperty maxCutoff = property.FindPropertyRelative(nameof(SfxVariation.MaximumLowPassHertz));

            Toggle shuffle = new("Shuffle Takes") { tooltip = "Play each available take once per cycle." };
            shuffle.bindingPath = useShuffle.propertyPath;
            root.Add(shuffle);
            VisualElement presets = DxAudioInspectorUi.Row();
            presets.style.flexWrap = Wrap.Wrap;
            root.Add(presets);

            root.Add(new PropertyField(usePitch, "Set Pitch in Cents"));
            VisualElement legacy = new();
            Label effective = new() { pickingMode = PickingMode.Ignore };
            if (legacyJitter != null)
            {
                legacy.Add(new PropertyField(legacyJitter, "Legacy Pitch Jitter"));
                legacy.Add(effective);
                root.Add(legacy);
            }

            VisualElement pitch = new();
            pitch.Add(new PropertyField(minPitch, "Minimum Pitch (cents)"));
            pitch.Add(new PropertyField(maxPitch, "Maximum Pitch (cents)"));
            root.Add(pitch);
            root.Add(new HelpBox("Changing pitch also changes playback duration.", HelpBoxMessageType.Info));
            root.Add(new PropertyField(minLevel, "Minimum Random Level (dB)"));
            root.Add(new PropertyField(maxLevel, "Maximum Random Level (dB)"));

            Toggle lowPass = new("Vary Low-Pass Cutoff") { tooltip = "Draw a new cutoff when a voice starts. Lower values soften bright sounds." };
            root.Add(lowPass);
            VisualElement cutoff = new();
            cutoff.Add(new PropertyField(minCutoff, "Minimum Cutoff (Hz)"));
            cutoff.Add(new PropertyField(maxCutoff, "Maximum Cutoff (Hz)"));
            root.Add(cutoff);
            HelpBox warning = new("Check the range limits. Pitch allows -1,200 to 1,200 cents, level -24 to 0 dB, and cutoff 500 to 20,000 Hz.", HelpBoxMessageType.Warning);
            root.Add(warning);
            presets.Add(new Button(() => ApplyPreset(0f, 0f, "Pitch Stable")) { text = "Pitch Stable" });
            presets.Add(new Button(() => ApplyPreset(50f, -1.5f, "Subtle")) { text = "Subtle" });

            lowPass.RegisterValueChangedCallback(change =>
            {
                SetLowPass(asset, property.propertyPath, change.newValue);
                Refresh();
            });

            root.TrackSerializedObjectValue(asset, _ => Refresh());
            Refresh();
            return root;

            void ApplyPreset(float cents, float decibels, string name)
            {
                asset.Update();
                Undo.SetCurrentGroupName(name);
                usePitch.boolValue = true;
                minPitch.floatValue = -cents;
                maxPitch.floatValue = cents;
                minLevel.floatValue = decibels;
                maxLevel.floatValue = 0f;
                asset.ApplyModifiedProperties();
                Refresh();
            }

            void Refresh()
            {
                bool mixedPitch = usePitch.hasMultipleDifferentValues;
                pitch.style.display = usePitch.boolValue || mixedPitch ? DisplayStyle.Flex : DisplayStyle.None;
                legacy.style.display = !usePitch.boolValue || mixedPitch ? DisplayStyle.Flex : DisplayStyle.None;
                if (legacyJitter != null)
                {
                    float jitter = legacyJitter.floatValue;
                    effective.text = legacyJitter.hasMultipleDifferentValues
                        ? "Pitch range varies by cue."
                        : $"Effective pitch {1200d * Math.Log(1f - jitter, 2d):0} to {1200d * Math.Log(1f + jitter, 2d):0} cents";
                }
                lowPass.showMixedValue = useLowPass.hasMultipleDifferentValues;
                lowPass.SetValueWithoutNotify(!lowPass.showMixedValue && useLowPass.boolValue);
                cutoff.style.display = useLowPass.boolValue || useLowPass.hasMultipleDifferentValues ? DisplayStyle.Flex : DisplayStyle.None;
                SfxVariation values = new(usePitch.boolValue, minPitch.floatValue, maxPitch.floatValue, minLevel.floatValue, maxLevel.floatValue)
                {
                    UseLowPass = useLowPass.boolValue,
                    MinimumLowPassHertz = minCutoff.floatValue,
                    MaximumLowPassHertz = maxCutoff.floatValue,
                };
                warning.style.display = values.HasInvalidBounds ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
