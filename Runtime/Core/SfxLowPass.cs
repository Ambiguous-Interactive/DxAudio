namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;

    /// <summary>Runs a per-play filter with audio-thread-owned state for each supported channel shape.</summary>
    internal sealed class SfxLowPass
    {
        private readonly DspFilter[] _filters;
        private readonly long[] _applied;
        private long _request;
        private int _generation;

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
            // One atomic request carries both the cutoff and the reset, even when the next play uses the same cutoff.
            long generation = (long)unchecked(++_generation) << 32;
            Interlocked.Exchange(ref _request, generation | (uint)BitConverter.SingleToInt32Bits(cutoffHertz));
        }

        internal void Process(float[] buffer, int channels)
        {
            long request = Interlocked.Read(ref _request);
            float cutoff = BitConverter.Int32BitsToSingle(unchecked((int)request));
            if (cutoff <= 0f || channels <= 0 || _filters.Length < channels)
            {
                return;
            }

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
