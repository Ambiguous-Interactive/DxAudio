namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;

    /// <summary>Applies editor audition gain and captures the resulting listener samples.</summary>
    internal sealed class StereoPeakTap
    {
        private const long UnreadEmpty = long.MinValue;
        private const float RampSeconds = 0.005f;

        internal float AuditionGain
        {
            set => _auditionGain = value;
        }

        private readonly ListenerMeterTap _listener;
        private long _pending = UnreadEmpty;

        // The editor publishes the target. The listener callback owns the ramp.
        private volatile float _auditionGain = 1f;
        private float _appliedGain = 1f;

        internal StereoPeakTap(int sampleRate)
        {
            _listener = new ListenerMeterTap(sampleRate);
        }

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

        internal void SetSampleRate(int sampleRate)
        {
            _listener.SetSampleRate(sampleRate);
        }

        internal void Observe(float[] samples, int channels)
        {
            if (channels <= 0 || samples.Length < channels)
            {
                return;
            }

            ApplyAudition(samples, channels);
            _listener.Enqueue(samples, channels);

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

        // The editor reads both meters in one drain of the listener samples.
        internal bool TryReadTruePeak(out float peak, out bool overrun, MasterLoudness loudness = null)
        {
            return _listener.TryRead(out peak, out overrun, loudness);
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

        private void ApplyAudition(float[] samples, int channels)
        {
            float target = _auditionGain;
            float gain = _appliedGain;
            if (target == 1f && gain == 1f)
            {
                return;
            }

            int length = samples.Length;
            int end = length - length % channels;
            int index = 0;
            if (gain != target)
            {
                float step = 1f / Math.Max(1f, _listener.SampleRate * RampSeconds);
                while (index < end && gain != target)
                {
                    gain = gain < target ? Math.Min(gain + step, target) : Math.Max(gain - step, target);
                    int nextFrame = index + channels;
                    for (; index < nextFrame; ++index)
                    {
                        samples[index] *= gain;
                    }
                }

                _appliedGain = gain;
            }

            if (target == 0f)
            {
                Array.Clear(samples, index, end - index);
            }
            else if (target != 1f)
            {
                for (; index < end; ++index)
                {
                    samples[index] *= target;
                }
            }
        }
    }
}
