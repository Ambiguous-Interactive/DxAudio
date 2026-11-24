#if UNITY_EDITOR
namespace WallstopStudios.DxAudio
{
    using System;
    using System.Threading;
    using UnityEngine;

    [DisallowMultipleComponent]
    internal sealed class MusicMeterTap : MonoBehaviour
    {
        private const double SignalTimeoutSeconds = 0.25d;

        private SampleWindow _window;
        private int _readers;

        internal static MusicMeterTap Attach(AudioSource source, int sampleCount)
        {
            if (!source.TryGetComponent(out MusicMeterTap tap))
            {
                tap = source.gameObject.AddComponent<MusicMeterTap>();
            }

            if (tap._readers == 0)
            {
                Volatile.Write(ref tap._window, new SampleWindow(sampleCount));
                tap.enabled = true;
            }
            ++tap._readers;
            return tap;
        }

        internal void Detach()
        {
            --_readers;
            if (_readers == 0)
            {
                enabled = false;
                Volatile.Write(ref _window, null);
            }
        }

        internal bool TryRead(float[] left, float[] right, double now)
        {
            SampleWindow window = Volatile.Read(ref _window);
            return window != null && window.TryRead(left, right, now);
        }

        private void OnAudioFilterRead(float[] samples, int channels)
        {
            SampleWindow window = Volatile.Read(ref _window);
            if (window != null && 0 < channels)
            {
                window.Observe(samples, channels, AudioSettings.dspTime);
            }
        }

        private sealed class SampleWindow
        {
            private readonly float[] _left;
            private readonly float[] _right;
            private int _busy;
            private int _next;
            private int _count;
            private double _observedAt;

            internal SampleWindow(int sampleCount)
            {
                _left = new float[sampleCount];
                _right = new float[sampleCount];
            }

            internal void Observe(float[] samples, int channels, double now)
            {
                // Skip a contended window. The audio callback must never wait for the editor.
                if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                {
                    return;
                }

                try
                {
                    int size = _left.Length;
                    int frames = samples.Length / channels;
                    int first = Math.Max(0, frames - size) * channels;
                    int end = frames * channels;
                    int rightChannel = channels == 1 ? 0 : 1;
                    for (int index = first; index < end; index += channels)
                    {
                        _left[_next] = samples[index];
                        _right[_next] = samples[index + rightChannel];
                        if (++_next == size)
                        {
                            _next = 0;
                        }
                    }
                    _count = Math.Min(size, _count + frames);
                    _observedAt = now;
                }
                finally
                {
                    Volatile.Write(ref _busy, 0);
                }
            }

            internal bool TryRead(float[] left, float[] right, double now)
            {
                if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                {
                    return false;
                }

                try
                {
                    int size = _left.Length;
                    if (_count < size || SignalTimeoutSeconds < now - _observedAt)
                    {
                        return false;
                    }

                    int tail = size - _next;
                    Array.Copy(_left, _next, left, 0, tail);
                    Array.Copy(_right, _next, right, 0, tail);
                    Array.Copy(_left, 0, left, tail, _next);
                    Array.Copy(_right, 0, right, tail, _next);
                    return true;
                }
                finally
                {
                    Volatile.Write(ref _busy, 0);
                }
            }
        }
    }
}
#endif
