namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DxAudioMixMonitorWindow : EditorWindow
    {
        private const int SamplesPerSecond = 20;
        private const double SampleIntervalSeconds = 1d / SamplesPerSecond;
        private const float TrackLabelWidth = 125f;
        private const float TrackHeight = 19f;

        private static readonly CustomStyleProperty<Color> PlayingColorRole = new("--dxa-theme-accent");
        private static readonly CustomStyleProperty<Color> HeldColorRole = new("--dxa-theme-target");
        private static readonly CustomStyleProperty<Color> PausedColorRole = new("--dxa-theme-wire");
        private static readonly CustomStyleProperty<Color> DropColorRole = new("--dxa-theme-fit");
        private static readonly CustomStyleProperty<Color> TrackColorRole = new("--dxa-theme-surface");
        private static readonly CustomStyleProperty<Color> CursorColorRole = new("--dxa-theme-text");

        internal MixMonitorDropCapture Capture { get; } = new();
        internal MixMonitorTimeline Timeline { get; } = new(1 + (int)(SamplesPerSecond * MixMonitorTimeline.HistorySeconds));

        [SerializeField]
        private SfxPlayer _player;

        private double _nextSample;
        private int _cursor;
        private bool _showVoices = true;
        private Vector2 _scroll;
        private IMGUIContainer _content;
        private Color _playingColor;
        private Color _heldColor;
        private Color _pausedColor;
        private Color _dropColor;
        private Color _trackColor;
        private Color _cursorColor;
        private bool _themeReady;

        [MenuItem("Tools/Wallstop Studios/DxAudio/Mix Monitor")]
        private static void Open()
        {
            DxAudioMixMonitorWindow window = GetWindow<DxAudioMixMonitorWindow>("Mix Monitor");
            GameObject selected = Selection.activeGameObject;
            if (window._player == null && selected != null)
            {
                window.SelectPlayer(selected.GetComponent<SfxPlayer>());
            }
        }

        private static float TrackX(in Rect track, double start, double end, double time)
        {
            return Mathf.Lerp(track.x + 2f, track.xMax - 2f, (float)((time - start) / (end - start)));
        }

        private static GUIContent DropLabel(SfxDropReason reason)
        {
            return DxAudioValidation.TryFromDrop(null, reason, out DxAudioValidation.Finding finding)
                ? new GUIContent($"{reason} ({DxAudioValidation.Id(finding.Rule)})", DxAudioValidation.Description(finding.Rule))
                : new GUIContent(reason.ToString());
        }

        public void CreateGUI()
        {
            DxAudioEditorTheme.Apply(rootVisualElement);
            _content = new IMGUIContainer(DrawWindow);
            _content.style.flexGrow = 1f;
            rootVisualElement.RegisterCallback<CustomStyleResolvedEvent>(ResolveColors);
            rootVisualElement.Add(_content);
        }

        internal void SelectPlayer(SfxPlayer player)
        {
            if (Capture.Paused || player == _player || (player != null && EditorUtility.IsPersistent(player)))
            {
                return;
            }

            _player = player;
            Timeline.Reset(player == null ? 0 : player.VoiceCount);
            _scroll = Vector2.zero;
            SamplePlayer();
        }

        internal void SamplePlayer()
        {
            if (Capture.Paused)
            {
                return;
            }

            if (_player == null)
            {
                Timeline.Reset(0);
                Capture.Sample(SfxDropCounts.None);
                return;
            }

            if (Timeline.VoiceCount != _player.VoiceCount)
            {
                Timeline.Reset(_player.VoiceCount);
            }

            SfxDropCounts counts = _player.DropCounts;
            Capture.Sample(counts);
            Timeline.Sample(EditorApplication.timeSinceStartup, counts);
            for (int voice = 0; voice < Timeline.VoiceCount; ++voice)
            {
                if (_player.TryObserveVoiceState(voice, out SfxVoiceState state))
                {
                    Timeline.SetVoice(voice, state);
                }
            }

            _cursor = Timeline.Count - 1;
        }

        internal void ToggleCapture()
        {
            Capture.Paused = !Capture.Paused;
            if (Capture.Paused)
            {
                Timeline.Interrupt();
            }
            else
            {
                SamplePlayer();
            }
        }

        private Rect ReserveTrack(string name)
        {
            Rect row = EditorGUILayout.GetControlRect(false, TrackHeight);
            GUI.Label(new Rect(row.x, row.y, TrackLabelWidth, row.height), name, EditorStyles.miniLabel);
            Rect track = new(row.x + TrackLabelWidth, row.y, Mathf.Max(1f, row.width - TrackLabelWidth), row.height);
            if (_themeReady && Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(track, _trackColor);
            }

            return track;
        }

        private void DrawCursor(in Rect track, double start, double end, double cursor)
        {
            float x = TrackX(in track, start, end, cursor);
            EditorGUI.DrawRect(new Rect(x, track.y, 1f, track.height), _cursorColor);
        }

        private void ResolveColors(CustomStyleResolvedEvent evt)
        {
            ICustomStyle style = evt.customStyle;
            _themeReady =
                style.TryGetValue(PlayingColorRole, out _playingColor)
                && style.TryGetValue(HeldColorRole, out _heldColor)
                && style.TryGetValue(PausedColorRole, out _pausedColor)
                && style.TryGetValue(DropColorRole, out _dropColor)
                && style.TryGetValue(TrackColorRole, out _trackColor)
                && style.TryGetValue(CursorColorRole, out _cursorColor);
            _content.MarkDirtyRepaint();
        }

        private void OnEnable()
        {
            minSize = new Vector2(380f, 280f);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                Capture.Reset();
                Timeline.Reset(0);
                _nextSample = 0d;
            }

            Repaint();
        }

        private void Update()
        {
            if (!EditorApplication.isPlaying || Capture.Paused || EditorApplication.timeSinceStartup < _nextSample)
            {
                return;
            }

            _nextSample = EditorApplication.timeSinceStartup + SampleIntervalSeconds;
            SamplePlayer();
            _content?.MarkDirtyRepaint();
        }

        private void DrawWindow()
        {
            using (new EditorGUI.DisabledScope(Capture.Paused))
            {
                SfxPlayer selected = (SfxPlayer)EditorGUILayout.ObjectField("SFX Player", _player, typeof(SfxPlayer), allowSceneObjects: true);
                SelectPlayer(selected);
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to capture SFX activity.", MessageType.Info);
                return;
            }

            if (_player == null && !Capture.Paused)
            {
                EditorGUILayout.HelpBox("Select an SFX Player from the scene.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(Capture.Paused ? "Resume" : "Pause", EditorStyles.toolbarButton, GUILayout.Width(65f)))
                {
                    ToggleCapture();
                }

                GUILayout.Label($"Drops: {Capture.VisibleCount}  |  Silenced: {Capture.SilencedCount}", EditorStyles.miniLabel);
            }

            using (EditorGUILayout.ScrollViewScope scroll = new(_scroll))
            {
                _scroll = scroll.scrollPosition;
                if (0 < Timeline.Count)
                {
                    DrawTimeline();
                }

                foreach (SfxDropReason reason in MixMonitorDropCapture.Reasons)
                {
                    DrawDrop(reason);
                }
            }
        }

        private void DrawTimeline()
        {
            int latest = Timeline.Count - 1;
            double end = Timeline.Time(latest);
            double start = end - MixMonitorTimeline.HistorySeconds;
            using (new EditorGUI.DisabledScope(!Capture.Paused || latest == 0))
            {
                _cursor = EditorGUILayout.IntSlider("Sample", _cursor, 0, Math.Max(1, latest));
            }

            double cursor = Timeline.Time(_cursor);
            EditorGUILayout.LabelField(cursor == end ? "Latest sample" : $"{cursor - end:F2} s from latest", EditorStyles.miniLabel);
            Rect ruler = ReserveTrack("Time");
            for (int tick = 0; tick <= 5; ++tick)
            {
                float x = Mathf.Lerp(ruler.x, ruler.xMax, tick / 5f);
                if (tick == 5)
                {
                    GUI.Label(new Rect(x - 26f, ruler.y, 26f, TrackHeight), "0 s", EditorStyles.miniLabel);
                }
                else
                {
                    GUI.Label(new Rect(x, ruler.y, 42f, TrackHeight), $"{(tick - 5) * MixMonitorTimeline.HistorySeconds / 5d:0} s", EditorStyles.miniLabel);
                }
            }

            DrawDropTrack(start, end, cursor);
            _showVoices = EditorGUILayout.Foldout(_showVoices, "SFX voices", toggleOnLabelClick: true);
            if (_showVoices)
            {
                for (int voice = 0; voice < Timeline.VoiceCount; ++voice)
                {
                    DrawVoiceTrack(voice, start, end, cursor);
                }
            }
        }

        private void DrawDropTrack(double start, double end, double cursor)
        {
            Rect track = ReserveTrack("Drop markers");
            if (_themeReady && Event.current.type == EventType.Repaint)
            {
                IReadOnlyList<SfxDropReason> reasons = MixMonitorDropCapture.Reasons;
                for (int frame = 0; frame < Timeline.Count; ++frame)
                {
                    SfxDropCounts drops = Timeline.Drops(frame);
                    for (int reasonIndex = 0; reasonIndex < reasons.Count; ++reasonIndex)
                    {
                        SfxDropReason reason = reasons[reasonIndex];
                        if (0 < drops[reason] && !Capture.IsSilenced(reason))
                        {
                            float x = TrackX(in track, start, end, Timeline.Time(frame));
                            EditorGUI.DrawRect(new Rect(x - 2f, track.y + 3f, 4f, track.height - 6f), _dropColor);
                            break;
                        }
                    }
                }

                DrawCursor(in track, start, end, cursor);
            }

            SfxDropCounts selected = Timeline.Drops(_cursor);
            foreach (SfxDropReason reason in MixMonitorDropCapture.Reasons)
            {
                if (0 < selected[reason] && !Capture.IsSilenced(reason))
                {
                    EditorGUILayout.LabelField(DropLabel(reason), new GUIContent(selected[reason].ToString()), EditorStyles.miniLabel);
                }
            }
        }

        private void DrawVoiceTrack(int voice, double start, double end, double cursor)
        {
            ref readonly SfxVoiceState selected = ref Timeline.Voice(_cursor, voice);
            string status =
                selected.isPaused ? (selected.isHeld ? "Loop paused" : "Paused")
                : selected.isHeld ? "Held loop"
                : selected.isPlaying ? "Playing"
                : selected.isUsable ? "Idle"
                : "Unavailable";
            Rect track = ReserveTrack($"{voice + 1}  {status}");
            if (!_themeReady || Event.current.type != EventType.Repaint)
            {
                return;
            }

            for (int frame = 0; frame < Timeline.Count; ++frame)
            {
                ref readonly SfxVoiceState state = ref Timeline.Voice(frame, voice);
                if (!state.isUsable || state.IsFree)
                {
                    continue;
                }

                double time = Timeline.Time(frame);
                double until = time;
                if (frame + 1 < Timeline.Count && !Timeline.StartsSegment(frame + 1))
                {
                    until = Math.Min(Timeline.Time(frame + 1), time + 2d * SampleIntervalSeconds);
                }

                float left = TrackX(in track, start, end, time);
                float right = TrackX(in track, start, end, until);
                Color color =
                    state.isPaused ? _pausedColor
                    : state.isHeld ? _heldColor
                    : _playingColor;
                EditorGUI.DrawRect(new Rect(Mathf.Min(left, track.xMax - 1f), track.y + 3f, Mathf.Max(1f, right - left), track.height - 6f), color);
            }

            DrawCursor(in track, start, end, cursor);
        }

        private void DrawDrop(SfxDropReason reason)
        {
            bool silenced = Capture.IsSilenced(reason);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(DropLabel(reason), GUILayout.ExpandWidth(true));
                GUILayout.Label(silenced ? "-" : Capture.Counts[reason].ToString(), GUILayout.Width(60f));
                if (GUILayout.Button(silenced ? "Show" : "Silence", GUILayout.Width(65f)))
                {
                    Capture.SetSilenced(reason, !silenced);
                }
            }
        }
    }
}
