namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;

    /// <summary>Runs a per-play filter with audio-thread-owned state for each supported channel shape.</summary>
    /// <remarks>
    /// A 32-bit seqlock publishes the cutoff and its reset generation as three ints. <c>Interlocked</c>
    /// on a <c>long</c> is atomic on a 32-bit player but takes a process-global lock, and the audio
    /// callback must never wait on one. The writer is the main thread at cue start; the reader retries
    /// only while those three stores are in flight, then falls back to the latest whole cutoff word and
    /// the last generation so a writer preempted on a single-core device cannot stall the callback. The
    /// cutoff word is stored before the sequence marker, so only a reader that runs before the first store
    /// can see a dry block.
    /// </remarks>
    internal sealed class SfxLowPass
    {
        private const int MaximumReadAttempts = 8;

        private readonly DspFilter[] _filters;
        private readonly long[] _applied;
        private int _sequence;
        private int _generation;
        private int _cutoffBits;
        private int _readGeneration;

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
            // Store the cutoff word before the sequence marker, so a reader that runs while this writer
            // is preempted still sees the requested cutoff instead of a dry block. The generation then
            // publishes the reset under the seqlock, even when the next play reuses the cutoff.
            Volatile.Write(ref _cutoffBits, BitConverter.SingleToInt32Bits(cutoffHertz));
            int sequence = Volatile.Read(ref _sequence);
            Volatile.Write(ref _sequence, sequence + 1);
            Volatile.Write(ref _generation, unchecked(++_generation));
            Volatile.Write(ref _sequence, sequence + 2);
        }

        internal void Process(float[] buffer, int channels)
        {
            int cutoffBits = 0;
            int generation = 0;
            bool consistent = false;
            for (int attempt = 0; attempt < MaximumReadAttempts; ++attempt)
            {
                int sequence = Volatile.Read(ref _sequence);
                if ((sequence & 1) != 0)
                {
                    continue;
                }

                cutoffBits = Volatile.Read(ref _cutoffBits);
                generation = Volatile.Read(ref _generation);
                if (sequence == Volatile.Read(ref _sequence))
                {
                    _readGeneration = generation;
                    consistent = true;
                    break;
                }
            }

            if (!consistent)
            {
                // A preempted writer may have stored the cutoff before its generation. The cutoff is a
                // whole 32-bit value, so use the latest one with the last whole generation; the next
                // consistent read supplies the matching generation and any reset.
                cutoffBits = Volatile.Read(ref _cutoffBits);
                generation = _readGeneration;
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
