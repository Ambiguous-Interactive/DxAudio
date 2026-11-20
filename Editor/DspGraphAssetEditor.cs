namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine.UIElements;

    /// <summary>Shows a graph's nodes, its edges, and what it delays each branch by.</summary>
    /// <remarks>
    /// The fields still author every node and edge. This adds the two questions the fields cannot answer: whether the
    /// graph is one the runtime can build, and how far apart the branches would arrive. The latency is measured at the
    /// editor's output rate, because a chain's delay is a count of samples at a rate.
    /// </remarks>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(DspGraphAsset))]
    public sealed class DspGraphAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            // A latency reading belongs to one authored graph, so a mixed selection only edits fields.
            if (targets.Length != 1)
            {
                return root;
            }

            DspGraphAsset asset = (DspGraphAsset)target;
            root.Add(new Button(() => DxAudioAuthoringWindow.Open(asset)) { text = "Open in DxAudio Authoring" });
            VisualElement section = DxAudioInspectorUi.AddSection(root, "Signal Path");
            VisualElement rows = new VisualElement();
            section.Add(rows);
            DxAudioInspectorUi.AddGraphRows(rows, asset);
            // A serialized change rebuilds the rows; nothing else moves this reading.
            rows.TrackSerializedObjectValue(
                serializedObject,
                _ =>
                {
                    rows.Clear();
                    DxAudioInspectorUi.AddGraphRows(rows, asset);
                }
            );
            return root;
        }
    }
}
