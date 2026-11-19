namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine.UIElements;

    /// <summary>Shows the mix bus's chain, and says when the object Unity hands the mix to is not this one.</summary>
    /// <remarks>
    /// Unity calls a filter on the object that carries the AudioListener with the sum of every source. A mix bus
    /// anywhere else receives no block at all, which is silent by construction, so the inspector names the fix.
    /// </remarks>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AudioMixBus))]
    public sealed class AudioMixBusEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            // The live reading is one running bus's, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return root;
            }

            AudioMixBus bus = (AudioMixBus)target;
            if (!bus.IsOnListener)
            {
                root.Insert(
                    0,
                    new HelpBox(
                        "Add an AudioListener to this object. Unity calls a filter on the listener's object with the sum of every source, so a mix bus anywhere else shapes nothing.",
                        HelpBoxMessageType.Warning
                    )
                );
            }

            VisualElement section = DxAudioInspectorUi.AddSection(root, "Mix Path");
            VisualElement rows = new VisualElement();
            section.Add(rows);
            DxAudioInspectorUi.AddChainRows(rows, bus.Chain);
            rows.TrackSerializedObjectValue(
                serializedObject,
                _ =>
                {
                    rows.Clear();
                    DxAudioInspectorUi.AddChainRows(rows, bus.Chain);
                }
            );
            DxAudioInspectorUi.RefreshWhilePlaying(rows, bus.Chain);
            return root;
        }
    }
}
