namespace WallstopStudios.DxAudio
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Runs a prepared chain on every block Unity hands this object's source.</summary>
    /// <remarks>
    /// Unity calls <see cref="OnAudioFilterRead" /> on the audio thread for each source on this object, and
    /// <see cref="ProcessBuffer" /> is that same path, so a test can drive a synthetic block with no audio device.
    ///
    /// A chain keeps delay lines and envelopes, so it belongs to the voice that plays. Every chain asset this host has
    /// met stays prepared for each block shape up to the output layout, because a filter block can arrive narrower than
    /// the device: choosing one again allocates nothing, and <see cref="Chain" />'s setter clears the voice's chain and
    /// the bus, because a new play must not inherit the last one's delay line or gain.
    ///
    /// The bus runs after the voice's own chain. Unity gives a script no way to sum one source into another, so a
    /// player's bus runs on each voice rather than on the mixed output: a limiter or a distortion on it behaves as the
    /// bus, while a compressor works on each voice alone.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("DxAudio/Audio Filter Host")]
    public sealed class AudioFilterHost : MonoBehaviour
    {
        private const int FallbackSampleRate = 48_000;
        private const int FallbackBlockSize = 1_024;
        private const int FallbackChannels = 2;

        [SerializeField]
        [Tooltip("The chain this host runs on its source, when a game drives the host itself. A player sets this per play.")]
        private DspChainAsset _chain;

        [SerializeField]
        [Tooltip("A chain this host runs after its own, so a player's bus reaches every voice.")]
        private DspChainAsset _busChain;

        private readonly List<Instance> _instances = new();
        private Instance _running;
        private Instance _bus;
        private int _sampleRate;
        private int _blockSize = FallbackBlockSize;
        private int _outputChannels;
        private int _liveChannels;
        private int _skippedBlocks;

        /// <summary>The chain this host runs, or null when it passes the block through.</summary>
        /// <remarks>Assigning starts a new play: the voice's chain and the bus both lose the last play's state.</remarks>
        public DspChainAsset Chain
        {
            get => _chain;
            set
            {
                _chain = value;
                _running = InstanceFor(value, bus: false);
                ClearState();
            }
        }

        /// <summary>The chain run after <see cref="Chain" />, or null.</summary>
        public DspChainAsset BusChain
        {
            get => _busChain;
            set
            {
                _busChain = value;
                _bus = InstanceFor(value, bus: true);
                if (_bus != null)
                {
                    _bus.Reset();
                }
            }
        }

        /// <summary>The chain running now, for a test or an inspector reading a stage. Null is a pass-through.</summary>
        public DspChain LiveChain => _running != null ? _running.For(ReadingChannels) : null;

        /// <summary>The bus running now. Null is a pass-through.</summary>
        public DspChain LiveBusChain => _bus != null ? _bus.For(ReadingChannels) : null;

        /// <summary>The delay the running chains add together, in samples.</summary>
        public int LatencySamples
        {
            get
            {
                int latency = 0;
                DspChain running = LiveChain;
                if (running != null)
                {
                    latency += running.LatencySamples;
                }

                DspChain bus = LiveBusChain;
                if (bus != null)
                {
                    latency += bus.LatencySamples;
                }

                return latency;
            }
        }

        /// <summary>How far the running chains hold the signal down now, in decibels; zero is untouched.</summary>
        public float GainReductionDecibels
        {
            get
            {
                float held = LiveChain != null ? LiveChain.GainReductionDecibels : 0f;
                if (LiveBusChain != null)
                {
                    held += LiveBusChain.GainReductionDecibels;
                }

                return held;
            }
        }

        /// <summary>The output channel count this host prepares for; every narrower block also runs.</summary>
        public int ChannelCount => _outputChannels;

        /// <summary>How many blocks were narrower, or wider, than any prepared chain. A diagnostic; zero is healthy.</summary>
        public int SkippedBlocks => _skippedBlocks;

        /// <summary>Prepares <paramref name="asset" />'s chain for this voice now, so the first play of it allocates nothing.</summary>
        public void Warm(DspChainAsset asset)
        {
            _ = InstanceFor(asset, bus: false);
        }

        /// <summary>Whether a chain for <paramref name="asset" /> is already prepared here, so it cannot allocate.</summary>
        internal bool IsPrepared(DspChainAsset asset)
        {
            if (asset == null)
            {
                return false;
            }

            for (int index = 0; index < _instances.Count; index++)
            {
                if (_instances[index].asset == asset)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Runs the block the engine hands this host. Unity calls it on the audio thread.</summary>
        internal void OnAudioFilterRead(float[] data, int channels)
        {
            ProcessBuffer(data, channels);
        }

        /// <summary>Runs <paramref name="buffer" /> through the chains in order, in place.</summary>
        /// <remarks>
        /// This is the entry point a test drives: it is what <see cref="OnAudioFilterRead" /> calls. A block no prepared
        /// chain matches passes through and counts in <see cref="SkippedBlocks" />, the same rule an effect keeps for a
        /// block of another shape.
        /// </remarks>
        public void ProcessBuffer(float[] buffer, int channels)
        {
            if (buffer == null)
            {
                return;
            }

            DspChain running = _running != null ? _running.For(channels) : null;
            DspChain bus = _bus != null ? _bus.For(channels) : null;
            if (running == null && bus == null)
            {
                if (_running != null || _bus != null)
                {
                    _skippedBlocks++;
                }

                return;
            }

            _liveChannels = channels;
            if (running != null)
            {
                running.Process(buffer, 0, buffer.Length, channels);
            }

            if (bus != null)
            {
                bus.Process(buffer, 0, buffer.Length, channels);
            }
        }

        private void Awake()
        {
            EnsurePrepared();
            Chain = _chain;
            BusChain = _busChain;
        }

        /// <summary>Reads the output rate, block size and channel count every chain on this host is prepared for.</summary>
        /// <remarks>
        /// Read once. A voice added before its player sets a chain is prepared from the device, and a player that
        /// already set one is not prepared again, so a late device query never invalidates a prepared chain.
        /// </remarks>
        private void EnsurePrepared()
        {
            if (0 < _sampleRate)
            {
                return;
            }

            _sampleRate = 0 < AudioSettings.outputSampleRate ? AudioSettings.outputSampleRate : FallbackSampleRate;
            _outputChannels = ChannelCountOf(AudioSettings.speakerMode);
            AudioSettings.GetDSPBufferSize(out int blockSize, out _);
            _blockSize = 0 < blockSize ? blockSize : FallbackBlockSize;
        }

        /// <summary>The block shape a reading describes: the last block run, or the output layout before any block.</summary>
        private int ReadingChannels => 0 < _liveChannels ? _liveChannels : _outputChannels;

        private void ClearState()
        {
            if (_running != null)
            {
                _running.Reset();
            }

            if (_bus != null)
            {
                _bus.Reset();
            }
        }

        /// <summary>The prepared chains for <paramref name="asset" /> in one slot, built the first time this voice meets it.</summary>
        private Instance InstanceFor(DspChainAsset asset, bool bus)
        {
            if (asset == null)
            {
                return null;
            }

            for (int index = 0; index < _instances.Count; index++)
            {
                Instance met = _instances[index];
                if (met.bus == bus && met.asset == asset)
                {
                    return met;
                }
            }

            EnsurePrepared();
            Instance created = new(asset, bus);
            created.Prepare(_sampleRate, _blockSize, _outputChannels);
            _instances.Add(created);
            return created;
        }

        private static int ChannelCountOf(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono:
                    return 1;
                case AudioSpeakerMode.Quad:
                    return 4;
                case AudioSpeakerMode.Surround:
                    return 5;
                case AudioSpeakerMode.Mode5point1:
                    return 6;
                case AudioSpeakerMode.Mode7point1:
                    return 8;
                default:
                    // Stereo, Prologic, and an uninitialised device all render two channels, which is what a 2D
                    // source is mixed into.
                    return FallbackChannels;
            }
        }

        /// <summary>One asset's chains in one slot, one per block shape the output layout can produce.</summary>
        /// <remarks>
        /// Narrower shapes are prepared too, because a source's filter block can carry fewer channels than the device:
        /// a chain prepared only for the output layout would pass a mono block through and the cue would play
        /// unshaped, which is the one failure that is invisible by ear until the mix is compared.
        /// </remarks>
        private sealed class Instance
        {
            public readonly DspChainAsset asset;
            public readonly bool bus;
            private DspChain[] _chains = Array.Empty<DspChain>();

            public Instance(DspChainAsset asset, bool bus)
            {
                this.asset = asset;
                this.bus = bus;
            }

            public void Prepare(int sampleRate, int blockSize, int shapes)
            {
                _chains = new DspChain[shapes];
                for (int index = 0; index < shapes; index++)
                {
                    DspChain chain = asset.CreateChain();
                    chain.Prepare(sampleRate, blockSize, index + 1);
                    _chains[index] = chain;
                }
            }

            /// <summary>The chain prepared for <paramref name="channels" />, or null when that shape is wider than the layout.</summary>
            public DspChain For(int channels)
            {
                if (channels <= 0 || _chains.Length < channels)
                {
                    return null;
                }

                return _chains[channels - 1];
            }

            public void Reset()
            {
                for (int index = 0; index < _chains.Length; index++)
                {
                    _chains[index].Reset();
                }
            }
        }
    }
}
