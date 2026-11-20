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
        private const double SeriesGapSeconds = 0.15d;
        private const int SeriesLength = 8;
        private const int DefaultSeed = 81;
        private const string PeakCaption = "Peak before monitor";
        private static DspAuditionPanel _playing;

        private readonly SfxPlaybackResolver _resolver = new();
        private readonly SfxPlaybackResolver _compareResolver = new();
        private readonly IntegerField _seed = new("Preview seed") { value = DefaultSeed, isDelayed = true };
        private readonly Toggle _bypassVariation = new("Bypass variation on replay");
        private readonly VisualElement _cueControls = new();
        private readonly VisualElement _signalPath = new();
        private readonly Button _take;
        private readonly Label _draw = new();
        private readonly Button _series;
        private readonly Button _replay;
        private readonly Button _capture;
        private readonly Button _playA;
        private readonly Button _playB;
        private readonly Label _comparisonStatus = new() { pickingMode = PickingMode.Ignore };
        private readonly ObjectField _clip = new("Audition clip")
        {
            objectType = typeof(AudioClip),
            allowSceneObjects = false,
            tooltip = "Leave empty to use a test tone.",
        };
        private readonly Toggle _loop = new("Loop") { value = true };
        private readonly Toggle _dry = new("Dry") { tooltip = "Bypass the cue chain and graph. Playback variation stays active." };
        private readonly Slider _volume = new("Monitor", 0f, 1f) { value = 0.25f };
        private readonly Label _status = new();
        private readonly ProgressBar _meter = new() { highValue = 1f, title = PeakCaption };
        private readonly Button _play;
        private readonly Button _stop;
        private readonly AudioLayout _layout = new();
        private readonly Action<Object> _openAsset;
        private readonly Action _showSettings;
        private Object _asset;
        private SfxPlayback _lastTake;
        private AudioClip _lastClip;
        private AudioClip _shownClip;
        private bool _lastLoop;
        private Comparison _baseline;
        private Comparison _comparison;
        private int _seriesRemaining;
        private double _nextStrikeAt;
        private AudioSource _source;
        private DspAuditionFilter _filter;
        private SceneView _enabledSceneAudio;
        private AudioClip _tone;
        private DspAuditionProcessor _processor;
        private int _observedHash;
        private bool _recheck;
        private int _lastRevision;
        private int _maximumChannels;
        private float _lowPassHertz;
        private string _draftProblem;
        private double _startedAt;
        private bool _editModeFailed;

        internal DspAuditionPanel(Action<Object> openAsset, Action showSettings)
        {
            _openAsset = openAsset;
            _showSettings = showSettings;
            _resolver.Reset(DefaultSeed);
            _take = new Button(() =>
            {
                if (_shownClip != null)
                {
                    EditorGUIUtility.PingObject(_shownClip);
                }
            })
            {
                tooltip = "Locate the selected recording in the Project window.",
            };
            _play = new Button(Play) { text = "Play" };
            _stop = new Button(StopAudition) { text = "Stop" };
            _series = new Button(PlaySeries) { text = "Play Series", tooltip = "Play eight sequential draws, with a short gap between takes." };
            _replay = new Button(Replay) { text = "Replay Last", tooltip = "Play the last Cue or Series draw again." };
            _capture = new Button(CaptureBaseline) { text = "Capture A", tooltip = "Save the cue's current playback settings for comparison." };
            _playA = new Button(() => PlayComparison(useBaseline: true)) { text = "Play A", tooltip = "Play the captured settings from the preview seed." };
            _playB = new Button(() => PlayComparison(useBaseline: false)) { text = "Play B", tooltip = "Play current settings from the same preview seed." };
            VisualElement transport = DxAudioInspectorUi.Row();
            transport.style.flexWrap = Wrap.Wrap;
            transport.Add(_play);
            transport.Add(_series);
            transport.Add(_replay);
            transport.Add(_stop);
            transport.Add(_loop);
            transport.Add(_dry);
            Add(_clip);
            Add(transport);
            VisualElement seedRow = DxAudioInspectorUi.Row();
            seedRow.style.flexWrap = Wrap.Wrap;
            seedRow.Add(_seed);
            seedRow.Add(new Button(ResetSeed) { text = "Reset Seed", tooltip = "Restart the sequence and clear the audition take history." });
            seedRow.Add(_bypassVariation);
            _cueControls.Add(seedRow);
            VisualElement compareRow = DxAudioInspectorUi.Row();
            compareRow.style.flexWrap = Wrap.Wrap;
            compareRow.Add(_capture);
            compareRow.Add(_playA);
            compareRow.Add(_playB);
            compareRow.Add(_comparisonStatus);
            _cueControls.Add(compareRow);
            _signalPath.style.flexDirection = FlexDirection.Row;
            _signalPath.style.flexWrap = Wrap.Wrap;
            _cueControls.Add(_signalPath);
            _cueControls.Add(_draw);
            _draw.style.whiteSpace = WhiteSpace.Normal;
            Add(_cueControls);
            Add(_volume);
            Add(_meter);
            Add(_status);
            _status.style.whiteSpace = WhiteSpace.Normal;

            _clip.RegisterValueChangedCallback(_ => Stop());
            _seed.RegisterValueChangedCallback(_ => ResetSeed());
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
            style.display = DisplayStyle.None;
            UpdateControls();
        }

        internal void SetTarget(Object asset)
        {
            if (_asset == asset)
            {
                return;
            }

            Stop();
            _asset = asset is DspChainAsset or DspGraphAsset or SfxCue ? asset : null;
            _compareResolver.ClearShuffleBags();
            _lastTake = default;
            _lastClip = null;
            _shownClip = null;
            _baseline = null;
            _comparisonStatus.text = string.Empty;
            _draw.text = string.Empty;
            style.display = _asset != null ? DisplayStyle.Flex : DisplayStyle.None;
            bool cueMode = _asset is SfxCue;
            _clip.style.display = cueMode ? DisplayStyle.None : DisplayStyle.Flex;
            _loop.style.display = cueMode ? DisplayStyle.None : DisplayStyle.Flex;
            _series.style.display = cueMode ? DisplayStyle.Flex : DisplayStyle.None;
            _replay.style.display = cueMode ? DisplayStyle.Flex : DisplayStyle.None;
            _cueControls.style.display = cueMode ? DisplayStyle.Flex : DisplayStyle.None;
            _cueControls.Unbind();
            if (_asset is SfxCue cue)
            {
                UpdateCuePath(cue);
                _cueControls.TrackSerializedObjectValue(
                    new SerializedObject(cue),
                    _ =>
                    {
                        if (cue != null && _asset == cue)
                        {
                            ValidateComparison();
                            UpdateCuePath(cue);
                            UpdatePlayButton();
                        }
                    }
                );
            }
            UpdateControls();
        }

        internal static void StopCurrent()
        {
            _playing?.Stop();
        }

        internal void Stop()
        {
            _seriesRemaining = 0;
            if (_comparison != null)
            {
                _comparison = null;
                _comparisonStatus.text = "A captured";
            }
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
            _take.style.unityFontStyleAndWeight = FontStyle.Normal;
            ShowPeak(0f);
            _status.text = string.Empty;
            UpdateControls();
        }

        private void StopAudition()
        {
            if (SfxAudition.IsPlaying(_asset))
            {
                SfxAudition.Stop();
            }
            Stop();
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
            BeginTransport();
            if (_asset is SfxCue)
            {
                PlayNextTake();
                return;
            }

            StartClip(_clip.value as AudioClip, 0f, 1f, 1f, _loop.value);
        }

        private void PlaySeries()
        {
            BeginTransport();
            _seriesRemaining = SeriesLength;
            PlayNextTake();
        }

        private void CaptureBaseline()
        {
            if (_asset is SfxCue cue && cue != null && cue.IsPlayable)
            {
                _baseline = new Comparison(cue);
                _comparisonStatus.text = "A captured";
                UpdatePlayButton();
            }
        }

        private void PlayComparison(bool useBaseline)
        {
            ValidateComparison();
            if (_baseline == null || _asset is not SfxCue cue || cue == null)
            {
                return;
            }

            BeginTransport();
            _comparison = useBaseline ? _baseline : new Comparison(cue);
            _compareResolver.Reset(_seed.value);
            _seriesRemaining = SeriesLength;
            _comparisonStatus.text = useBaseline ? "Playing A" : "Playing B";
            PlayNextTake();
        }

        private void ValidateComparison()
        {
            if (_baseline != null && (_asset is not SfxCue cue || cue == null || !_baseline.Matches(cue)))
            {
                _baseline = null;
                if (_comparison != null)
                {
                    Stop();
                }
                _comparisonStatus.text = "Takes changed. Capture A again.";
                UpdatePlayButton();
            }
        }

        private void Replay()
        {
            BeginTransport();
            if (_asset is SfxCue cue && _lastClip != null)
            {
                PlayTake(cue, _lastClip, _lastTake, _lastLoop, _bypassVariation.value);
            }
        }

        private void ResetSeed()
        {
            Stop();
            _resolver.Reset(_seed.value);
            _lastTake = default;
            _lastClip = null;
            _shownClip = null;
            _take.text = "Take";
            _draw.text = string.Empty;
            UpdateControls();
        }

        private void BeginTransport()
        {
            if (_playing != this)
            {
                StopCurrent();
            }
            Stop();
            SfxAudition.Stop();
        }

        private void PlayNextTake()
        {
            if (_asset is not SfxCue cue || cue == null || !TryResolveTake(cue, out SfxPlayback playback))
            {
                Stop();
                _status.text = "Add a playable take to audition this cue.";
                return;
            }

            bool loop = _seriesRemaining == 0 && cue.IsLooping;
            if (0 < _seriesRemaining)
            {
                --_seriesRemaining;
            }

            AudioClip clip = playback.Variant.Clip;
            if (PlayTake(cue, clip, playback, loop, bypass: false) && _comparison == null)
            {
                _lastClip = clip;
                _lastTake = playback;
                _lastLoop = loop;
            }
        }

        private bool TryResolveTake(SfxCue cue, out SfxPlayback playback)
        {
            Comparison settings = _comparison;
            return settings == null
                ? _resolver.TryResolve(cue, out playback)
                : _compareResolver.TryResolve(cue, settings.TakeRevision, settings.Variation, settings.PitchJitter, settings.Volume, settings.Gains, out playback);
        }

        private bool PlayTake(SfxCue cue, AudioClip clip, SfxPlayback playback, bool loop, bool bypass)
        {
            float pitch = bypass ? 1f : playback.Pitch;
            float volume = bypass ? playback.BaseVolume : playback.Volume;
            float cutoff = bypass ? 0f : playback.LowPassHertz;
            if (!StartClip(clip, playback.StartSeconds, pitch, volume, loop, cutoff))
            {
                return false;
            }

            _shownClip = clip;
            _take.text = clip.name;
            _take.SetEnabled(true);
            _take.style.unityFontStyleAndWeight = FontStyle.Bold;
            double cents = 0f < pitch ? 1200d * Math.Log(pitch, 2d) : 0d;
            string level = 0f < volume ? $"{20d * Math.Log10(volume):+0.##;-0.##;0} dB" : "Muted";
            string filter = 0f < cutoff ? $"{cutoff:0} Hz" : "Off";
            _draw.text = $"Trim {playback.StartSeconds:0.###} s · Pitch {cents:+0.##;-0.##;0} cents · Voice level {level} · Low-pass {filter}";
            if (bypass)
            {
                _draw.text += " · Variation bypassed";
            }
            if (cue.FollowsTakeOf != null && !playback.FollowedTake)
            {
                _draw.text += " · No matching leader take in this preview. Drew from this cue.";
            }
            return true;
        }

        private bool StartClip(AudioClip clip, float trim, float pitch, float volume, bool loop, float lowPassHertz = 0f)
        {
            _lowPassHertz = lowPassHertz;
            _layout.Invalidate();
            _layout.Refresh();
            _maximumChannels = Math.Max(_layout.Channels, clip != null ? clip.channels : 1);
            _lastRevision = 0;
            _observedHash = ContentHash();
            _recheck = false;
            if (!TryPrepare(out DspAuditionProcessor.Revision revision, out string problem))
            {
                Stop();
                _status.text = problem;
                return false;
            }

            if (!TryEnableSceneAudio(out SceneView enabled))
            {
                Stop();
                _status.text = "Open a Scene view or enter Play Mode to audition.";
                return false;
            }

            if (enabled != null)
            {
                _enabledSceneAudio = enabled;
            }

            if (clip == null)
            {
                clip = CreateTone(_layout.SampleRate);
                _tone = clip;
            }

            if (_source == null)
            {
                GameObject host = EditorUtility.CreateGameObjectWithHideFlags("DxAudio DSP Audition", HideFlags.HideAndDontSave, typeof(AudioSource), typeof(DspAuditionFilter));
                _source = host.GetComponent<AudioSource>();
                _filter = host.GetComponent<DspAuditionFilter>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
                _source.bypassListenerEffects = true;
                _source.bypassReverbZones = true;
            }

            _source.loop = loop;
            _source.volume = volume;
            _source.pitch = pitch;
            _source.clip = clip;
            _source.time = trim;
            _processor = new DspAuditionProcessor(_layout.SampleRate, _layout.BlockSize, _maximumChannels);
            _processor.SetDry(_dry.value);
            _processor.SetMonitor(_volume.value);
            _processor.Publish(revision);
            _filter.SetProcessor(_processor.Process);
            _draftProblem = null;
            _playing = this;
            _startedAt = EditorApplication.timeSinceStartup;
            ShowPeak(0f);
            _status.text = "Starting audition…";
            _source.Play();
            EditorApplication.QueuePlayerLoopUpdate();
            UpdateControls();
            return true;
        }

        private void Tick()
        {
            ValidateComparison();
            if (_processor == null)
            {
                if (0 < _seriesRemaining && _nextStrikeAt <= EditorApplication.timeSinceStartup && !EditorApplication.isPaused)
                {
                    PlayNextTake();
                }
                UpdatePlayButton();
                UpdateStopButton();
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
                if (_seriesRemaining == 0)
                {
                    float peak = _asset is SfxCue ? _processor.PeakSinceStart : 0f;
                    Stop();
                    ShowPeak(peak);
                }
                else
                {
                    ShowPeak(_processor.PeakSinceStart);
                    _filter.SetProcessor(null);
                    _processor = null;
                    _nextStrikeAt = EditorApplication.timeSinceStartup + SeriesGapSeconds;
                    _status.text = "Waiting for the next take…";
                    _take.style.unityFontStyleAndWeight = FontStyle.Normal;
                }
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
                string text = _asset is SfxCue ? mode : $"{mode} · Playing revision {active}";
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

            ShowPeak(_asset is SfxCue ? _processor.PeakSinceStart : _processor.Peak);
        }

        private void ShowPeak(float peak)
        {
            _meter.value = Math.Min(1f, peak);
            _meter.title = 1f < peak ? PeakCaption + " · Above 0 dBFS" : PeakCaption;
        }

        private bool TryPrepare(out DspAuditionProcessor.Revision revision, out string problem)
        {
            SfxCue cue = _asset as SfxCue;
            DspGraphAsset graphAsset = cue != null ? cue.Graph : _asset as DspGraphAsset;
            DspChainAsset chainAsset = cue != null ? cue.Chain : _asset as DspChainAsset;
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

                if (chainAsset != null)
                {
                    chain = chainAsset.CreateChain();
                    if (!chain.TryPrepare(_layout.SampleRate, _layout.BlockSize, channels))
                    {
                        continue;
                    }
                }

                long latency = (long)(graph?.LatencySamples ?? 0) + (chain?.LatencySamples ?? 0);
                if (latency < 0 || DspGraph.MaximumPreparedBytes < latency * channels * sizeof(float))
                {
                    continue;
                }

                DspFilter lowPass = null;
                if (0f < _lowPassHertz)
                {
                    lowPass = new DspFilter { FrequencyHz = _lowPassHertz };
                    if (!lowPass.TryPrepare(_layout.SampleRate, _layout.BlockSize, channels))
                    {
                        continue;
                    }
                }

                paths[channels - 1] = new DspAuditionProcessor.Path(_layout.BlockSize, channels, chain, graph, lowPass);
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

                if (_asset is SfxCue cue)
                {
                    return ChainHash(cue.Chain) * 397 ^ GraphHash(cue.Graph);
                }
                return GraphHash(_asset as DspGraphAsset);
            }
        }

        private static int GraphHash(DspGraphAsset graph)
        {
            if (graph == null)
            {
                return 0;
            }
            unchecked
            {
                int hash = (graph.GetHashCode() * 397 ^ (graph.InputNode?.GetHashCode() ?? 0)) * 397 ^ (graph.OutputNode?.GetHashCode() ?? 0);
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
            UpdateStopButton();
        }

        private void UpdateStopButton()
        {
            _stop.SetEnabled(_source != null || SfxAudition.IsPlaying(_asset));
        }

        private void UpdatePlayButton()
        {
            bool needsSceneAudio = _source == null && !EditorApplication.isPlaying && !HasSceneAudio();
            SfxCue cue = _asset as SfxCue;
            bool idle = _source == null && _seriesRemaining == 0;
            bool playable = cue != null && cue.IsPlayable;
            _play.text =
                cue != null ? "Play Cue"
                : needsSceneAudio ? "Play with Scene Audio"
                : "Play";
            _play.tooltip = needsSceneAudio ? "Enable Scene view audio until audition stops." : "Audition the selected asset.";
            _play.SetEnabled(_asset != null && idle && (cue == null || playable));
            _series.SetEnabled(idle && playable);
            _replay.SetEnabled(idle && _lastClip != null);
            _capture.SetEnabled(idle && playable);
            _playA.SetEnabled(idle && playable && _baseline != null);
            _playB.SetEnabled(idle && playable && _baseline != null);
            _take.SetEnabled(_shownClip != null);
        }

        private void UpdateCuePath(SfxCue cue)
        {
            _signalPath.Clear();
            _take.text = _shownClip != null ? _shownClip.name : "Take";
            _signalPath.Add(_take);
            if (_showSettings != null)
            {
                AddPathStage(new Button(_showSettings) { text = "Playback settings" });
            }
            if (cue.Chain != null)
            {
                AddPathStage(new Button(() => _openAsset(cue.Chain)) { text = cue.Chain.name, tooltip = "Open the cue chain" });
            }
            if (cue.Graph != null)
            {
                AddPathStage(new Button(() => _openAsset(cue.Graph)) { text = cue.Graph.name, tooltip = "Open the cue graph" });
            }
            AddPathStage(new Label("Isolated preview output") { tooltip = "Player and scene mixer routing are bypassed." });
        }

        private void AddPathStage(VisualElement stage)
        {
            _signalPath.Add(new Label("→") { pickingMode = PickingMode.Ignore });
            _signalPath.Add(stage);
        }

        internal static bool TryEnableSceneAudio(out SceneView enabled)
        {
            enabled = null;
            if (EditorApplication.isPlaying || HasSceneAudio())
            {
                return true;
            }

            enabled = SceneView.lastActiveSceneView;
            if (enabled == null)
            {
                return false;
            }

            enabled.audioPlay = true;
            return true;
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

        private sealed class Comparison
        {
            private readonly AudioClip[] _clips;
            private readonly float[] _trims;
            private readonly bool _loop;

            internal readonly SfxVariation Variation;
            internal readonly int TakeRevision;
            internal readonly float PitchJitter;
            internal readonly float Volume;
            internal readonly float[] Gains;

            internal Comparison(SfxCue cue)
            {
                Variation = cue.Variation;
                TakeRevision = cue.TakeRevision;
                PitchJitter = cue.PitchJitter;
                Volume = cue.Volume;
                _loop = cue.IsLooping;
                _clips = new AudioClip[cue.Variants.Count];
                _trims = new float[_clips.Length];
                Gains = new float[cue.PlayableVariantCount];
                int playable = 0;
                for (int index = 0; index < _clips.Length; ++index)
                {
                    SfxVariant take = cue.Variants[index];
                    _clips[index] = take?.Clip;
                    _trims[index] = take?.StartTimeSeconds ?? 0f;
                    if (_clips[index] != null)
                    {
                        Gains[playable++] = take.Gain;
                    }
                }
            }

            internal bool Matches(SfxCue cue)
            {
                if (cue.Variants.Count != _clips.Length || cue.IsLooping != _loop || cue.PlayableVariantCount != Gains.Length)
                {
                    return false;
                }

                for (int index = 0; index < _clips.Length; ++index)
                {
                    SfxVariant take = cue.Variants[index];
                    if (_clips[index] != take?.Clip || !_trims[index].Equals(take?.StartTimeSeconds ?? 0f))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
