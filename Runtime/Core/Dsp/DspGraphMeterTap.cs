namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;

    /// <summary>Publishes graph audition peaks from one audio thread to the editor without copying PCM.</summary>
    internal sealed class DspGraphMeterTap
    {
        internal DspGraphSpectrumTap Spectrum { get; }

        private readonly int[] _nodes = new int[DspGraph.MaximumNodes];
        private readonly int[] _edges = new int[DspGraph.MaximumEdges];
        private readonly PeakHold[] _nodePeaks = new PeakHold[DspGraph.MaximumNodes];
        private readonly long[] _edgePeaks = new long[DspGraph.MaximumEdges];
        private readonly long _holdFrames;
        private long _frame;

        internal DspGraphMeterTap(int sampleRate, DspGraphSpectrumTap spectrum = null)
        {
            Spectrum = spectrum;
            _holdFrames = 2L * sampleRate;
            for (int node = 0; node < DspGraph.MaximumNodes; ++node)
            {
                _nodes[node] = node;
            }

            for (int edge = 0; edge < DspGraph.MaximumEdges; ++edge)
            {
                _edges[edge] = edge;
            }
        }

        private static (float, float) Scan(float[] buffer, int start, int count, int channels)
        {
            float left = 0f;
            float right = 0f;
            int rightOffset = channels == 1 ? 0 : 1;
            int end = start + count;
            for (int sample = start; sample < end; sample += channels)
            {
                float a = Math.Abs(buffer[sample]);
                float b = Math.Abs(buffer[sample + rightOffset]);
                // Ignore NaN so one invalid sample cannot replace a valid block peak.
                left = left < a ? a : left;
                right = right < b ? b : right;
            }

            return (left, right);
        }

        private static long Pack(float left, float right)
        {
            return (long)BitConverter.SingleToInt32Bits(left) << 32 | (uint)BitConverter.SingleToInt32Bits(right);
        }

        private static (float, float) Unpack(long value)
        {
            return (BitConverter.Int32BitsToSingle((int)(value >> 32)), BitConverter.Int32BitsToSingle(unchecked((int)value)));
        }

        // Set the authored-to-prepared mapping before handing the tap to the audio thread.
        internal void MapNode(int authored, int prepared)
        {
            _nodes[authored] = prepared;
        }

        internal void MapEdge(int authored, int prepared)
        {
            _edges[authored] = prepared;
        }

        internal int PreparedNode(int authored)
        {
            return _nodes[authored];
        }

        internal (bool Active, float Left, float Right, float HeldLeft, float HeldRight, bool Clipped) ReadNode(int authored)
        {
            int node = PreparedNode(authored);
            return node < 0 ? default : _nodePeaks[node].Read();
        }

        internal (bool Active, float Left, float Right) ReadEdge(int authored)
        {
            int edge = _edges[authored];
            if (edge < 0)
            {
                return default;
            }

            (float left, float right) = Unpack(Volatile.Read(ref _edgePeaks[edge]));
            return (true, left, right);
        }

        internal void Advance(int frames)
        {
            _frame += frames;
        }

        internal void ObserveNode(int node, float[] buffer, int count, int channels)
        {
            (float left, float right) = Scan(buffer, 0, count, channels);
            _nodePeaks[node].Write(left, right, _frame, _holdFrames);
        }

        internal void ObserveEdge(int edge, int from)
        {
            Volatile.Write(ref _edgePeaks[edge], _nodePeaks[from].Latest);
        }

        internal void ObserveDelayedEdge(int edge, float[] line, int start, int count, int channels)
        {
            int first = Math.Min(count, line.Length - start);
            (float left, float right) = Scan(line, start, first, channels);
            if (first < count)
            {
                (float leftWrapped, float rightWrapped) = Scan(line, 0, count - first, channels);
                left = Math.Max(left, leftWrapped);
                right = Math.Max(right, rightWrapped);
            }

            Volatile.Write(ref _edgePeaks[edge], Pack(left, right));
        }

        private struct PeakHold
        {
            internal long Latest;

            private long _held;
            private int _clip;
            private float _left;
            private float _right;
            private long _leftExpires;
            private long _rightExpires;
            private long _clipExpires;

            internal void Write(float left, float right, long frame, long hold)
            {
                long expires = frame + hold;
                if (_left <= left || _leftExpires <= frame)
                {
                    _left = left;
                    _leftExpires = expires;
                }

                if (_right <= right || _rightExpires <= frame)
                {
                    _right = right;
                    _rightExpires = expires;
                }

                if (1f <= left || 1f <= right)
                {
                    _clipExpires = expires;
                }

                Volatile.Write(ref Latest, Pack(left, right));
                Volatile.Write(ref _held, Pack(_left, _right));
                Volatile.Write(ref _clip, frame < _clipExpires ? 1 : 0);
            }

            internal (bool, float, float, float, float, bool) Read()
            {
                (float left, float right) = Unpack(Volatile.Read(ref Latest));
                (float heldLeft, float heldRight) = Unpack(Volatile.Read(ref _held));
                return (true, left, right, heldLeft, heldRight, Volatile.Read(ref _clip) == 1);
            }
        }
    }
}
