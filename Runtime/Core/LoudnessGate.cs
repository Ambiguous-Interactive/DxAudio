namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Keeps exact block energies and reads the relative gate in logarithmic time.</summary>
    internal sealed class LoudnessGate
    {
        private readonly double _absoluteThreshold;

        // Index zero is the empty subtree. Nodes share storage and grow in batches.
        private Node[] _nodes = new Node[256];
        private int _used;
        private int _root;

        internal LoudnessGate(double absoluteThreshold)
        {
            _absoluteThreshold = absoluteThreshold;
        }

        internal void Add(double energy)
        {
            if (!(_absoluteThreshold < energy))
            {
                return;
            }

            int capacity = _nodes.Length;
            if (_used == capacity - 1)
            {
                Array.Resize(ref _nodes, 2 * capacity);
            }

            _root = Insert(_root, energy);
        }

        internal double? Mean()
        {
            if (_root == 0)
            {
                return null;
            }

            ref Node root = ref _nodes[_root];
            double threshold = Math.Max(_absoluteThreshold, 0.1d * (root.Sum / root.Count));
            double sum = 0d;
            int count = 0;
            int index = _root;
            while (index != 0)
            {
                ref Node node = ref _nodes[index];
                if (threshold < node.Energy)
                {
                    // The whole right subtree passes when this energy passes.
                    ref Node right = ref _nodes[node.Right];
                    sum += node.Energy * node.Occurrences + right.Sum;
                    count += node.Occurrences + right.Count;
                    index = node.Left;
                }
                else
                {
                    index = node.Right;
                }
            }

            return sum / count;
        }

        internal void Clear()
        {
            _used = 0;
            _root = 0;
        }

        // An AVL tree puts a bound on both sorted input and sustained playback.
        private int Insert(int index, double energy)
        {
            if (index == 0)
            {
                ++_used;
                _nodes[_used] = new Node(energy);
                return _used;
            }

            // Add reserves space before recursing, so the node array stays in place.
            ref Node node = ref _nodes[index];
            if (energy < node.Energy)
            {
                node.Left = Insert(node.Left, energy);
            }
            else if (node.Energy < energy)
            {
                node.Right = Insert(node.Right, energy);
            }
            else
            {
                ++node.Occurrences;
            }

            Update(ref node);
            int balance = Balance(in node);
            if (1 < balance)
            {
                if (Balance(in _nodes[node.Left]) < 0)
                {
                    node.Left = RotateLeft(node.Left);
                }

                return RotateRight(index);
            }

            if (balance < -1)
            {
                if (0 < Balance(in _nodes[node.Right]))
                {
                    node.Right = RotateRight(node.Right);
                }

                return RotateLeft(index);
            }

            return index;
        }

        private int Balance(in Node node)
        {
            return _nodes[node.Left].Height - _nodes[node.Right].Height;
        }

        private int RotateLeft(int index)
        {
            ref Node node = ref _nodes[index];
            int parent = node.Right;
            ref Node right = ref _nodes[parent];
            node.Right = right.Left;
            right.Left = index;
            Update(ref node);
            Update(ref right);
            return parent;
        }

        private int RotateRight(int index)
        {
            ref Node node = ref _nodes[index];
            int parent = node.Left;
            ref Node left = ref _nodes[parent];
            node.Left = left.Right;
            left.Right = index;
            Update(ref node);
            Update(ref left);
            return parent;
        }

        private void Update(ref Node node)
        {
            ref Node left = ref _nodes[node.Left];
            ref Node right = ref _nodes[node.Right];
            node.Count = node.Occurrences + left.Count + right.Count;
            node.Sum = node.Energy * node.Occurrences + left.Sum + right.Sum;
            node.Height = 1 + Math.Max(left.Height, right.Height);
        }

        private struct Node
        {
            internal readonly double Energy;
            internal double Sum;
            internal int Left;
            internal int Right;
            internal int Occurrences;
            internal int Count;
            internal int Height;

            internal Node(double energy)
            {
                Energy = energy;
                Sum = energy;
                Left = 0;
                Right = 0;
                Occurrences = 1;
                Count = 1;
                Height = 1;
            }
        }
    }
}
