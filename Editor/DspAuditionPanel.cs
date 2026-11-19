namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>Controls a single temporary audition at the editor's output layout.</summary>
    internal sealed class DspAuditionPanel : VisualElement
    {
        private const double StartupSeconds = 1.25d;
        private static DspAuditionPanel _playing;

        private readonly ObjectField _clip = new("Audition clip")
        {
            objectType = typeof(AudioClip),
            allowSceneObjects = false,
            tooltip = "Leave empty to use a test tone.",
        };
        private readonly Toggle _loop = new("Loop") { value = true };
        private readonly Toggle _dry = new("Dry");
        private readonly Slider _volume = new("Monitor", 0f, 1f) { value = 0.25f };
        private readonly Label _status = new();
        private readonly ProgressBar _meter = new() { highValue = 1f, title = "DSP peak" };
        private readonly Button _play;
        private readonly Button _stop;
        private readonly AudioLayout _layout = new();
        private Object _asset;
        private AudioSource _source;
        private DspAuditionFilter _filter;
        private SceneView _enabledSceneAudio;
        private AudioClip _tone;
        private DspAuditionProcessor _processor;
        private int _observedHash;
        private bool _recheck;
        private int _lastRevision;
        private int _maximumChannels;
        private string _draftProblem;
        private double _startedAt;
        private bool _editModeFailed;

        internal DspAuditionPanel()
        {
            _play = new Button(Play) { text = "Play" };
            _stop = new Button(Stop) { text = "Stop" };
            VisualElement transport = new();
            transport.style.flexDirection = FlexDirection.Row;
            transport.Add(_play);
            transport.Add(_stop);
            transport.Add(_loop);
            transport.Add(_dry);
            Add(_clip);
            Add(transport);
            Add(_volume);
            Add(_meter);
            Add(_status);
            _status.style.whiteSpace = WhiteSpace.Normal;

            _clip.RegisterValueChangedCallback(_ => Stop());
            _loop.RegisterValueChangedCallback(change =>
            {
                if (_source != null)
                {
                    _source.loop = change.newValue;
                }
            });
            _dry.RegisterValueChangedCallback(change => _processor?.SetDry(change.newValue));
            _volume.RegisterValueChangedCallback(change => _processor?.SetMonitor(change.newValue));
            RegisterCallback<AttachToPanelEvent>(_ => Subscribe());
            RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());
            schedule.Execute(Tick).Every(75);
            UpdateControls();
        }

        internal void SetTarget(Object asset)
        {
            if (_asset == asset)
            {
                return;
            }

            Stop();
            _asset = asset is DspChainAsset || asset is DspGraphAsset ? asset : null;
            style.display = _asset != null ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateControls();
        }

        internal void Stop()
        {
            if (_source != null)
            {
                _source.Stop();
            }

            if (_filter != null)
            {
                _filter.SetProcessor(null);
                Object.DestroyImmediate(_filter.gameObject);
            }
            else if (_source != null)
            {
                Object.DestroyImmediate(_source.gameObject);
            }

            if (_tone != null)
            {
                Object.DestroyImmediate(_tone);
            }

            if (_enabledSceneAudio != null)
            {
                _enabledSceneAudio.audioPlay = false;
                _enabledSceneAudio = null;
            }

            if (_playing == this)
            {
                _playing = null;
            }

            _source = null;
            _filter = null;
            _tone = null;
            _processor = null;
            _meter.value = 0f;
            _status.text = string.Empty;
            UpdateControls();
        }

        private void Subscribe()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += ModeChanged;
            EditorApplication.projectChanged += QueueRecheck;
            Undo.undoRedoPerformed += QueueRecheck;
            AudioSettings.OnAudioConfigurationChanged += DeviceChanged;
        }

        private void Unsubscribe()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.playModeStateChanged -= ModeChanged;
            EditorApplication.projectChanged -= QueueRecheck;
            Undo.undoRedoPerformed -= QueueRecheck;
            AudioSettings.OnAudioConfigurationChanged -= DeviceChanged;
            Stop();
        }

        private void ModeChanged(PlayModeStateChange change)
        {
            Stop();
        }

        private void DeviceChanged(bool deviceWasChanged)
        {
            if (_source != null)
            {
                Stop();
                _status.text = "The audio device changed. Press Play to audition on the new device.";
            }
        }

        private void QueueRecheck()
        {
            _recheck = true;
        }

        private void Play()
        {
            _playing?.Stop();
            Stop();
            SfxAudition.Stop();
            _layout.Invalidate();
            _layout.Refresh();
            AudioClip clip = _clip.value as AudioClip;
            _maximumChannels = Math.Max(_layout.Channels, clip != null ? clip.channels : 1);
            _lastRevision = 0;
            _observedHash = ContentHash();
            _recheck = false;
            if (!TryPrepare(out DspAuditionProcessor.Revision revision, out string problem))
            {
                _status.text = problem;
                return;
            }

            if (!EditorApplication.isPlaying && !HasSceneAudio())
            {
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    _status.text = "Open a Scene view or enter Play Mode to audition.";
                    return;
                }

                _enabledSceneAudio = sceneView;
                sceneView.audioPlay = true;
            }

            if (clip == null)
            {
                clip = CreateTone(_layout.SampleRate);
                _tone = clip;
            }

            GameObject host = EditorUtility.CreateGameObjectWithHideFlags("DxAudio DSP Audition", HideFlags.HideAndDontSave, typeof(AudioSource), typeof(DspAuditionFilter));
            _source = host.GetComponent<AudioSource>();
            _filter = host.GetComponent<DspAuditionFilter>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.bypassListenerEffects = true;
            _source.loop = _loop.value;
            _source.volume = 1f;
            _source.clip = clip;
            _processor = new DspAuditionProcessor(_layout.SampleRate, _layout.BlockSize, _maximumChannels);
            _processor.SetDry(_dry.value);
            _processor.SetMonitor(_volume.value);
            _processor.Publish(revision);
            _filter.SetProcessor(_processor.Process);
            _draftProblem = null;
            _playing = this;
            _startedAt = EditorApplication.timeSinceStartup;
            _status.text = "Starting audition…";
            _source.Play();
            EditorApplication.QueuePlayerLoopUpdate();
            UpdateControls();
        }

        private void Tick()
        {
            if (_processor == null)
            {
                UpdatePlayButton();
                return;
            }

            if (_asset == null || _source == null || _source.clip == null)
            {
                Stop();
                return;
            }

            if (_processor.ProcessedBlocks == 0 && StartupSeconds < EditorApplication.timeSinceStartup - _startedAt)
            {
                bool unsupported = 0 < _processor.UnsupportedChannels;
                bool failedEditMode = !unsupported && !EditorApplication.isPlaying;
                _editModeFailed = failedEditMode;
                Stop();
                _status.text =
                    unsupported ? "This layout exceeds the DSP audition budget. Try a clip with fewer channels."
                    : failedEditMode ? "Unity sent no Edit Mode audio. Enter Play Mode to audition."
                    : "Unity sent no audio. Check that the scene has an active Audio Listener and audio is enabled.";
                return;
            }

            if (0 < _processor.ProcessedBlocks && 0 < _processor.UnsupportedChannels)
            {
                Stop();
                _status.text = "The preview audio layout changed. Press Play to restart audition.";
                return;
            }

            if (0 < _processor.ProcessedBlocks && !_source.isPlaying && !EditorApplication.isPaused)
            {
                Stop();
                return;
            }

            int hash = ContentHash();
            if (_recheck || hash != _observedHash)
            {
                _recheck = false;
                _observedHash = hash;
                bool prepared = TryPrepare(out DspAuditionProcessor.Revision revision, out _draftProblem);
                _processor.Publish(prepared ? revision : null);
            }

            int active = _processor.ActiveRevision;
            if (0 < active)
            {
                string mode = _dry.value ? "Dry" : "Wet";
                string text = $"{mode} · Playing revision {active}";
                if (_draftProblem != null)
                {
                    text += $". Draft pending. {_draftProblem}";
                }
                else if (active != _lastRevision)
                {
                    text += $". Applying revision {_lastRevision}…";
                }

                _status.text = text;
            }

            _meter.value = Math.Min(1f, _processor.Peak);
            _meter.title = 1f < _processor.Peak ? "DSP peak · Clipping" : "DSP peak";
        }

        private bool TryPrepare(out DspAuditionProcessor.Revision revision, out string problem)
        {
            DspGraphAsset graphAsset = _asset as DspGraphAsset;
            DspChainAsset chainAsset = _asset as DspChainAsset;
            DspAuditionProcessor.Path[] paths = new DspAuditionProcessor.Path[_maximumChannels];
            bool prepared = false;
            for (int channels = 1; channels <= _maximumChannels; ++channels)
            {
                DspGraph graph = null;
                DspChain chain = null;
                if (graphAsset != null)
                {
                    if (
                        !graphAsset.TryCreateGraph(out graph)
                        || !graph.TryPrepare(
                            _layout.SampleRate,
                            _layout.BlockSize,
                            channels,
                            graphAsset.IndexOfNode(graphAsset.InputNode),
                            graphAsset.IndexOfNode(graphAsset.OutputNode)
                        )
                    )
                    {
                        continue;
                    }
                }
                else
                {
                    chain = chainAsset.CreateChain();
                    if (!chain.TryPrepare(_layout.SampleRate, _layout.BlockSize, channels))
                    {
                        continue;
                    }
                }

                int latency = graph?.LatencySamples ?? chain.LatencySamples;
                if (latency < 0 || DspGraph.MaximumPreparedBytes < (long)latency * channels * sizeof(float))
                {
                    continue;
                }

                paths[channels - 1] = new DspAuditionProcessor.Path(_layout.BlockSize, channels, chain, graph);
                prepared = true;
            }

            int currentChannels = _processor?.Channels ?? 0;
            if (!prepared || (0 < currentChannels && paths[currentChannels - 1] == null))
            {
                problem = "The current draft cannot play on this audio layout. Check the signal path and DSP budget.";
                revision = null;
                return false;
            }

            ++_lastRevision;
            problem = null;
            revision = new DspAuditionProcessor.Revision(_lastRevision, paths);
            return true;
        }

        private int ContentHash()
        {
            unchecked
            {
                if (_asset is DspChainAsset chain)
                {
                    return ChainHash(chain);
                }

                DspGraphAsset graph = (DspGraphAsset)_asset;
                int hash = (graph.InputNode?.GetHashCode() ?? 0) * 397 ^ (graph.OutputNode?.GetHashCode() ?? 0);
                foreach (DspGraphNode node in graph.Nodes)
                {
                    hash = hash * 397 ^ (node?.Name?.GetHashCode() ?? 0);
                    hash = hash * 397 ^ ChainHash(node?.Chain);
                }

                foreach (DspGraphEdge edge in graph.Edges)
                {
                    hash = hash * 397 ^ (edge?.From?.GetHashCode() ?? 0);
                    hash = hash * 397 ^ (edge?.To?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }

        private static int ChainHash(DspChainAsset chain)
        {
            if (chain == null)
            {
                return 0;
            }
            unchecked
            {
                int hash = chain.GetHashCode();
                foreach (DspEffectAsset effect in chain.Effects)
                {
                    hash = hash * 397;
                    if (effect != null)
                    {
                        hash = (hash ^ effect.GetHashCode()) * 397 ^ EditorUtility.GetDirtyCount(effect);
                    }
                }

                return hash;
            }
        }

        private void UpdateControls()
        {
            bool needsPlayMode = _editModeFailed && !EditorApplication.isPlaying;
            if (needsPlayMode)
            {
                _status.text = "Edit Mode audio was unavailable. Try again or enter Play Mode.";
            }

            UpdatePlayButton();
            _stop.SetEnabled(_source != null);
        }

        private void UpdatePlayButton()
        {
            bool needsSceneAudio = _source == null && !EditorApplication.isPlaying && !HasSceneAudio();
            _play.text = needsSceneAudio ? "Play with Scene Audio" : "Play";
            _play.tooltip = needsSceneAudio ? "Enable Scene view audio until audition stops." : "Audition the selected DSP asset.";
            _play.SetEnabled(_asset != null && _source == null);
        }

        private static bool HasSceneAudio()
        {
            foreach (SceneView view in SceneView.sceneViews)
            {
                if (view != null && view.audioPlay)
                {
                    return true;
                }
            }

            return false;
        }

        private static AudioClip CreateTone(int sampleRate)
        {
            const float ToneHertz = 220f;
            float[] samples = new float[sampleRate];
            for (int index = 0; index < samples.Length; ++index)
            {
                double phase = 2d * Math.PI * ToneHertz * index / sampleRate;
                samples[index] = (float)(0.15d * Math.Sin(phase) + 0.05d * Math.Sin(3d * phase));
            }

            AudioClip tone = AudioClip.Create("DxAudio test tone", samples.Length, 1, sampleRate, false);
            tone.hideFlags = HideFlags.HideAndDontSave;
            tone.SetData(samples, 0);
            return tone;
        }
    }
}
