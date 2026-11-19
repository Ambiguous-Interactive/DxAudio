namespace WallstopStudios.DxAudio
{
    using System;
    using UnityEngine;

    /// <summary>One authored node of a <see cref="DspGraphAsset" />: a name, and the chain it runs.</summary>
    /// <remarks>
    /// The name is the node's identity because an edge names its two ends. A position in the list would move when a
    /// node above it is added or removed, and an edge would then carry the signal through a different node with no
    /// warning. A node with no chain is a junction: it passes whatever reaches it straight on, which is how a fan-out
    /// or fan-in with no processing of its own is authored.
    /// </remarks>
    [Serializable]
    public sealed class DspGraphNode
    {
        [SerializeField]
        [Tooltip("The name edges use. It must be unique in the graph, and a rename needs every edge updated.")]
        private string _name;

        [SerializeField]
        [Tooltip("The chain this node runs. Empty makes it a junction that passes its input on untouched.")]
        private DspChainAsset _chain;

        public DspGraphNode()
        {
            _name = string.Empty;
        }

        public DspGraphNode(string name, DspChainAsset chain)
        {
            _name = name ?? string.Empty;
            _chain = chain;
        }

        /// <summary>The node's identity, which edges name.</summary>
        public string Name => _name;

        /// <summary>The chain this node runs, or null for a junction.</summary>
        public DspChainAsset Chain => _chain;

        /// <summary>Builds this node's own chain, or an empty one for a junction.</summary>
        /// <remarks>
        /// Two nodes never share the returned chain: a chain holds delay lines and envelopes, and a graph that ran one
        /// chain at two nodes would run the same line twice in a block.
        /// </remarks>
        public DspChain CreateChain()
        {
            return _chain != null ? _chain.CreateChain() : new DspChain();
        }
    }
}
