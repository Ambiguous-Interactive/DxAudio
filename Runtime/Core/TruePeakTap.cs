namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;

    /// <summary>Moves listener blocks from the audio callback to the editor's true-peak filter.</summary>
    internal sealed class TruePeakTap
    {
        // Over 300 ms at 48 kHz and eight channels, with a normal read every 50 ms.
        private const int Capacity = 1 << 17;
        private const int Mask = Capacity - 1;
        private const int HeaderSize = 2;
        private readonly float[] _queue = new float[Capacity];
        private readonly TruePeakFilter _filter = new();
        private int _writing;
        private int _written;
        private int _read;
        private int _reset;
        private int _overrun;
        private bool _gap;

        internal void RequestReset()
        {
            Volatile.Write(ref _reset, 1);
        }

        internal void Enqueue(float[] samples, int channels)
        {
            // ProcessBuffer can also be driven explicitly while Unity is delivering audio.
            if (Interlocked.CompareExchange(ref _writing, 1, 0) != 0)
            {
                RequestReset();
                Volatile.Write(ref _overrun, 1);
                return;
            }

            try
            {
                WriteBlock(samples, channels);
            }
            finally
            {
                Volatile.Write(ref _writing, 0);
            }
        }

        internal bool TryRead(out float peak, out bool overrun)
        {
            int end = Volatile.Read(ref _written);
            int read = _read;
            bool received = read != end;
            float maximum = 0f;
            while (read != end)
            {
                int start = read & Mask;
                int channels = (int)_queue[start];
                if (channels == 0)
                {
                    read = unchecked(read + Capacity - start);
                    continue;
                }

                if (channels < 0)
                {
                    _filter.Reset();
                    channels = -channels;
                }

                int count = (int)_queue[start + 1];
                maximum = Math.Max(maximum, _filter.Observe(_queue, start + HeaderSize, count, channels));
                read = unchecked(read + HeaderSize + count);
                Volatile.Write(ref _read, read);
            }

            if (Interlocked.Exchange(ref _overrun, 0) == 1)
            {
                peak = 0f;
                overrun = true;
                return false;
            }

            peak = maximum;
            overrun = false;
            return received;
        }

        private void WriteBlock(float[] samples, int channels)
        {
            if (TruePeakFilter.MaxChannels < channels)
            {
                _gap = true;
                return;
            }

            int length = samples.Length;
            int count = length - length % channels;
            int written = _written;
            int start = written & Mask;
            int tail = Capacity - start;
            int free = Capacity - unchecked(written - Volatile.Read(ref _read));
            int padding = count <= tail - HeaderSize ? 0 : tail;
            if (free - padding - HeaderSize < count)
            {
                _gap = true;
                Volatile.Write(ref _overrun, 1);
                return;
            }

            // A zero header skips the unused tail. Each queued block stays contiguous.
            if (padding != 0)
            {
                _queue[start] = 0f;
                start = 0;
            }

            bool reset = Interlocked.Exchange(ref _reset, 0) == 1 || _gap;
            _gap = false;
            _queue[start] = reset ? -channels : channels;
            _queue[start + 1] = count;
            Array.Copy(samples, 0, _queue, start + HeaderSize, count);
            Volatile.Write(ref _written, unchecked(written + padding + HeaderSize + count));
        }
    }
}
