namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;

    /// <summary>Transfers paired stereo windows from one audition audio thread to the editor.</summary>
    internal sealed class DspGraphSpectrumTap
    {
        internal int SampleRate { get; }

        private readonly float[] _input;
        private readonly float[] _output;
        private long _selection = uint.MaxValue;
        private long _capturedSelection;
        private int _written;
        private int _copySamples;
        private int _ready;

        internal DspGraphSpectrumTap(int sampleRate, int frames)
        {
            SampleRate = sampleRate;
            _input = new float[2 * frames];
            _output = new float[_input.Length];
        }

        internal void Select(int node)
        {
            // The editor is the sole writer. A generation distinguishes A → B → A from an unchanged A.
            long previous = Interlocked.Read(ref _selection);
            if (unchecked((int)previous) != node)
            {
                long generation = (previous >> 32) + 1;
                Interlocked.Exchange(ref _selection, (generation << 32) | unchecked((uint)node));
            }
        }

        internal bool TryRead(float[] input, float[] output)
        {
            if (Volatile.Read(ref _ready) == 0)
            {
                return false;
            }

            bool current = Interlocked.Read(ref _capturedSelection) == Interlocked.Read(ref _selection);
            if (current)
            {
                Array.Copy(_input, input, _input.Length);
                Array.Copy(_output, output, _output.Length);
            }

            // Release after the copies. The audio thread never waits for the editor.
            Volatile.Write(ref _ready, 0);
            return current;
        }

        internal int BeginBlock(DspGraphMeterTap meters, int frames)
        {
            if (Volatile.Read(ref _ready) != 0)
            {
                return -1;
            }

            long selection = Interlocked.Read(ref _selection);
            if (Interlocked.Read(ref _capturedSelection) != selection)
            {
                // The _ready handshake serializes this field; Interlocked keeps the 64-bit compare whole.
                Interlocked.Exchange(ref _capturedSelection, selection);
                _written = 0;
            }

            int authored = unchecked((int)selection);
            if (authored < 0 || DspGraph.MaximumNodes <= authored)
            {
                return -1;
            }

            _copySamples = Math.Min(2 * frames, _input.Length - _written);
            return meters.PreparedNode(authored);
        }

        internal void CaptureInput(float[] samples, int channels)
        {
            CopyStereo(samples, _input, channels);
        }

        internal void CaptureOutput(float[] samples, int channels)
        {
            CopyStereo(samples, _output, channels);
            _written += _copySamples;
            if (_written == _output.Length)
            {
                _written = 0;
                Volatile.Write(ref _ready, 1);
            }
        }

        private void CopyStereo(float[] samples, float[] destination, int channels)
        {
            if (channels == 2)
            {
                Array.Copy(samples, 0, destination, _written, _copySamples);
                return;
            }

            int right = channels == 1 ? 0 : 1;
            int end = _written + _copySamples;
            for (int write = _written, read = 0; write < end; write += 2, read += channels)
            {
                destination[write] = samples[read];
                destination[write + 1] = samples[read + right];
            }
        }
    }
}
