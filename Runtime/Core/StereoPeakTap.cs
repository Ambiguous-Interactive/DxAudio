namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;

    /// <summary>Transfers the largest front-channel sample peaks since the last editor read.</summary>
    internal sealed class StereoPeakTap
    {
        private const long UnreadEmpty = long.MinValue;
        private readonly TruePeakTap _truePeak = new();
        private long _pending = UnreadEmpty;

        private static long Pack(float left, float right)
        {
            return ((long)BitConverter.SingleToInt32Bits(left) << 32) | (uint)BitConverter.SingleToInt32Bits(right);
        }

        private static float Left(long pair)
        {
            return BitConverter.Int32BitsToSingle((int)(pair >> 32));
        }

        private static float Right(long pair)
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)pair));
        }

        internal void RequestTruePeakReset()
        {
            _truePeak.RequestReset();
        }

        internal void Observe(float[] samples, int channels)
        {
            if (channels <= 0 || samples.Length < channels)
            {
                return;
            }

            _truePeak.Enqueue(samples, channels);

            float left = 0f;
            float right = 0f;
            int lastFrame = samples.Length - channels;
            for (int index = 0; index <= lastFrame; index += channels)
            {
                float magnitude = Math.Abs(samples[index]);
                if (left < magnitude && magnitude <= float.MaxValue)
                {
                    left = magnitude;
                }

                if (1 < channels)
                {
                    magnitude = Math.Abs(samples[index + 1]);
                    if (right < magnitude && magnitude <= float.MaxValue)
                    {
                        right = magnitude;
                    }
                }
            }

            if (channels == 1)
            {
                right = left;
            }

            // Clear and publish whole pairs so a read cannot move only one channel to the next interval.
            long previous = Volatile.Read(ref _pending);
            while (true)
            {
                long next = previous == UnreadEmpty ? Pack(left, right) : Pack(Math.Max(left, Left(previous)), Math.Max(right, Right(previous)));
                long observed = Interlocked.CompareExchange(ref _pending, next, previous);
                if (observed == previous)
                {
                    return;
                }

                previous = observed;
            }
        }

        internal bool TryReadTruePeak(out float peak, out bool overrun)
        {
            return _truePeak.TryRead(out peak, out overrun);
        }

        internal bool TryRead(out float left, out float right)
        {
            long pair = Interlocked.Exchange(ref _pending, UnreadEmpty);
            if (pair == UnreadEmpty)
            {
                left = 0f;
                right = 0f;
                return false;
            }

            left = Left(pair);
            right = Right(pair);
            return true;
        }
    }
}
