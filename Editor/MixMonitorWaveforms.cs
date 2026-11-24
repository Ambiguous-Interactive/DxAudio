namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class MixMonitorWaveforms
    {
        private const int MaxPeaks = 512;
        private const int MaxSamples = 2_000_000;
        private const int ReadFrames = 4096;
        private const int Capacity = 64;

        private readonly Dictionary<AudioClip, float[]> _cache = new(Capacity);
        private readonly AudioClip[] _order = new AudioClip[Capacity];
        private float[] _samples = Array.Empty<float>();
        private int _next;

        internal static float Peak(float[] peaks, float from, float to)
        {
            int first = Mathf.FloorToInt(from * peaks.Length);
            int last = Mathf.Max(first, Mathf.CeilToInt(to * peaks.Length) - 1);
            float peak = 0f;
            for (int bin = first; bin <= last; ++bin)
            {
                peak = Mathf.Max(peak, peaks[bin % peaks.Length]);
            }

            return Mathf.Clamp01(peak);
        }

        internal void Clear()
        {
            _cache.Clear();
            Array.Clear(_order, 0, _order.Length);
            _next = 0;
        }

        internal float[] Observe(AudioClip clip, ref bool canRead)
        {
            if (clip == null)
            {
                return null;
            }

            if (_cache.TryGetValue(clip, out float[] peaks))
            {
                return peaks;
            }

            if (!canRead || clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.loadState != AudioDataLoadState.Loaded)
            {
                return null;
            }

            int frames = clip.samples;
            int channels = clip.channels;
            if (frames <= 0 || channels <= 0 || MaxSamples < (long)frames * channels)
            {
                return null;
            }

            canRead = false;
            peaks = Read(clip, frames, channels);
            AudioClip oldest = _order[_next];
            if (!ReferenceEquals(oldest, null))
            {
                _cache.Remove(oldest);
            }

            _cache.Add(clip, peaks);
            _order[_next] = clip;
            _next = (_next + 1) % Capacity;
            return peaks;
        }

        private float[] Read(AudioClip clip, int frames, int channels)
        {
            int readFrames = Math.Min(ReadFrames, frames);
            int bufferSize = readFrames * channels;
            if (_samples.Length != bufferSize)
            {
                _samples = new float[bufferSize];
            }

            float[] peaks = new float[Math.Min(MaxPeaks, frames)];
            for (int offset = 0; offset < frames; offset += readFrames)
            {
                if (!clip.GetData(_samples, offset))
                {
                    return null;
                }

                // Unity wraps the final read to the beginning. Only keep frames before the end.
                int count = Math.Min(readFrames, frames - offset);
                for (int frame = 0; frame < count; ++frame)
                {
                    int peak = (offset + frame) * peaks.Length / frames;
                    int first = frame * channels;
                    for (int sample = first; sample < first + channels; ++sample)
                    {
                        peaks[peak] = Math.Max(peaks[peak], Math.Abs(_samples[sample]));
                    }
                }
            }

            return peaks;
        }
    }
}
