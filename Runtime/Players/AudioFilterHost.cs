namespace WallstopStudios.DxAudio
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Runs a prepared chain on every block Unity hands this object's source.</summary>
    /// <remarks>
    /// Unity calls <see cref="OnAudioFilterRead" /> on the audio thread for each source on this object, and
    /// <see cref="ProcessBuffer" /> is that same path, so a test can drive a synthetic block with no audio device.
    ///
    /// A chain keeps delay lines and envelopes, so it belongs to the voice that plays. Every chain asset this host has
    /// met stays prepared for this voice's output rate and channel count: choosing one again allocates nothing, and
    /// <see cref="Chain" />'s setter clears its state, because the voice is about to start a different cue.
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

        private readonly List<DspChainAsset> _met = new();
        private readonly List<DspChain> _prepared = new();
        private DspChain _running;
        private DspChain _bus;
        private int _sampleRate;
        private int _channels;
        private int _blockSize = FallbackBlockSize;

        /// <summary>The chain this host runs, or null when it passes the block through.</summary>
        /// <remarks>Assigning clears the chain's state: the voice is starting something new, not continuing.</remarks>
        public DspChainAsset Chain
        {
            get => _chain;
            set
            {
                _chain = value;
                _running = PreparedFor(value);
                if (_running != null)
                {
                    _running.Reset();
                }
            }
        }

        /// <summary>The chain run after <see cref="Chain" />, or null.</summary>
        public DspChainAsset BusChain
        {
            get => _busChain;
            set
            {
                _busChain = value;
                _bus = PreparedFor(value);
            }
        }

        /// <summary>The chain running now, for a test or an inspector reading a stage. Null is a pass-through.</summary>
        public DspChain LiveChain => _running;

        /// <summary>The bus running now. Null is a pass-through.</summary>
        public DspChain LiveBusChain => _bus;

        /// <summary>The delay the running chains add together, in samples.</summary>
        public int LatencySamples
        {
            get
            {
                int latency = 0;
                if (_running != null)
                {
                    latency += _running.LatencySamples;
                }

                if (_bus != null)
                {
                    latency += _bus.LatencySamples;
                }

                return latency;
            }
        }

        /// <summary>How far the running chains hold the signal down now, in decibels; zero is untouched.</summary>
        public float GainReductionDecibels
        {
            get
            {
                float most = _running != null ? _running.GainReductionDecibels : 0f;
                if (_bus != null && _bus.GainReductionDecibels < most)
                {
                    most = _bus.GainReductionDecibels;
                }

                return most;
            }
        }

        /// <summary>The rate and channel count every chain here is prepared for.</summary>
        public int ChannelCount => _channels;

        /// <summary>Prepares <paramref name="asset" />'s chain for this voice now, so the first play of it allocates nothing.</summary>
        public void Warm(DspChainAsset asset)
        {
            _ = PreparedFor(asset);
        }

        /// <summary>Runs the block the engine hands this host. Unity calls it on the audio thread.</summary>
        /// <remarks>A block whose channel count differs from the prepared one passes through, the same rule an effect keeps.</remarks>
        internal void OnAudioFilterRead(float[] data, int channels)
        {
            ProcessBuffer(data, channels);
        }

        /// <summary>Runs <paramref name="buffer" /> through the chains in order, in place.</summary>
        /// <remarks>This is the entry point a test drives: it is what <see cref="OnAudioFilterRead" /> calls.</remarks>
        public void ProcessBuffer(float[] buffer, int channels)
        {
            if (buffer == null || channels != _channels)
            {
                return;
            }

            if (_running != null)
            {
                _running.Process(buffer, 0, buffer.Length, channels);
            }

            if (_bus != null)
            {
                _bus.Process(buffer, 0, buffer.Length, channels);
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
            _channels = ChannelCountOf(AudioSettings.speakerMode);
            AudioSettings.GetDSPBufferSize(out int blockSize, out _);
            _blockSize = 0 < blockSize ? blockSize : FallbackBlockSize;
        }

        /// <summary>The chain instance for <paramref name="asset" />, built and prepared the first time this voice meets it.</summary>
        private DspChain PreparedFor(DspChainAsset asset)
        {
            if (asset == null)
            {
                return null;
            }

            for (int index = 0; index < _met.Count; index++)
            {
                if (_met[index] == asset)
                {
                    return _prepared[index];
                }
            }

            EnsurePrepared();

            DspChain chain = asset.CreateChain();
            chain.Prepare(_sampleRate, _blockSize, _channels);
            _met.Add(asset);
            _prepared.Add(chain);
            return chain;
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
    }
}
