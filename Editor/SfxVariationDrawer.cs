namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine.UIElements;

    [CustomPropertyDrawer(typeof(SfxVariation))]
    internal sealed class SfxVariationDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            VisualElement root = new() { name = "DxAudioSfxVariation" };
            root.Add(new Label("Playback Variation"));
            SerializedProperty usePitch = property.FindPropertyRelative(nameof(SfxVariation.UsePitchCents));
            SerializedProperty minPitch = property.FindPropertyRelative(nameof(SfxVariation.MinimumPitchCents));
            SerializedProperty maxPitch = property.FindPropertyRelative(nameof(SfxVariation.MaximumPitchCents));
            SerializedProperty minLevel = property.FindPropertyRelative(nameof(SfxVariation.MinimumAttenuationDecibels));
            SerializedProperty maxLevel = property.FindPropertyRelative(nameof(SfxVariation.MaximumAttenuationDecibels));

            root.Add(new PropertyField(usePitch, "Set Pitch in Cents"));
            VisualElement pitch = new();
            pitch.Add(new PropertyField(minPitch, "Minimum Pitch (cents)"));
            pitch.Add(new PropertyField(maxPitch, "Maximum Pitch (cents)"));
            pitch.Add(new HelpBox("Changing playback pitch also changes the clip duration.", HelpBoxMessageType.Info));
            root.Add(pitch);
            root.Add(new PropertyField(minLevel, "Minimum Level (dB)"));
            root.Add(new PropertyField(maxLevel, "Maximum Level (dB)"));
            HelpBox warning = new("Use ordered pitch bounds from -1,200 to +1,200 cents and level bounds from -24 to 0 dB.", HelpBoxMessageType.Warning);
            root.Add(warning);

            void Refresh()
            {
                pitch.style.display = usePitch.boolValue || usePitch.hasMultipleDifferentValues ? DisplayStyle.Flex : DisplayStyle.None;
                SfxVariation values = new(usePitch.boolValue, minPitch.floatValue, maxPitch.floatValue, minLevel.floatValue, maxLevel.floatValue);
                warning.style.display = values.HasInvalidBounds ? DisplayStyle.Flex : DisplayStyle.None;
            }

            root.Add(
                new Button(() =>
                {
                    SerializedObject asset = property.serializedObject;
                    asset.Update();
                    usePitch.boolValue = true;
                    minPitch.floatValue = -50f;
                    maxPitch.floatValue = 50f;
                    minLevel.floatValue = -1.5f;
                    maxLevel.floatValue = 0f;
                    asset.ApplyModifiedProperties();
                    Refresh();
                })
                {
                    text = "Apply Subtle Preset",
                }
            );
            root.TrackSerializedObjectValue(property.serializedObject, _ => Refresh());
            Refresh();
            return root;
        }
    }
}
