namespace WallstopStudios.DxAudio
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Runs a chain on the final mix of every source, on the object that carries the AudioListener.</summary>
    /// <remarks>
    /// Unity sums every source before it hands a block to a filter on the listener's object, so a chain here acts on
    /// the mix rather than on one voice. That is the only stage in this package that sees the sum: the bus on
    /// <see cref="AudioFilterHost" /> runs once per voice, so two voices that each fit under a ceiling can still sum
    /// above it, while a mix bus holds the ceiling of the sum.
    ///
    /// A mix bus adds no buffering of its own. It shapes the block Unity gives it in place, so the only delay is the
    /// one its chain reports, and only effects with a lookahead or a window report any.
    ///
    /// A mix bus shapes every source in the scene, including ones this package does not play. That is what makes it a
    /// master stage; use the per-voice bus for shaping one player.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wallstop Studios/DxAudio/Audio Mix Bus")]
    public sealed class AudioMixBus : MonoBehaviour
    {
        /// <summary>The chain this bus runs, or null when it passes the mix through.</summary>
        /// <remarks>
        /// Assigning another asset selects that asset's prepared chain and clears it at the next block, never here: the
        /// audio thread may be inside a block on the old chain. Assigning the asset that already runs does nothing, so
        /// a game can set the same bus every scene without restarting a compressor's release.
        /// </remarks>
        public DspChainAsset Chain
        {
            get => _chain;
            set
            {
                if (_chain == value)
                {
                    return;
                }

                _chain = value;
                Select(value);
            }
        }

        /// <summary>The chain running now, for a test or an inspector reading a stage. Null is a pass-through.</summary>
        public DspChain LiveChain => _running != null ? _running.For(ReadingChannels) : null;

        /// <summary>The delay the running chain adds, in samples.</summary>
        public int LatencySamples
        {
            get
            {
                DspChain chain = LiveChain;
                return chain != null ? chain.LatencySamples : 0;
            }
        }

        /// <summary>How far the running chain holds the mix down now, in decibels; zero is untouched.</summary>
        public float GainReductionDecibels
        {
            get
            {
                DspChain chain = LiveChain;
                return chain != null ? chain.GainReductionDecibels : 0f;
            }
        }

        /// <summary>The output channel count this bus prepares for; every narrower block also runs.</summary>
        public int ChannelCount => _layout.Channels;

        /// <summary>How many blocks no prepared chain ran. A diagnostic; zero is healthy.</summary>
        public int SkippedBlocks => _skippedBlocks;

        /// <summary>Whether this object also carries the AudioListener a mix bus needs to receive the mix.</summary>
        public bool IsOnListener => TryGetComponent<AudioListener>(out _);

        /// <summary>The sample rate every slot here was prepared with, for a test that checks a device change.</summary>
        internal int PreparedSampleRate => _layout.SampleRate;

        /// <summary>The block shape a reading describes: the last block run, or the output layout before any block.</summary>
        private int ReadingChannels => 0 < _liveChannels ? _liveChannels : _layout.Channels;

        [SerializeField]
        [Tooltip("The chain this bus runs on the final mix, after Unity sums every source. Attach this object to the AudioListener.")]
        private DspChainAsset _chain;

        private readonly AudioLayout _layout = new();
        private readonly List<DspChainSlot> _slots = new();

        // Written on the main thread, read on the audio thread. Volatile makes a swap visible to the next block.
        private volatile DspChainSlot _running;
        private int _liveChannels;
        private int _skippedBlocks;

        /// <summary>Prepares <paramref name="asset" />'s chain now, so the first block that meets it allocates nothing.</summary>
        public void Warm(DspChainAsset asset)
        {
            _ = SlotFor(asset);
        }

        /// <summary>Whether <paramref name="asset" />'s chain is prepared here, so a switch to it cannot allocate.</summary>
        public bool IsPrepared(DspChainAsset asset)
        {
            if (asset == null)
            {
                return false;
            }

            foreach (DspChainSlot slot in _slots)
            {
                if (slot.Asset == asset)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Runs <paramref name="buffer" /> through the chain in place.</summary>
        /// <remarks>
        /// This is the entry point a test drives: it is what <see cref="OnAudioFilterRead" /> calls. A block no prepared
        /// chain matches passes through and counts in <see cref="SkippedBlocks" />, so a bypass is visible at run time.
        /// </remarks>
        public void ProcessBuffer(float[] buffer, int channels)
        {
            if (buffer == null)
            {
                return;
            }

            DspChainSlot slot = _running;
            DspChain chain = slot != null ? slot.For(channels) : null;
            if (chain == null)
            {
                if (slot != null)
                {
                    ++_skippedBlocks;
                }

                return;
            }

            _liveChannels = channels;
            if (slot.ClearBeforeNextBlock())
            {
                slot.Reset();
            }

            chain.Process(buffer, 0, buffer.Length, channels);
        }

        /// <summary>Reads the serialized chain and prepares it. This is what <see cref="Awake" /> runs.</summary>
        /// <remarks>
        /// It does not go through the <see cref="Chain" /> setter, because that setter treats the asset already stored
        /// as a no-op: an object loaded with a chain would otherwise never prepare it.
        /// </remarks>
        internal void Initialize()
        {
            _layout.Refresh();
            Select(_chain);
        }

        /// <summary>Re-prepares the selected chain for a new output rate or layout. Runs on the main thread.</summary>
        /// <remarks>
        /// A chain's delay lines are sized from the sample rate, so a device change leaves the prepared chain wrong
        /// until this runs. The old slot stays alive for the block in flight and is dropped after it.
        /// </remarks>
        internal void OnAudioConfigurationChanged(bool deviceChanged)
        {
            _layout.Invalidate();
            _liveChannels = 0;
            _slots.Clear();
            Select(_chain);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            ProcessBuffer(data, channels);
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            /*
                A device change while this component was disabled never reached the handler. An unchanged layout leaves a
                running release alone, so a plain enable does not restart it.
            */
            if (!_layout.MatchesDevice())
            {
                OnAudioConfigurationChanged(deviceChanged: true);
            }
        }

        /// <summary>Re-selects the serialized chain when an inspector edit changes it in play mode.</summary>
        /// <remarks>
        /// A game assigns <see cref="Chain" />, but an edit to the serialized field does not pass through that setter.
        /// Without this, the audio would keep the slot it already ran while the inspector named the new asset.
        /// </remarks>
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Select(_chain);
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        /// <summary>Publishes the prepared slot for <paramref name="asset" />, owing it a clear first.</summary>
        private void Select(DspChainAsset asset)
        {
            DspChainSlot prepared = SlotFor(asset);
            if (prepared != null)
            {
                prepared.RequestClear();
            }

            _running = prepared;
        }

        /// <summary>The prepared chain for <paramref name="asset" /> in one slot, built the first time this bus meets it.</summary>
        private DspChainSlot SlotFor(DspChainAsset asset)
        {
            if (asset == null)
            {
                return null;
            }

            foreach (DspChainSlot slot in _slots)
            {
                if (slot.Asset == asset)
                {
                    return slot;
                }
            }

            _layout.Refresh();
            DspChainSlot created = new(asset);
            created.Prepare(_layout.SampleRate, _layout.BlockSize, _layout.Channels);
            _slots.Add(created);
            return created;
        }
    }
}
