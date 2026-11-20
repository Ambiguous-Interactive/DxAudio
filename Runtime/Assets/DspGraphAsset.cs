namespace WallstopStudios.DxAudio
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>A graph of chains, authored as data: which nodes run, how they connect, and where the signal enters and leaves.</summary>
    /// <remarks>
    /// A chain is a recipe for one signal path; a graph is a recipe for several that run side by side. This asset holds
    /// the recipe only: a voice builds its own <see cref="DspGraph" /> from it, with a fresh chain per node, because
    /// delay lines and envelopes belong to the voice that is playing. The cue, the track or the player names the asset;
    /// nothing here runs audio.
    ///
    /// Nodes are named, and edges name their two ends, so adding or removing a node does not move the edges that pass
    /// over another node. The name is the identity: two nodes may not share one, and an edge to a name no node holds
    /// is refused rather than guessed at.
    /// </remarks>
    [CreateAssetMenu(fileName = "DspGraph", menuName = "DxAudio/DSP/Graph")]
    public sealed class DspGraphAsset : ScriptableObject
    {
        /// <summary>The rate <see cref="FindProblems" /> prepares at when it checks the memory budget.</summary>
        public const int ReferenceSampleRate = 48_000;

        /// <summary>The block size <see cref="FindProblems" /> prepares with when it checks the memory budget.</summary>
        public const int ReferenceBlockFrames = 1_024;

        /// <summary>The channel count <see cref="FindProblems" /> prepares with when it checks the memory budget.</summary>
        public const int ReferenceChannels = 2;

        /// <summary>The authored nodes, in the order the account lists them.</summary>
        public IReadOnlyList<DspGraphNode> Nodes => _nodes;

        /// <summary>The authored connections.</summary>
        public IReadOnlyList<DspGraphEdge> Edges => _edges;

        /// <summary>The name of the node the signal enters.</summary>
        public string InputNode => _inputNode;

        /// <summary>The name of the node the signal leaves.</summary>
        public string OutputNode => _outputNode;

        /// <summary>How many nodes the graph holds.</summary>
        public int NodeCount => _nodes.Count;

        /// <summary>How many edges the graph holds.</summary>
        public int EdgeCount => _edges.Count;

        [SerializeField]
        [Tooltip("The nodes the signal can pass through. Each one runs a chain, or passes its input on when its chain is empty.")]
        private List<DspGraphNode> _nodes = new();

        [SerializeField]
        [Tooltip("The connections between nodes. A node with several incoming edges sums them, and one with several outgoing edges feeds every one.")]
        private List<DspGraphEdge> _edges = new();

        [SerializeField]
        [Tooltip("The node that receives the voice's block. The signal enters here.")]
        private string _inputNode;

        [SerializeField]
        [Tooltip("The node that leaves the graph. The signal is read from here.")]
        private string _outputNode;

        /// <summary>Builds a graph asset in code, for tests and for games that author audio at runtime.</summary>
        public static DspGraphAsset Create(IEnumerable<DspGraphNode> nodes, IEnumerable<DspGraphEdge> edges, string inputNode, string outputNode)
        {
            DspGraphAsset asset = CreateInstance<DspGraphAsset>();
            asset._nodes = nodes == null ? new List<DspGraphNode>() : new List<DspGraphNode>(nodes);
            asset._edges = edges == null ? new List<DspGraphEdge>() : new List<DspGraphEdge>(edges);
            asset._inputNode = inputNode ?? string.Empty;
            asset._outputNode = outputNode ?? string.Empty;
            return asset;
        }

        /// <summary>The position of the node named <paramref name="name" />, or -1 when no node holds that name.</summary>
        /// <remarks>
        /// The position is what <see cref="DspGraph" /> calls a node, because a graph holds its nodes in the order they
        /// were added. It moves when the authored list changes, so code should resolve a name at the moment it builds.
        /// </remarks>
        public int IndexOfNode(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            for (int index = 0; index < _nodes.Count; index++)
            {
                DspGraphNode node = _nodes[index];
                if (node != null && string.Equals(node.Name, name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Builds a graph that holds one fresh chain per node, ready for <see cref="DspGraph.TryPrepare" />.
        /// </summary>
        /// <remarks>
        /// This is prepare work, not play work: it allocates the graph and one fresh chain per node, so a voice calls it
        /// from its warm-up or its first meet of the asset, never from the audio thread. A refused asset answers
        /// <see langword="false" /> and leaves <paramref name="graph" /> null, so the caller passes the block through
        /// instead of running half a graph. <see cref="FindProblems" /> says which part failed.
        /// </remarks>
        public bool TryCreateGraph(out DspGraph graph)
        {
            graph = null;
            return TryCreateGraphCore(out graph);
        }

        /// <summary>What stops this asset from playing, as one line per problem; empty means it can be built.</summary>
        /// <remarks>
        /// This is authoring and diagnostics work, so it allocates and reads the same fields the inspector shows. The
        /// audio path calls <see cref="TryCreateGraph" /> while it prepares a voice, never this.
        /// </remarks>
        public IReadOnlyList<string> FindProblems()
        {
            List<string> problems = new();
            CollectProblems(
                (_, _, message) =>
                {
                    problems.Add(message);
                }
            );
            return problems;
        }

        internal DspGraph CollectProblems(Action<int, int, string> report)
        {
            bool found = false;
            void Add(int node, int edge, string message)
            {
                found = true;
                report(node, edge, message);
            }

            if (_nodes.Count == 0)
            {
                Add(-1, -1, "The graph holds no nodes, so it cannot play.");
                return null;
            }

            if (DspGraph.MaximumNodes < _nodes.Count)
            {
                Add(-1, -1, $"The graph holds {_nodes.Count} nodes, but a graph plays at most {DspGraph.MaximumNodes}.");
            }

            if (DspGraph.MaximumEdges < _edges.Count)
            {
                Add(-1, -1, $"The graph holds {_edges.Count} edges, but a graph plays at most {DspGraph.MaximumEdges}.");
            }

            for (int index = 0; index < _nodes.Count; index++)
            {
                DspGraphNode node = _nodes[index];
                if (node == null)
                {
                    Add(index, -1, $"Node {index + 1} is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.Name))
                {
                    Add(index, -1, $"Node {index + 1} has no name, so no edge can reach it.");
                    continue;
                }

                if (HasEarlierNodeNamed(index, node.Name))
                {
                    Add(index, -1, $"More than one node is named \"{node.Name}\", so an edge to it is ambiguous.");
                }
            }

            if (IndexOfNode(_inputNode) < 0)
            {
                Add(-1, -1, string.IsNullOrWhiteSpace(_inputNode) ? "The graph has no input node." : $"The input node \"{_inputNode}\" is no node in this graph.");
            }

            if (IndexOfNode(_outputNode) < 0)
            {
                Add(-1, -1, string.IsNullOrWhiteSpace(_outputNode) ? "The graph has no output node." : $"The output node \"{_outputNode}\" is no node in this graph.");
            }

            for (int index = 0; index < _edges.Count; index++)
            {
                DspGraphEdge edge = _edges[index];
                if (edge == null)
                {
                    Add(-1, index, $"Edge {index + 1} is empty.");
                    continue;
                }

                int from = IndexOfNode(edge.From);
                int to = IndexOfNode(edge.To);
                if (from < 0)
                {
                    Add(-1, index, $"An edge names \"{edge.From}\", which is no node in this graph.");
                }

                if (to < 0)
                {
                    Add(-1, index, $"An edge names \"{edge.To}\", which is no node in this graph.");
                }

                if (0 <= from && from == to)
                {
                    Add(-1, index, $"An edge joins \"{edge.From}\" to itself.");
                }
            }

            if (!found)
            {
                AddCycleProblems(Add);
            }

            if (!found)
            {
                AddPathProblems(Add, IndexOfNode(_inputNode), IndexOfNode(_outputNode));
            }

            return found ? null : PrepareForFindings(Add);
        }

        /// <summary>Adds one problem per edge the graph refuses after its ends are known to exist.</summary>
        private void AddCycleProblems(Action<int, int, string> report)
        {
            DspGraph probe = new();
            for (int remaining = _nodes.Count; 0 < remaining; --remaining)
            {
                _ = probe.TryAddNode(new DspChain(), out _);
            }

            for (int index = 0; index < _edges.Count; index++)
            {
                DspGraphEdge edge = _edges[index];
                if (!probe.TryAddEdge(IndexOfNode(edge.From), IndexOfNode(edge.To)))
                {
                    report(-1, index, $"The edge from \"{edge.From}\" to \"{edge.To}\" repeats an edge or closes a cycle.");
                }
            }
        }

        /// <summary>Adds one problem per node that the input cannot reach or that cannot reach the output.</summary>
        /// <remarks>
        /// The graph prepares only a layout where every node lies on the path, so an orphan node refuses the whole graph
        /// and the voice plays unshaped. This names the node, because the prepared graph does not.
        /// </remarks>
        private void AddPathProblems(Action<int, int, string> report, int input, int output)
        {
            bool[] reached = new bool[_nodes.Count];
            bool[] reaches = new bool[_nodes.Count];
            reached[input] = true;
            reaches[output] = true;
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (DspGraphEdge edge in _edges)
                {
                    int from = IndexOfNode(edge.From);
                    int to = IndexOfNode(edge.To);
                    if (reached[from] && !reached[to])
                    {
                        reached[to] = true;
                        grew = true;
                    }

                    if (reaches[to] && !reaches[from])
                    {
                        reaches[from] = true;
                        grew = true;
                    }
                }
            }

            for (int index = 0; index < _nodes.Count; index++)
            {
                if (!reached[index] || !reaches[index])
                {
                    report(index, -1, $"The node \"{_nodes[index].Name}\" is off the path from the input to the output, so it would never run.");
                }
            }
        }

        /// <summary>Adds a finding when the graph cannot be prepared at a reference output layout.</summary>
        /// <remarks>
        /// The memory a graph reserves grows with the block size and the channel count, so no check settles every device.
        /// This one prepares at <see cref="ReferenceSampleRate" /> Hz for <see cref="ReferenceChannels" /> channels and
        /// <see cref="ReferenceBlockFrames" />-frame blocks, and reports a refusal instead of promising a graph that
        /// some device would bypass. A game prepares the graph itself on each device, so a refusal here is a warning
        /// about one layout, not a verdict about every layout.
        /// </remarks>
        private DspGraph PrepareForFindings(Action<int, int, string> report)
        {
            if (!TryCreateGraph(out DspGraph graph))
            {
                report(-1, -1, "The graph cannot be built.");
                return null;
            }

            if (!graph.TryPrepare(ReferenceSampleRate, ReferenceBlockFrames, ReferenceChannels, IndexOfNode(_inputNode), IndexOfNode(_outputNode)))
            {
                report(
                    -1,
                    -1,
                    $"The graph cannot be prepared at {ReferenceSampleRate} Hz for {ReferenceChannels} channels and "
                        + $"{ReferenceBlockFrames}-frame blocks: a chain refused the layout, or the graph is over its {DspGraph.MaximumPreparedBytes / (1024 * 1024)} MiB budget."
                );
                return null;
            }

            return graph;
        }

        /// <summary>Whether a node before <paramref name="index" /> already holds <paramref name="name" />.</summary>
        private bool HasEarlierNodeNamed(int index, string name)
        {
            for (int earlier = 0; earlier < index; earlier++)
            {
                DspGraphNode node = _nodes[earlier];
                if (node != null && string.Equals(node.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryCreateGraphCore(out DspGraph graph)
        {
            if (_nodes.Count == 0)
            {
                graph = null;
                return false;
            }

            int input = IndexOfNode(_inputNode);
            int output = IndexOfNode(_outputNode);
            if (input < 0 || output < 0)
            {
                graph = null;
                return false;
            }

            DspGraph built = new();
            for (int index = 0; index < _nodes.Count; index++)
            {
                DspGraphNode node = _nodes[index];
                if (node == null || HasEarlierNodeNamed(index, node.Name))
                {
                    graph = null;
                    return false;
                }

                if (!built.TryAddNode(node.CreateChain(), out int placed) || placed != index)
                {
                    graph = null;
                    return false;
                }
            }

            foreach (DspGraphEdge edge in _edges)
            {
                if (edge == null || !built.TryAddEdge(IndexOfNode(edge.From), IndexOfNode(edge.To)))
                {
                    graph = null;
                    return false;
                }
            }

            graph = built;
            return true;
        }
    }
}
