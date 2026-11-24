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
            set
            {
                long state = Volatile.Read(ref _audition);
                while (true)
                {
                    long observed = Interlocked.CompareExchange(ref _audition, Pack(Left(state), value), state);
                    if (observed == state)
                    {
                        return;
                    }

                    state = observed;
                }
            }
        }

        private readonly ListenerMeterTap _listener;
        private long _pending = UnreadEmpty;

        // Publish the claimed gain and editor target together so a callback cannot spend the same ramp twice.
        private long _audition = Pack(1f, 1f);

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

        private static float RampGain(float start, float target, float step, int frames)
        {
            float gain = start + step * frames;
            return step < 0f ? Math.Max(gain, target) : Math.Min(gain, target);
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
            long state = Volatile.Read(ref _audition);
            float start = Left(state);
            float target = Right(state);
            if (target == 1f && start == 1f)
            {
                return;
            }

            int frames = samples.Length / channels;
            int end = frames * channels;
            int index = 0;
            if (start != target)
            {
                float step = ClaimRamp(frames, state, ref start, ref target);
                for (int frame = 1; frame <= frames && step != 0f; ++frame)
                {
                    float gain = RampGain(start, target, step, frame);
                    if (gain == target)
                    {
                        break;
                    }

                    int nextFrame = index + channels;
                    for (; index < nextFrame; ++index)
                    {
                        samples[index] *= gain;
                    }
                }
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

        private float ClaimRamp(int frames, long state, ref float start, ref float target)
        {
            float step = 1f / Math.Max(1f, _listener.SampleRate * RampSeconds);
            while (start != target)
            {
                float signedStep = start < target ? step : -step;
                float end = RampGain(start, target, signedStep, frames);
                long observed = Interlocked.CompareExchange(ref _audition, Pack(end, target), state);
                if (observed == state)
                {
                    return signedStep;
                }

                state = observed;
                start = Left(state);
                target = Right(state);
            }

            return 0f;
        }
    }
}
