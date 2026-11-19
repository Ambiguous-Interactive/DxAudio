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
    /// the device. A voice reads an asset once: it keeps the effect settings it built the first time it met that asset.
    /// A graph follows the same rule and runs its branches side by side, so it comes after the chain and before the bus.
    ///
    /// The bus runs after the voice's own chain, on each voice separately. Unity gives a script no way to sum one
    /// source into another, so every bus effect in this package acts on one voice at a time and never on the mixed
    /// output: two voices that each fit under a ceiling can still sum above it.
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
        [Tooltip("A chain this host runs after its own, on one voice at a time, so a player's bus reaches every voice.")]
        private DspChainAsset _busChain;

        [SerializeField]
        [Tooltip("A graph this host runs after its own chain. Its branches run in parallel, and it runs on one voice at a time.")]
        private DspGraphAsset _graph;

        private readonly List<Instance> _instances = new();
        private readonly List<GraphInstance> _graphInstances = new();
        private Instance _running;
        private Instance _bus;
        private GraphInstance _runningGraph;
        private AudioSource _source;
        private int _sampleRate;
        private int _blockSize = FallbackBlockSize;
        private int _outputChannels;
        private int _liveChannels;
        private int _skippedBlocks;

        /// <summary>The chain this host runs, or null when it passes the block through.</summary>
        /// <remarks>
        /// Assigning starts a new play. The chain and the bus are cleared at the next block that runs them, never here:
        /// the audio thread may be inside a block that is still playing, and a swap must not spend another chain's
        /// clear. The graph is assigned separately, so a game that drives a host itself sets both on every play.
        /// </remarks>
        public DspChainAsset Chain
        {
            get => _chain;
            set
            {
                Instance prepared = InstanceFor(value, bus: false);
                _chain = value;
                _running = prepared;
                MarkCleared(prepared);
                MarkCleared(_bus);
            }
        }

        /// <summary>The chain run after <see cref="Chain" />, or null.</summary>
        public DspChainAsset BusChain
        {
            get => _busChain;
            set
            {
                Instance prepared = InstanceFor(value, bus: true);
                _busChain = value;
                _bus = prepared;
                MarkCleared(prepared);
            }
        }

        /// <summary>The graph run after <see cref="Chain" /> and before <see cref="BusChain" />, or null.</summary>
        /// <remarks>
        /// A graph runs its branches side by side, so it is the one stage that can follow two paths and sum them. A cue
        /// or track names it, and a player sets it on every play, so a reused voice never inherits the last play's
        /// graph. Assigning clears this graph at its next block, never here.
        /// </remarks>
        public DspGraphAsset Graph
        {
            get => _graph;
            set
            {
                GraphInstance prepared = GraphInstanceFor(value);
                _graph = value;
                _runningGraph = prepared;
                MarkCleared(prepared);
            }
        }

        /// <summary>The chain running now, for a test or an inspector reading a stage. Null is a pass-through.</summary>
        public DspChain LiveChain => _running != null ? _running.For(ReadingChannels) : null;

        /// <summary>The graph running now. Null is a pass-through.</summary>
        public DspGraph LiveGraph => _runningGraph != null ? _runningGraph.For(ReadingChannels) : null;

        /// <summary>The bus running now. Null is a pass-through.</summary>
        public DspChain LiveBusChain => _bus != null ? _bus.For(ReadingChannels) : null;

        /// <summary>The delay the running stages add together, in samples.</summary>
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

                DspGraph graph = LiveGraph;
                if (graph != null)
                {
                    latency += graph.LatencySamples;
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
                DspChain running = LiveChain;
                DspChain bus = LiveBusChain;
                float held = running != null ? running.GainReductionDecibels : 0f;
                if (bus != null)
                {
                    held += bus.GainReductionDecibels;
                }

                return held;
            }
        }

        /// <summary>The output channel count this host prepares for; every narrower block also runs.</summary>
        public int ChannelCount => _outputChannels;

        /// <summary>How many blocks were wider than any prepared chain. A diagnostic; zero is healthy.</summary>
        public int SkippedBlocks => _skippedBlocks;

        /// <summary>Whether the source on this object reports playback now, so a live reading belongs to it.</summary>
        public bool IsSounding
        {
            get
            {
                AudioSource source = Source;
                return source != null && source.isPlaying;
            }
        }

        /// <summary>Prepares <paramref name="asset" />'s chain for this voice now, so the first play of it allocates nothing.</summary>
        public void Warm(DspChainAsset asset)
        {
            _ = InstanceFor(asset, bus: false);
        }

        /// <summary>Prepares <paramref name="asset" />'s graph for this voice now, so the first play of it allocates nothing.</summary>
        public void Warm(DspGraphAsset asset)
        {
            _ = GraphInstanceFor(asset);
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

        /// <summary>Whether a graph for <paramref name="asset" /> is already prepared here, so it cannot allocate.</summary>
        internal bool IsPrepared(DspGraphAsset asset)
        {
            if (asset == null)
            {
                return false;
            }

            for (int index = 0; index < _graphInstances.Count; index++)
            {
                if (_graphInstances[index].asset == asset)
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

            Instance runningInstance = _running;
            Instance busInstance = _bus;
            GraphInstance graphInstance = _runningGraph;
            DspChain running = runningInstance != null ? runningInstance.For(channels) : null;
            DspGraph graph = graphInstance != null ? graphInstance.For(channels) : null;
            DspChain bus = busInstance != null ? busInstance.For(channels) : null;
            if (running == null && graph == null && bus == null)
            {
                if (runningInstance != null || graphInstance != null || busInstance != null)
                {
                    _skippedBlocks++;
                }

                return;
            }

            _liveChannels = channels;
            if (running != null && runningInstance.ClearBeforeNextBlock())
            {
                running.Reset();
            }

            if (graph != null && graphInstance.ClearBeforeNextBlock())
            {
                graph.Reset();
            }

            if (bus != null && busInstance.ClearBeforeNextBlock())
            {
                bus.Reset();
            }

            if (running != null)
            {
                running.Process(buffer, 0, buffer.Length, channels);
            }

            if (graph != null)
            {
                graph.Process(buffer, 0, buffer.Length, channels);
            }

            if (bus != null)
            {
                bus.Process(buffer, 0, buffer.Length, channels);
            }
        }

        private AudioSource Source
        {
            get
            {
                if (_source == null)
                {
                    _source = GetComponent<AudioSource>();
                }

                return _source;
            }
        }

        private void Awake()
        {
            EnsurePrepared();
            Chain = _chain;
            Graph = _graph;
            BusChain = _busChain;
        }

        private void OnEnable()
        {
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        /// <summary>Re-prepares the selected chains for a new output rate or layout. Runs on the main thread.</summary>
        /// <remarks>
        /// A chain's delay lines are sized from the sample rate, so a device change leaves every prepared chain wrong
        /// until this runs. The old instances stay alive for the block in flight and are dropped after it.
        /// </remarks>
        internal void OnAudioConfigurationChanged(bool deviceChanged)
        {
            _sampleRate = 0;
            _liveChannels = 0;
            _instances.Clear();
            _graphInstances.Clear();
            Instance running = InstanceFor(_chain, bus: false);
            Instance bus = InstanceFor(_busChain, bus: true);
            GraphInstance graph = GraphInstanceFor(_graph);
            _running = running;
            _bus = bus;
            _runningGraph = graph;
            MarkCleared(running);
            MarkCleared(bus);
            MarkCleared(graph);
        }

        /// <summary>Asks for <paramref name="instance" /> to be cleared before it next processes a block.</summary>
        /// <remarks>
        /// The request belongs to the instance, not to the host: a swap races the audio thread, and a host-wide flag
        /// could be spent on the chain the block is finishing instead of the one the next play selected.
        /// </remarks>
        private static void MarkCleared(Instance instance)
        {
            if (instance != null)
            {
                instance.ClearRequested = 1;
            }
        }

        /// <summary>Asks for <paramref name="instance" /> to be cleared before it next processes a block.</summary>
        private static void MarkCleared(GraphInstance instance)
        {
            if (instance != null)
            {
                instance.ClearRequested = 1;
            }
        }

        /// <summary>Reads the output rate, block size and channel count every chain on this host is prepared for.</summary>
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

        /// <summary>The prepared graphs for <paramref name="asset" /> in one slot, built the first time this voice meets it.</summary>
        private GraphInstance GraphInstanceFor(DspGraphAsset asset)
        {
            if (asset == null)
            {
                return null;
            }

            for (int index = 0; index < _graphInstances.Count; index++)
            {
                GraphInstance met = _graphInstances[index];
                if (met.asset == asset)
                {
                    return met;
                }
            }

            EnsurePrepared();
            GraphInstance created = new(asset);
            created.Prepare(_sampleRate, _blockSize, _outputChannels);
            _graphInstances.Add(created);
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

            /// <summary>One when a new play has asked for a clear that no block has applied yet.</summary>
            public int ClearRequested;

            /// <summary>Whether a clear is owed to this instance, taking the request when it is.</summary>
            public bool ClearBeforeNextBlock()
            {
                if (0 == ClearRequested)
                {
                    return false;
                }

                ClearRequested = 0;
                return true;
            }

            public void Prepare(int sampleRate, int blockSize, int shapes)
            {
                _chains = new DspChain[shapes];
                for (int index = 0; index < shapes; index++)
                {
                    DspChain chain = asset.CreateChain();
                    _ = chain.TryPrepare(sampleRate, blockSize, index + 1);
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

        /// <summary>One graph asset's graphs in one slot, one per block shape the output layout can produce.</summary>
        /// <remarks>
        /// A graph holds a buffer per node and a line per waiting edge, so it follows the same rule a chain does: it is
        /// built once per voice and block shape, and a voice that met the asset keeps what it built.
        /// </remarks>
        private sealed class GraphInstance
        {
            public readonly DspGraphAsset asset;
            private DspGraph[] _graphs = Array.Empty<DspGraph>();

            public GraphInstance(DspGraphAsset asset)
            {
                this.asset = asset;
            }

            /// <summary>One when a new play has asked for a clear that no block has applied yet.</summary>
            public int ClearRequested;

            /// <summary>Whether a clear is owed to this instance, taking the request when it is.</summary>
            public bool ClearBeforeNextBlock()
            {
                if (0 == ClearRequested)
                {
                    return false;
                }

                ClearRequested = 0;
                return true;
            }

            public void Prepare(int sampleRate, int blockSize, int shapes)
            {
                _graphs = new DspGraph[shapes];
                for (int index = 0; index < shapes; index++)
                {
                    // An asset that cannot be built leaves an unprepared graph, which passes every block through, the
                    // same answer a chain that refuses its layout gives. The inspector reports why it was refused.
                    DspGraph graph = asset.TryCreateGraph(out DspGraph built) ? built : new DspGraph();
                    _ = graph.TryPrepare(sampleRate, blockSize, index + 1, asset.IndexOfNode(asset.InputNode), asset.IndexOfNode(asset.OutputNode));
                    _graphs[index] = graph;
                }
            }

            /// <summary>The graph prepared for <paramref name="channels" />, or null when that shape is wider than the layout.</summary>
            public DspGraph For(int channels)
            {
                if (channels <= 0 || _graphs.Length < channels)
                {
                    return null;
                }

                return _graphs[channels - 1];
            }

            public void Reset()
            {
                for (int index = 0; index < _graphs.Length; index++)
                {
                    _graphs[index].Reset();
                }
            }
        }
    }
}
