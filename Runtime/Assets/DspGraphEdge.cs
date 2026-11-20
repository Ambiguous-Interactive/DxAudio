namespace WallstopStudios.DxAudio
{
    using System;
    using UnityEngine;

    /// <summary>One authored connection of a <see cref="DspGraphAsset" />: the signal leaves one node and enters another.</summary>
    /// <remarks>
    /// An edge names its ends rather than their positions, so it survives a node that is added above them. A node that
    /// receives several edges sums them, which is the fan-in; a node that sends several feeds every one, which is the
    /// fan-out.
    /// </remarks>
    [Serializable]
    public sealed class DspGraphEdge
    {
        /// <summary>The name of the node the signal leaves.</summary>
        public string From => _from;

        /// <summary>The name of the node the signal enters.</summary>
        public string To => _to;

        [SerializeField]
        [Tooltip("The node the signal leaves. Its name must match a node in this graph.")]
        private string _from;

        [SerializeField]
        [Tooltip("The node the signal enters. Its name must match a node in this graph, and it runs after the node it left.")]
        private string _to;

        public DspGraphEdge()
        {
            _from = string.Empty;
            _to = string.Empty;
        }

        public DspGraphEdge(string from, string to)
        {
            _from = from ?? string.Empty;
            _to = to ?? string.Empty;
        }
    }
}
