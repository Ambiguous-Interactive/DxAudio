namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;

    /// <summary>Runs a per-play filter with audio-thread-owned state for each supported channel shape.</summary>
    /// <remarks>
    /// A 32-bit seqlock publishes the cutoff and its reset generation as three ints. <c>Interlocked</c>
    /// on a <c>long</c> is atomic on a 32-bit player but takes a process-global lock, and the audio
    /// callback must never wait on one. The writer is the main thread at cue start; the reader retries
    /// only while those three stores are in flight.
    /// </remarks>
    internal sealed class SfxLowPass
    {
        private readonly DspFilter[] _filters;
        private readonly long[] _applied;
        private int _sequence;
        private int _generation;
        private int _cutoffBits;

        internal SfxLowPass(int sampleRate, int blockFrames, int maximumChannels)
        {
            _filters = new DspFilter[maximumChannels];
            _applied = new long[maximumChannels];
            for (int index = 0; index < maximumChannels; ++index)
            {
                DspFilter filter = new();
                _ = filter.TryPrepare(sampleRate, blockFrames, index + 1);
                _filters[index] = filter;
            }
        }

        internal void Begin(float cutoffHertz)
        {
            // Publish the cutoff and the reset generation together, even when the next play reuses the cutoff.
            int sequence = Volatile.Read(ref _sequence);
            Volatile.Write(ref _sequence, sequence + 1);
            Volatile.Write(ref _cutoffBits, BitConverter.SingleToInt32Bits(cutoffHertz));
            Volatile.Write(ref _generation, unchecked(++_generation));
            Volatile.Write(ref _sequence, sequence + 2);
        }

        internal void Process(float[] buffer, int channels)
        {
            int sequence;
            int cutoffBits;
            int generation;
            while (true)
            {
                sequence = Volatile.Read(ref _sequence);
                if ((sequence & 1) != 0)
                {
                    continue;
                }

                cutoffBits = Volatile.Read(ref _cutoffBits);
                generation = Volatile.Read(ref _generation);
                if (sequence == Volatile.Read(ref _sequence))
                {
                    break;
                }
            }

            float cutoff = BitConverter.Int32BitsToSingle(cutoffBits);
            if (cutoff <= 0f || channels <= 0 || _filters.Length < channels)
            {
                return;
            }

            long request = ((long)generation << 32) | (uint)cutoffBits;
            int index = channels - 1;
            DspFilter filter = _filters[index];
            if (_applied[index] != request)
            {
                filter.FrequencyHz = cutoff;
                filter.Reset();
                _applied[index] = request;
            }

            filter.Process(buffer, 0, buffer.Length, channels);
        }
    }
}
