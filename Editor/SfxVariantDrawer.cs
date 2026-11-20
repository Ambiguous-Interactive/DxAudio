namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    [CustomPropertyDrawer(typeof(SfxVariant))]
    internal sealed class SfxVariantDrawer : PropertyDrawer
    {
        private static SfxVariant Take(SfxCue cue, int index) => cue != null && index < cue.Variants.Count ? cue.Variants[index] : null;

        private static bool TryGetTakeIndex(string path, out int index)
        {
            int bracket = path.LastIndexOf('[');
            if (0 <= bracket && path.EndsWith("]", StringComparison.Ordinal) && int.TryParse(path.Substring(bracket + 1, path.Length - bracket - 2), out int parsed))
            {
                index = parsed;
                return 0 <= parsed;
            }
            index = default;
            return false;
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            SerializedProperty clip = property.FindPropertyRelative("_clip");
            SerializedProperty trim = property.FindPropertyRelative("_startTimeSeconds");
            SerializedProperty level = property.FindPropertyRelative("_attenuationDecibels");
            VisualElement row = new();
            row.Add(new PropertyField(clip, "Clip"));
            row.Add(new PropertyField(trim, "Trim (seconds)"));
            row.Add(new PropertyField(level, "Take Level (dB)"));
            SerializedObject asset = property.serializedObject;
            if (!asset.isEditingMultipleObjects && asset.targetObject is SfxCue cue && TryGetTakeIndex(property.propertyPath, out int index))
            {
                VisualElement preview = DxAudioInspectorUi.Row();
                Label start = new() { pickingMode = PickingMode.Ignore };
                preview.Add(start);
                Button audition = new(() => SfxAudition.Play(cue, Take(cue, index))) { text = "Audition", name = SfxCueEditor.AuditionName };
                preview.Add(audition);
                row.Add(preview);

                void Refresh()
                {
                    SfxVariant take = Take(cue, index);
                    AudioClip audio = take?.Clip;
                    bool playable = audio != null;
                    audition.SetEnabled(playable);
                    start.text = playable ? $"Starts at {cue.PlaybackStartSecondsFor(take):0.###} s of {audio.length:0.###} s" : "Assign a clip to audition.";
                }

                row.TrackSerializedObjectValue(asset, _ => Refresh());
                Refresh();
            }

            return row;
        }
    }
}
