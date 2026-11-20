namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Threading;

    /// <summary>Owns the audition DSP state on the audio thread. The editor publishes fully prepared revisions.</summary>
    internal sealed class DspAuditionProcessor
    {
        private static readonly Revision CancelledRevision = new(0, Array.Empty<Path>());

        internal int ActiveRevision => Volatile.Read(ref _activeRevision);
        internal int ProcessedBlocks => Volatile.Read(ref _processedBlocks);
        internal int Channels => Volatile.Read(ref _channels);
        internal int UnsupportedChannels => Volatile.Read(ref _unsupportedChannels);
        internal float Peak => Volatile.Read(ref _peak);
        internal float PeakSinceStart => Volatile.Read(ref _peakSinceStart);

        private readonly int _blockFrames;
        private readonly int _maximumChannels;
        private readonly int _crossfadeFrames;
        private readonly int _dipFrames;
        private Revision _pending;
        private Revision _active;
        private Revision _candidate;
        private int _primeFrames;
        private int _transitionFrame;
        private int _activeRevision;
        private int _processedBlocks;
        private int _unsupportedChannels;
        private int _channels;
        private int _dry;
        private float _dryMix;
        private float _monitor = 1f;
        private float _monitorGain;
        private float _peak;
        private float _peakSinceStart;

        internal DspAuditionProcessor(int sampleRate, int blockFrames, int maximumChannels)
        {
            _blockFrames = blockFrames;
            _maximumChannels = maximumChannels;
            _crossfadeFrames = Math.Max(1, sampleRate / 50);
            _dipFrames = Math.Max(1, sampleRate / 200);
        }

        internal void Publish(Revision revision)
        {
            Interlocked.Exchange(ref _pending, revision ?? CancelledRevision);
        }

        internal void SetDry(bool dry)
        {
            Volatile.Write(ref _dry, dry ? 1 : 0);
        }

        internal void SetMonitor(float gain)
        {
            Volatile.Write(ref _monitor, gain);
        }

        internal void Process(float[] data, int channels)
        {
            if (channels <= 0 || _maximumChannels < channels || data.Length % channels != 0)
            {
                Silence(data, channels);
                return;
            }

            if ((_candidate == null || _transitionFrame == 0) && Volatile.Read(ref _pending) != null)
            {
                Revision next = Interlocked.Exchange(ref _pending, null);
                if (next == CancelledRevision)
                {
                    _candidate = null;
                }
                else if (_active == null)
                {
                    SetActive(next);
                    _dryMix = Volatile.Read(ref _dry);
                    _monitorGain = Volatile.Read(ref _monitor);
                }
                else if (next?.For(channels) != null)
                {
                    _candidate = next;
                    _primeFrames = next.For(channels)?.LatencyFrames ?? 0;
                    _transitionFrame = 0;
                }
            }

            Path active = _active?.For(channels);
            Path candidate = _candidate?.For(channels);
            if (active == null)
            {
                Silence(data, channels);
                return;
            }

            if (candidate == null)
            {
                _candidate = null;
            }

            Volatile.Write(ref _channels, channels);
            Volatile.Write(ref _unsupportedChannels, 0);
            float peak = 0f;
            int blockSamples = _blockFrames * channels;
            int dry = Volatile.Read(ref _dry);
            float monitor = Volatile.Read(ref _monitor);
            float step = 1f / _dipFrames;
            for (int offset = 0; offset < data.Length; offset += blockSamples)
            {
                int count = Math.Min(blockSamples, data.Length - offset);
                active.Render(data, offset, count, channels);
                candidate?.Render(data, offset, count, channels);
                for (int sample = 0; sample < count; sample += channels)
                {
                    float oldGain = 1f;
                    float newGain = 0f;
                    if (candidate != null)
                    {
                        Transition(active, candidate, out oldGain, out newGain);
                    }

                    for (int channel = 0; channel < channels; ++channel)
                    {
                        int index = sample + channel;
                        float output = active.Sample(index, _dryMix) * oldGain;
                        if (candidate != null)
                        {
                            output += candidate.Sample(index, _dryMix) * newGain;
                        }

                        data[offset + index] = output * _monitorGain;
                        peak = Math.Max(peak, Math.Abs(output));
                    }

                    _dryMix = dry == 1 ? Math.Min(1f, _dryMix + step) : Math.Max(0f, _dryMix - step);
                    _monitorGain = _monitorGain < monitor ? Math.Min(monitor, _monitorGain + step) : Math.Max(monitor, _monitorGain - step);
                }

                int transitionLength = active.LatencyFrames == candidate?.LatencyFrames ? _crossfadeFrames : _dipFrames * 2;
                if (candidate != null && transitionLength <= _transitionFrame)
                {
                    SetActive(_candidate);
                    _candidate = null;
                    active = candidate;
                    candidate = null;
                }
            }

            Volatile.Write(ref _peak, peak);
            if (_peakSinceStart < peak)
            {
                Volatile.Write(ref _peakSinceStart, peak);
            }
            Volatile.Write(ref _processedBlocks, _processedBlocks + 1);
        }

        private void Transition(Path active, Path candidate, out float oldGain, out float newGain)
        {
            if (0 < _primeFrames)
            {
                --_primeFrames;
                oldGain = 1f;
                newGain = 0f;
                return;
            }

            if (active.LatencyFrames == candidate.LatencyFrames)
            {
                newGain = Math.Min(1f, (float)_transitionFrame / _crossfadeFrames);
                oldGain = 1f - newGain;
            }
            else
            {
                oldGain = Math.Max(0f, 1f - (float)_transitionFrame / _dipFrames);
                newGain = AudioMath.Clamp((float)(_transitionFrame - _dipFrames) / _dipFrames, 0f, 1f);
            }

            ++_transitionFrame;
        }

        private void SetActive(Revision revision)
        {
            _active = revision;
            Volatile.Write(ref _activeRevision, revision.Number);
        }

        private void Silence(float[] data, int channels)
        {
            Array.Clear(data, 0, data.Length);
            Volatile.Write(ref _unsupportedChannels, channels);
            Volatile.Write(ref _peak, 0f);
        }

        internal sealed class Revision
        {
            internal int Number { get; }
            private readonly Path[] _shapes;

            internal Revision(int number, Path[] shapes)
            {
                Number = number;
                _shapes = shapes;
            }

            internal Path For(int channels)
            {
                return 0 < channels && channels <= _shapes.Length ? _shapes[channels - 1] : null;
            }
        }

        internal sealed class Path
        {
            internal int LatencyFrames { get; }
            private readonly DspChain _chain;
            private readonly DspGraph _graph;
            private readonly DspFilter _lowPass;
            private readonly float[] _wet;
            private readonly float[] _dry;
            private readonly float[] _delay;
            private int _delayCursor;

            internal Path(int blockFrames, int channels, DspChain chain, DspGraph graph, DspFilter lowPass = null)
            {
                _chain = chain;
                _graph = graph;
                _lowPass = lowPass;
                LatencyFrames = (graph?.LatencySamples ?? 0) + (chain?.LatencySamples ?? 0);
                _wet = new float[blockFrames * channels];
                _dry = new float[_wet.Length];
                _delay = new float[LatencyFrames * channels];
            }

            internal void Render(float[] input, int offset, int count, int channels)
            {
                Array.Copy(input, offset, _wet, 0, count);
                float[] source = input;
                int start = offset;
                if (_lowPass != null)
                {
                    // Dry bypasses the cue effects while retaining the resolved playback variation.
                    _lowPass.Process(_wet, 0, count, channels);
                    source = _wet;
                    start = 0;
                }
                if (_delay.Length == 0)
                {
                    Array.Copy(source, start, _dry, 0, count);
                }
                else
                {
                    for (int index = 0; index < count; ++index)
                    {
                        _dry[index] = _delay[_delayCursor];
                        _delay[_delayCursor] = source[start + index];
                        ++_delayCursor;
                        if (_delay.Length == _delayCursor)
                        {
                            _delayCursor = 0;
                        }
                    }
                }

                _chain?.Process(_wet, 0, count, channels);
                _graph?.Process(_wet, 0, count, channels);
            }

            internal float Sample(int index, float dryMix)
            {
                return _wet[index] * (1f - dryMix) + _dry[index] * dryMix;
            }
        }
    }
}
