namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine.UIElements;

    /// <summary>Shows a chain in the order the signal meets it, with the live reduction of a voice that plays it.</summary>
    /// <remarks>
    /// Order is the sound, so the signal path is drawn as rows rather than left to the default list. The live reading
    /// is the one thing the fields cannot show: how far each stage holds the signal down while audio runs.
    /// </remarks>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(DspChainAsset))]
    public sealed class DspChainAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            // The live reading is one running chain's, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return root;
            }

            DspChainAsset asset = (DspChainAsset)target;
            VisualElement section = DxAudioInspectorUi.AddSection(root, "Signal Path");
            VisualElement rows = new VisualElement();
            section.Add(rows);
            DxAudioInspectorUi.AddChainRows(rows, asset);
            // A serialized change rebuilds the rows; a running voice also moves its reduction with no edit at all.
            rows.TrackSerializedObjectValue(
                serializedObject,
                _ =>
                {
                    rows.Clear();
                    DxAudioInspectorUi.AddChainRows(rows, asset);
                }
            );
            DxAudioInspectorUi.RefreshWhilePlaying(rows, asset);
            Foldout eq = new Foldout { text = "Drawn EQ", value = false };
            eq.Add(new DspEqCurveAuthoring());
            root.Add(eq);
            return root;
        }
    }
}
