namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;

    /// <summary>Moves listener blocks from the audio callback to the editor meters.</summary>
    /// <remarks>
    /// Editor-only: <see cref="StereoPeakTap"/> creates this tap from <c>AudioMixBus.TryAttachPeakTap</c>, which is
    /// compiled only under <c>UNITY_EDITOR</c>. Its 64-bit <c>Interlocked</c> reads therefore run on the 64-bit
    /// editor, never in a player audio callback.
    /// </remarks>
    internal sealed class ListenerMeterTap
    {
        // Over 300 ms at 48 kHz and eight channels, with a normal read every 50 ms.
        private const int Capacity = 1 << 17;
        private const int Mask = Capacity - 1;
        private const int HeaderSize = 3;

        internal int SampleRate => unchecked((int)Interlocked.Read(ref _format));

        private readonly float[] _queue = new float[Capacity];
        private readonly TruePeakFilter _filter = new();
        private long _format;
        private int _writing;
        private int _written;
        private int _read;
        private int _readRevision;
        private int _reset;
        private int _overrun;
        private bool _gap;

        internal ListenerMeterTap(int sampleRate)
        {
            _format = sampleRate;
        }

        internal void SetSampleRate(int sampleRate)
        {
            // Publish the rate and its revision together, including resets at the same rate.
            int revision = unchecked((int)(Interlocked.Read(ref _format) >> 32) + 1);
            Interlocked.Exchange(ref _format, ((long)revision << 32) | (uint)sampleRate);
        }

        internal void Enqueue(float[] samples, int channels)
        {
            int revision = (int)(Interlocked.Read(ref _format) >> 32);
            // ProcessBuffer can also be driven explicitly while Unity is delivering audio.
            if (Interlocked.CompareExchange(ref _writing, 1, 0) != 0)
            {
                Volatile.Write(ref _reset, 1);
                Volatile.Write(ref _overrun, 1);
                return;
            }

            try
            {
                WriteBlock(samples, channels, revision);
            }
            finally
            {
                Volatile.Write(ref _writing, 0);
            }
        }

        internal bool TryRead(out float peak, out bool overrun, MasterLoudness loudness)
        {
            int end = Volatile.Read(ref _written);
            long format = Interlocked.Read(ref _format);
            int revision = (int)(format >> 32);
            int sampleRate = unchecked((int)format);
            if (_readRevision != revision)
            {
                _filter.Reset();
                loudness?.Reset();
                _readRevision = revision;
            }

            int read = _read;
            bool received = false;
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

                int count = (int)_queue[start + 1];
                if (revision == BitConverter.SingleToInt32Bits(_queue[start + 2]))
                {
                    if (channels < 0)
                    {
                        _filter.Reset();
                        loudness?.Reset();
                        channels = -channels;
                    }

                    maximum = Math.Max(maximum, _filter.Observe(_queue, start + HeaderSize, count, channels));
                    loudness?.Observe(_queue, start + HeaderSize, count, channels, sampleRate);
                    received = true;
                }

                read = unchecked(read + HeaderSize + count);
                Volatile.Write(ref _read, read);
            }

            if (Interlocked.Exchange(ref _overrun, 0) == 1)
            {
                loudness?.Reset();
                peak = 0f;
                overrun = true;
                return false;
            }

            peak = maximum;
            overrun = false;
            return received;
        }

        private void WriteBlock(float[] samples, int channels, int revision)
        {
            if (TruePeakFilter.MaxChannels < channels)
            {
                _gap = true;
                Volatile.Write(ref _overrun, 1);
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
            _queue[start + 2] = BitConverter.Int32BitsToSingle(revision);
            Array.Copy(samples, 0, _queue, start + HeaderSize, count);
            Volatile.Write(ref _written, unchecked(written + padding + HeaderSize + count));
        }
    }
}
