namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Shows the mix bus's chain, and says when the object Unity hands the mix to is not this one.</summary>
    /// <remarks>
    /// Unity calls a filter on the object that carries the AudioListener with the sum of every source. A mix bus
    /// anywhere else receives no block at all, which is silent by construction, so the inspector names the fix. An
    /// AudioSource on the same object is refused too: the engine also runs the filter for that source's block, so the
    /// one chain would see it twice.
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
                        "Add an AudioListener to this object. Unity calls a filter on the listener's object "
                            + "with the sum of every source, so a mix bus anywhere else shapes nothing.",
                        HelpBoxMessageType.Warning
                    )
                );
            }
            else if (bus.GetComponent<AudioSource>() != null)
            {
                root.Insert(
                    0,
                    new HelpBox(
                        "Remove the AudioSource from this object. The engine runs a filter on the listener's object "
                            + "for that source too, so this chain would shape that sound and then the mix.",
                        HelpBoxMessageType.Warning
                    )
                );
            }

            VisualElement section = DxAudioInspectorUi.AddSection(root, "Mix Path");
            VisualElement rows = new VisualElement();
            section.Add(rows);
            DxAudioInspectorUi.AddChainRows(rows, bus.Chain, mix: true);
            rows.TrackSerializedObjectValue(
                serializedObject,
                _ =>
                {
                    rows.Clear();
                    DxAudioInspectorUi.AddChainRows(rows, bus.Chain, mix: true);
                }
            );
            DxAudioInspectorUi.RefreshWhilePlaying(rows, bus.Chain, mix: true);
            return root;
        }
    }
}
