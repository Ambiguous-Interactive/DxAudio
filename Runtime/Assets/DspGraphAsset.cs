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
            if (_nodes.Count == 0)
            {
                return false;
            }

            int input = IndexOfNode(_inputNode);
            int output = IndexOfNode(_outputNode);
            if (input < 0 || output < 0)
            {
                return false;
            }

            DspGraph built = new();
            for (int index = 0; index < _nodes.Count; index++)
            {
                DspGraphNode node = _nodes[index];
                if (node == null || HasEarlierNodeNamed(index, node.Name))
                {
                    return false;
                }

                if (!built.TryAddNode(node.CreateChain(), out int placed) || placed != index)
                {
                    return false;
                }
            }

            for (int index = 0; index < _edges.Count; index++)
            {
                DspGraphEdge edge = _edges[index];
                if (edge == null || !built.TryAddEdge(IndexOfNode(edge.From), IndexOfNode(edge.To)))
                {
                    return false;
                }
            }

            graph = built;
            return true;
        }

        /// <summary>What stops this asset from playing, as one line per problem; empty means it can be built.</summary>
        /// <remarks>
        /// This is authoring and diagnostics work, so it allocates and reads the same fields the inspector shows. The
        /// audio path calls <see cref="TryCreateGraph" /> while it prepares a voice, never this.
        /// </remarks>
        public IReadOnlyList<string> FindProblems()
        {
            List<string> problems = new();
            if (_nodes.Count == 0)
            {
                problems.Add("The graph holds no nodes, so it cannot play.");
                return problems;
            }

            if (DspGraph.MaximumNodes < _nodes.Count)
            {
                problems.Add($"The graph holds {_nodes.Count} nodes, but a graph plays at most {DspGraph.MaximumNodes}.");
            }

            if (DspGraph.MaximumEdges < _edges.Count)
            {
                problems.Add($"The graph holds {_edges.Count} edges, but a graph plays at most {DspGraph.MaximumEdges}.");
            }

            for (int index = 0; index < _nodes.Count; index++)
            {
                DspGraphNode node = _nodes[index];
                if (node == null)
                {
                    problems.Add($"Node {index + 1} is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.Name))
                {
                    problems.Add($"Node {index + 1} has no name, so no edge can reach it.");
                    continue;
                }

                if (HasEarlierNodeNamed(index, node.Name))
                {
                    problems.Add($"More than one node is named \"{node.Name}\", so an edge to it is ambiguous.");
                }
            }

            if (IndexOfNode(_inputNode) < 0)
            {
                problems.Add(string.IsNullOrWhiteSpace(_inputNode) ? "The graph has no input node." : $"The input node \"{_inputNode}\" is no node in this graph.");
            }

            if (IndexOfNode(_outputNode) < 0)
            {
                problems.Add(string.IsNullOrWhiteSpace(_outputNode) ? "The graph has no output node." : $"The output node \"{_outputNode}\" is no node in this graph.");
            }

            for (int index = 0; index < _edges.Count; index++)
            {
                DspGraphEdge edge = _edges[index];
                if (edge == null)
                {
                    problems.Add($"Edge {index + 1} is empty.");
                    continue;
                }

                int from = IndexOfNode(edge.From);
                int to = IndexOfNode(edge.To);
                if (from < 0)
                {
                    problems.Add($"An edge names \"{edge.From}\", which is no node in this graph.");
                }

                if (to < 0)
                {
                    problems.Add($"An edge names \"{edge.To}\", which is no node in this graph.");
                }

                if (0 <= from && from == to)
                {
                    problems.Add($"An edge joins \"{edge.From}\" to itself.");
                }
            }

            if (problems.Count == 0)
            {
                AddCycleProblems(problems);
            }

            if (problems.Count == 0)
            {
                AddPathProblems(problems, IndexOfNode(_inputNode), IndexOfNode(_outputNode));
            }

            return problems;
        }

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

        /// <summary>Adds one problem per edge the graph refuses after its ends are known to exist.</summary>
        private void AddCycleProblems(List<string> problems)
        {
            DspGraph probe = new();
            for (int index = 0; index < _nodes.Count; index++)
            {
                _ = probe.TryAddNode(_nodes[index].CreateChain(), out _);
            }

            for (int index = 0; index < _edges.Count; index++)
            {
                DspGraphEdge edge = _edges[index];
                if (!probe.TryAddEdge(IndexOfNode(edge.From), IndexOfNode(edge.To)))
                {
                    problems.Add($"The edge from \"{edge.From}\" to \"{edge.To}\" repeats an edge or closes a cycle.");
                }
            }
        }

        /// <summary>Adds one problem per node that the input cannot reach or that cannot reach the output.</summary>
        /// <remarks>
        /// The graph prepares only a layout where every node lies on the path, so an orphan node refuses the whole graph
        /// and the voice plays unshaped. This names the node, because the prepared graph does not.
        /// </remarks>
        private void AddPathProblems(List<string> problems, int input, int output)
        {
            bool[] reached = new bool[_nodes.Count];
            bool[] reaches = new bool[_nodes.Count];
            reached[input] = true;
            reaches[output] = true;
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int index = 0; index < _edges.Count; index++)
                {
                    DspGraphEdge edge = _edges[index];
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
                    problems.Add($"The node \"{_nodes[index].Name}\" is off the path from the input to the output, so it would never run.");
                }
            }
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
    }
}
