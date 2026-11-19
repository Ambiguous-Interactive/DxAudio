namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>A signal graph: one input fans out into branches, and the branches sum into one output.</summary>
    /// <remarks>
    /// A chain runs stages in series. A graph runs chains in parallel instead: a node holds a <see cref="DspChain"/>,
    /// an edge carries a node's output into a later node, and a node that takes several edges sums them. A node with
    /// several outgoing edges feeds every one, which is the fan-out, and a node with several incoming edges sums them,
    /// which is the fan-in.
    ///
    /// Branches report their own delay, so two branches that reach the same node would arrive out of step. The graph
    /// delays the shorter branch on the edge that joins them, which keeps every path aligned, and reports the longest
    /// path as <see cref="LatencySamples"/>. A branch that adds no delay therefore needs no buffer.
    ///
    /// <see cref="TryPrepare"/> sizes every buffer and refuses a cycle, a stray node or a graph over the size budget
    /// before any audio runs. <see cref="Process"/> then allocates nothing, logs nothing, takes no lock and throws
    /// nothing for a prepared shape. Like a chain, a graph is built and prepared before a block runs it: preparing one
    /// while another thread is inside <see cref="Process"/> would hand that block half a graph.
    /// </remarks>
    public sealed class DspGraph
    {
        /// <summary>How many nodes one graph holds.</summary>
        public const int MaximumNodes = 16;

        /// <summary>How many edges one graph holds, counted in one direction each.</summary>
        public const int MaximumEdges = 32;

        /// <summary>The most memory one prepared graph may reserve, in bytes.</summary>
        /// <remarks>
        /// A graph sizes a buffer per node and a delay line per edge that needs one, so its cost grows with every
        /// block shape. Two mebibytes holds a stereo graph of sixteen nodes and thirty-two edges at any Unity block
        /// size; a graph that asks for more is refused in <see cref="TryPrepare"/> rather than allocated.
        /// </remarks>
        public const int MaximumPreparedBytes = 2 * 1024 * 1024;

        private readonly DspChain[] _chains = new DspChain[MaximumNodes];
        private readonly int[] _edgeFrom = new int[MaximumEdges];
        private readonly int[] _edgeTo = new int[MaximumEdges];
        private readonly int[] _edgeDelayFrames = new int[MaximumEdges];
        private readonly int[] _edgePosition = new int[MaximumEdges];
        private readonly int[] _order = new int[MaximumNodes];
        private readonly int[] _age = new int[MaximumNodes];
        private readonly int[] _incoming = new int[MaximumNodes];
        private readonly bool[] _reached = new bool[MaximumNodes];
        private int _channels;
        private int _blockFrames;
        private int _input;
        private int _output;
        private float[][] _buffers;
        private float[][] _edgeLines;

        public DspGraph()
        {
            _buffers = Array.Empty<float[]>();
            _edgeLines = Array.Empty<float[]>();
        }

        /// <summary>How many nodes the graph holds.</summary>
        public int NodeCount { get; private set; }

        /// <summary>How many edges the graph holds.</summary>
        public int EdgeCount { get; private set; }

        /// <summary>Whether <see cref="TryPrepare"/> accepted this graph, so <see cref="Process"/> runs it.</summary>
        public bool IsPrepared { get; private set; }

        /// <summary>The delay of the longest path from the input to the output, in samples; zero until prepared.</summary>
        public int LatencySamples { get; private set; }

        /// <summary>How many bytes <see cref="TryPrepare"/> reserved; zero until prepared.</summary>
        public int PreparedBytes { get; private set; }

        /// <summary>Adds a node that runs <paramref name="chain"/>, and answers its index.</summary>
        /// <remarks>
        /// Each node owns its chain. A chain is stateful, so a graph that ran one chain at two nodes would run the same
        /// delay line twice in a block and corrupt it, and the second node would see what the first left behind.
        /// </remarks>
        /// <returns>
        /// <see langword="false"/> for a null chain, a chain the graph already holds, or a graph already at
        /// <see cref="MaximumNodes"/>.
        /// </returns>
        public bool TryAddNode(DspChain chain, out int node)
        {
            if (chain == null || MaximumNodes <= NodeCount)
            {
                node = -1;
                return false;
            }

            for (int existing = 0; existing < NodeCount; existing++)
            {
                if (ReferenceEquals(_chains[existing], chain))
                {
                    node = -1;
                    return false;
                }
            }

            _chains[NodeCount] = chain;
            node = NodeCount;
            NodeCount++;
            Invalidate();
            return true;
        }

        /// <summary>Adds an edge that carries the output of <paramref name="from"/> into <paramref name="to"/>.</summary>
        /// <remarks>
        /// A node that receives several edges sums them, so adding an edge is also how a fan-in is built. A cycle is
        /// refused here, at the edge that would close it, and not a block later.
        /// </remarks>
        /// <returns>
        /// <see langword="false"/> for an index no node holds, a node joined to itself, an edge the graph already
        /// holds, a graph already at <see cref="MaximumEdges"/>, or an edge that would close a cycle.
        /// </returns>
        public bool TryAddEdge(int from, int to)
        {
            if (from < 0 || to < 0 || NodeCount <= from || NodeCount <= to || from == to || MaximumEdges <= EdgeCount)
            {
                return false;
            }

            for (int edge = 0; edge < EdgeCount; edge++)
            {
                if (_edgeFrom[edge] == from && _edgeTo[edge] == to)
                {
                    return false;
                }
            }

            if (Reaches(to, from))
            {
                return false;
            }

            _edgeFrom[EdgeCount] = from;
            _edgeTo[EdgeCount] = to;
            EdgeCount++;
            Invalidate();
            return true;
        }

        /// <summary>Sizes every buffer, aligns every branch and holds the graph ready to run.</summary>
        /// <remarks>
        /// The input node receives the caller's block, so an edge into it would be a cycle and
        /// <see cref="TryAddEdge"/> already refuses one. Every node has to be reachable from the input and able to
        /// reach the output; a node that fails either test is dead weight and is refused.
        ///
        /// The node buffers are budgeted before any chain is prepared, because a chain allocates from the layout too:
        /// a channel count that cannot fit the graph is refused without the stages spending memory first. The edge
        /// lines are budgeted once the chains report their delay. A chain's latency is read here, so a stage added to
        /// a prepared chain afterwards leaves the graph's alignment stale; prepare the graph again after editing.
        /// </remarks>
        /// <returns>
        /// <see langword="false"/> for a layout that is not positive in every part, an index no node holds, a cycle,
        /// a node off the path, or a graph that would reserve more than <see cref="MaximumPreparedBytes"/>. A refused
        /// call reserves no graph buffer.
        /// </returns>
        public bool TryPrepare(int sampleRate, int maxBlockSize, int channels, int inputNode, int outputNode)
        {
            Invalidate();
            if (sampleRate <= 0 || maxBlockSize <= 0 || channels <= 0)
            {
                return false;
            }

            if (inputNode < 0 || outputNode < 0 || NodeCount <= inputNode || NodeCount <= outputNode)
            {
                return false;
            }

            if (!OrderNodes() || !AllOnThePath(inputNode, outputNode))
            {
                return false;
            }

            long floats = (long)NodeCount * maxBlockSize * channels;
            if (MaximumPreparedBytes < floats * sizeof(float))
            {
                return false;
            }

            for (int node = 0; node < NodeCount; node++)
            {
                if (!_chains[node].TryPrepare(sampleRate, maxBlockSize, channels))
                {
                    return false;
                }
            }

            // A node arrives as late as its latest predecessor, then its own chain adds its delay. Every edge then
            // waits the difference, so all paths reach the output with the same age.
            for (int index = 0; index < NodeCount; index++)
            {
                int node = _order[index];
                int arrival = 0;
                for (int edge = 0; edge < EdgeCount; edge++)
                {
                    if (_edgeTo[edge] == node && arrival < _age[_edgeFrom[edge]])
                    {
                        arrival = _age[_edgeFrom[edge]];
                    }
                }

                _age[node] = arrival + _chains[node].LatencySamples;
            }

            for (int edge = 0; edge < EdgeCount; edge++)
            {
                int destination = _edgeTo[edge];
                int delay = _age[destination] - _chains[destination].LatencySamples - _age[_edgeFrom[edge]];
                if (delay < 0)
                {
                    return false;
                }

                _edgeDelayFrames[edge] = delay;
                if (0 < delay)
                {
                    floats += ((long)delay + maxBlockSize) * channels;
                }
            }

            if (MaximumPreparedBytes < floats * sizeof(float))
            {
                return false;
            }

            float[][] buffers = new float[NodeCount][];
            for (int node = 0; node < NodeCount; node++)
            {
                buffers[node] = new float[maxBlockSize * channels];
            }

            float[][] lines = new float[EdgeCount][];
            for (int edge = 0; edge < EdgeCount; edge++)
            {
                int delay = _edgeDelayFrames[edge];
                lines[edge] = 0 < delay ? new float[(delay + maxBlockSize) * channels] : Array.Empty<float>();
                _edgePosition[edge] = 0;
            }

            _buffers = buffers;
            _edgeLines = lines;
            _channels = channels;
            _blockFrames = maxBlockSize;
            _input = inputNode;
            _output = outputNode;
            LatencySamples = _age[outputNode];
            PreparedBytes = (int)(floats * sizeof(float));
            IsPrepared = true;
            Reset();
            return true;
        }

        /// <summary>Runs the graph over <paramref name="count"/> interleaved samples of <paramref name="buffer"/> in place.</summary>
        /// <remarks>
        /// A block no prepared graph matches passes through untouched, the same rule a chain and an effect keep.
        /// </remarks>
        public void Process(float[] buffer, int offset, int count, int channels)
        {
            if (!IsPrepared || buffer == null || channels != _channels || offset < 0 || count <= 0 || count % channels != 0 || buffer.Length - count < offset)
            {
                return;
            }

            if (_blockFrames < count / channels)
            {
                return;
            }

            float[][] buffers = _buffers;
            for (int node = 0; node < buffers.Length; node++)
            {
                Array.Clear(buffers[node], 0, count);
            }

            Array.Copy(buffer, offset, buffers[_input], 0, count);
            for (int index = 0; index < NodeCount; index++)
            {
                int node = _order[index];
                float[] produced = buffers[node];
                _chains[node].Process(produced, 0, count, channels);
                for (int edge = 0; edge < EdgeCount; edge++)
                {
                    if (_edgeFrom[edge] == node)
                    {
                        Carry(edge, produced, count, channels, buffers[_edgeTo[edge]]);
                    }
                }
            }

            Array.Copy(buffers[_output], 0, buffer, offset, count);
        }

        /// <summary>Clears every node buffer and delay line and resets every chain, so a reused graph starts silent.</summary>
        public void Reset()
        {
            if (!IsPrepared)
            {
                return;
            }

            for (int node = 0; node < _buffers.Length; node++)
            {
                Array.Clear(_buffers[node], 0, _buffers[node].Length);
                _chains[node].Reset();
            }

            for (int edge = 0; edge < _edgeLines.Length; edge++)
            {
                Array.Clear(_edgeLines[edge], 0, _edgeLines[edge].Length);
                _edgePosition[edge] = 0;
            }
        }

        /// <summary>Carries one node's block along one edge, delayed, and sums it into the destination.</summary>
        private void Carry(int edge, float[] source, int count, int channels, float[] destination)
        {
            int delayFrames = _edgeDelayFrames[edge];
            if (delayFrames == 0)
            {
                for (int sample = 0; sample < count; sample++)
                {
                    destination[sample] += source[sample];
                }

                return;
            }

            // The line holds the block that is still on its way plus the block being written, so one wrap covers a
            // whole read. Everything that ran before this edge wrote its block, so the delay is whole blocks of lag.
            float[] line = _edgeLines[edge];
            int capacity = line.Length;
            int position = _edgePosition[edge];
            for (int sample = 0; sample < count; sample++)
            {
                line[position] = source[sample];
                position++;
                if (position == capacity)
                {
                    position = 0;
                }
            }

            int read = position - count - (delayFrames * channels);
            if (read < 0)
            {
                read += capacity;
            }

            for (int sample = 0; sample < count; sample++)
            {
                destination[sample] += line[read];
                read++;
                if (read == capacity)
                {
                    read = 0;
                }
            }

            _edgePosition[edge] = position;
        }

        /// <summary>Orders the nodes so every node follows all of its predecessors, and refuses a cycle.</summary>
        private bool OrderNodes()
        {
            for (int node = 0; node < NodeCount; node++)
            {
                _incoming[node] = 0;
            }

            for (int edge = 0; edge < EdgeCount; edge++)
            {
                _incoming[_edgeTo[edge]]++;
            }

            int ordered = 0;
            for (int node = 0; node < NodeCount; node++)
            {
                if (_incoming[node] == 0)
                {
                    _order[ordered] = node;
                    ordered++;
                }
            }

            for (int index = 0; index < ordered; index++)
            {
                int node = _order[index];
                for (int edge = 0; edge < EdgeCount; edge++)
                {
                    if (_edgeFrom[edge] == node)
                    {
                        int next = _edgeTo[edge];
                        _incoming[next]--;
                        if (_incoming[next] == 0)
                        {
                            _order[ordered] = next;
                            ordered++;
                        }
                    }
                }
            }

            return ordered == NodeCount;
        }

        /// <summary>Whether every node follows the input and reaches the output.</summary>
        private bool AllOnThePath(int inputNode, int outputNode)
        {
            for (int node = 0; node < NodeCount; node++)
            {
                _reached[node] = false;
            }

            _reached[inputNode] = true;
            for (int index = 0; index < NodeCount; index++)
            {
                int node = _order[index];
                if (!_reached[node])
                {
                    continue;
                }

                for (int edge = 0; edge < EdgeCount; edge++)
                {
                    if (_edgeFrom[edge] == node)
                    {
                        _reached[_edgeTo[edge]] = true;
                    }
                }
            }

            for (int node = 0; node < NodeCount; node++)
            {
                if (!_reached[node])
                {
                    return false;
                }
            }

            for (int node = 0; node < NodeCount; node++)
            {
                _reached[node] = false;
            }

            _reached[outputNode] = true;
            for (int index = NodeCount - 1; 0 <= index; index--)
            {
                int node = _order[index];
                if (!_reached[node])
                {
                    continue;
                }

                for (int edge = 0; edge < EdgeCount; edge++)
                {
                    if (_edgeTo[edge] == node)
                    {
                        _reached[_edgeFrom[edge]] = true;
                    }
                }
            }

            for (int node = 0; node < NodeCount; node++)
            {
                if (!_reached[node])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether an edge from <paramref name="start"/> can already reach <paramref name="target"/>.</summary>
        private bool Reaches(int start, int target)
        {
            if (start == target)
            {
                return true;
            }

            for (int node = 0; node < NodeCount; node++)
            {
                _reached[node] = false;
            }

            _reached[start] = true;
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int edge = 0; edge < EdgeCount; edge++)
                {
                    if (_reached[_edgeFrom[edge]] && !_reached[_edgeTo[edge]])
                    {
                        _reached[_edgeTo[edge]] = true;
                        grew = true;
                    }
                }
            }

            return _reached[target];
        }

        /// <summary>Drops every prepared buffer, so the next block passes through until the graph is prepared again.</summary>
        private void Invalidate()
        {
            IsPrepared = false;
            LatencySamples = 0;
            PreparedBytes = 0;
            _buffers = Array.Empty<float[]>();
            _edgeLines = Array.Empty<float[]>();
        }
    }
}
