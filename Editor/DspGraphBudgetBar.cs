namespace WallstopStudios.DxAudio.Editor
{
    using UnityEngine.UIElements;

    internal sealed class DspGraphBudgetBar : VisualElement
    {
        private readonly Label _nodes = new() { name = "DxAudioGraphNodeBudget", tooltip = "Counts all authored nodes, including Input and Output." };
        private readonly Label _edges = new() { name = "DxAudioGraphEdgeBudget" };
        private readonly Label _memory = new() { name = "DxAudioGraphMemoryBudget" };

        internal DspGraphBudgetBar()
        {
            name = "DxAudioGraphBudgets";
            AddToClassList("dxaudio-graph-budgets");
            _nodes.AddToClassList("dxaudio-graph-budget");
            _edges.AddToClassList("dxaudio-graph-budget");
            _memory.AddToClassList("dxaudio-graph-budget");
            _memory.tooltip =
                $"Node buffers and edge delay lines at {DspGraphAsset.ReferenceSampleRate / 1000} kHz, "
                + $"{DspGraphAsset.ReferenceChannels} channels, and {DspGraphAsset.ReferenceBlockFrames:N0} frames.";
            Add(_nodes);
            Add(_edges);
            Add(_memory);
        }

        internal void Refresh(DspGraphAsset graph, DspGraph prepared)
        {
            _nodes.text = $"Nodes {graph.NodeCount}/{DspGraph.MaximumNodes}";
            _edges.text = $"Edges {graph.EdgeCount}/{DspGraph.MaximumEdges}";
            _nodes.EnableInClassList("dxaudio-graph-budget-exceeded", DspGraph.MaximumNodes < graph.NodeCount);
            _edges.EnableInClassList("dxaudio-graph-budget-exceeded", DspGraph.MaximumEdges < graph.EdgeCount);
            _memory.text = prepared == null ? "Graph buffers unavailable" : $"Graph buffers {prepared.PreparedBytes / 1024d:N1}/{DspGraph.MaximumPreparedBytes / 1024} KiB";
        }
    }
}
