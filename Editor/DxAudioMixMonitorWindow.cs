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
        private const double MaxSampleGapSeconds = 2d * SampleIntervalSeconds;
        private const double HistorySeconds = MixMonitorTimeline<AudioClip, MusicTrack>.HistorySeconds;
        private const int HistoryCapacity = 1 + (int)(SamplesPerSecond * HistorySeconds);
        private const int DuckHoldCapacity = 32;
        private const float TrackLabelWidth = 125f;
        private const float TrackHeight = 19f;

        private static readonly CustomStyleProperty<Color> PlayingColorRole = new("--dxa-theme-accent");
        private static readonly CustomStyleProperty<Color> HeldColorRole = new("--dxa-theme-target");
        private static readonly CustomStyleProperty<Color> PausedColorRole = new("--dxa-theme-wire");
        private static readonly CustomStyleProperty<Color> DropColorRole = new("--dxa-theme-fit");
        private static readonly CustomStyleProperty<Color> TrackColorRole = new("--dxa-theme-surface");
        private static readonly CustomStyleProperty<Color> CursorColorRole = new("--dxa-theme-text");

        internal MixMonitorDropCapture Capture { get; } = new();
        internal MixMonitorTimeline<AudioClip, MusicTrack> Timeline { get; } = new(HistoryCapacity, MusicPlayer.VoiceCount, DuckHoldCapacity);

        private bool HasSfx => _player != null || 0 < Timeline.VoiceCount || 0 < Capture.Counts.Total;

        [SerializeField]
        private SfxPlayer _player;

        [SerializeField]
        private MusicPlayer _music;

        private readonly Vector3[] _line = new Vector3[HistoryCapacity];
        private readonly MixMonitorWaveforms _waveforms = new();
        private readonly IMusicDuckReason[] _duckReasons = new IMusicDuckReason[DuckHoldCapacity];
        private readonly object[] _duckHolders = new object[DuckHoldCapacity];
        private readonly long[] _duckIds = new long[DuckHoldCapacity];
        private double _nextSample;
        private int _cursor;
        private int _selectedVoice = -1;
        private bool _showVoices = true;
        private bool _showMusic = true;
        private bool _showDucks = true;
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
            if (selected != null)
            {
                window.SelectPlayers(
                    window._player == null ? selected.GetComponent<SfxPlayer>() : window._player,
                    window._music == null ? selected.GetComponent<MusicPlayer>() : window._music
                );
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

        private static string DuckName(IMusicDuckReason reason)
        {
            if (reason is MusicDuck asset && asset == null)
            {
                return "Destroyed duck";
            }

            string name = reason.ToString();
            return string.IsNullOrEmpty(name) ? "Unnamed duck" : name;
        }

        private static void DrawWaveform(in Rect rect, float[] peaks, float start, float end, Color color)
        {
            int columns = Mathf.Clamp(Mathf.CeilToInt(rect.width), 1, peaks.Length);
            float width = rect.width / columns;
            float center = rect.center.y;
            float amplitude = rect.height * 0.5f - 1f;
            for (int column = 0; column < columns; ++column)
            {
                float from = Mathf.Lerp(start, end, (float)column / columns);
                float to = Mathf.Lerp(start, end, (float)(column + 1) / columns);
                float height = Mathf.Max(0.5f, MixMonitorWaveforms.Peak(peaks, from, to) * amplitude);
                EditorGUI.DrawRect(new Rect(rect.x + column * width, center - height, width, 2f * height), color);
            }
        }

        public void CreateGUI()
        {
            DxAudioEditorTheme.Apply(rootVisualElement);
            _content = new IMGUIContainer(DrawWindow);
            _content.style.flexGrow = 1f;
            rootVisualElement.RegisterCallback<CustomStyleResolvedEvent>(ResolveColors);
            rootVisualElement.Add(_content);
        }

        internal void SelectPlayers(SfxPlayer sfx, MusicPlayer music)
        {
            if (sfx != null && EditorUtility.IsPersistent(sfx))
            {
                sfx = _player;
            }

            if (music != null && EditorUtility.IsPersistent(music))
            {
                music = _music;
            }

            if (Capture.Paused || (sfx == _player && music == _music))
            {
                return;
            }

            _player = sfx;
            _music = music;
            _selectedVoice = -1;
            _waveforms.Clear();
            Timeline.Reset(sfx == null ? 0 : sfx.VoiceCount);
            _scroll = Vector2.zero;
            SamplePlayer();
        }

        internal void SamplePlayer()
        {
            if (Capture.Paused)
            {
                return;
            }

            if (_player == null && _music == null)
            {
                Timeline.Reset(0);
                Capture.Sample(SfxDropCounts.None);
                return;
            }

            int voiceCount = _player == null ? 0 : _player.VoiceCount;
            if (Timeline.VoiceCount != voiceCount)
            {
                Timeline.Reset(voiceCount);
            }

            SfxDropCounts counts = _player == null ? SfxDropCounts.None : _player.DropCounts;
            Capture.Sample(counts);
            Timeline.Sample(EditorApplication.timeSinceStartup, counts, _music == null ? (float?)null : _music.Attenuation.volumeScale);
            bool canReadWaveform = true;
            for (int voice = 0; voice < Timeline.VoiceCount; ++voice)
            {
                if (_player.TryObserveVoiceState(voice, out SfxVoiceState state, out AudioClip clip, out float clipSeconds, out int playbackId))
                {
                    Timeline.SetVoice(voice, state, clip, clipSeconds, _waveforms.Observe(clip, ref canReadWaveform), playbackId);
                }
            }

            if (_music != null)
            {
                int holds = _music.CopyDuckHolds(_duckReasons, _duckHolders, _duckIds);
                Timeline.SetDuckHolds(_duckReasons, _duckIds, holds);
                // Only the timeline should retain references, and holders do not belong to the history.
                int copied = Math.Min(holds, DuckHoldCapacity);
                Array.Clear(_duckReasons, 0, copied);
                Array.Clear(_duckHolders, 0, copied);

                for (int deck = 0; deck < Timeline.DeckCount; ++deck)
                {
                    if (_music.TryObserveDeck(deck, out MusicTrack track, out float fade))
                    {
                        Timeline.SetDeck(deck, track, fade);
                    }
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
                _selectedVoice = -1;
                SamplePlayer();
            }
        }

        internal bool SelectClip(int frame, int voice)
        {
            if (Timeline.Clip(frame, voice).clip == null)
            {
                return false;
            }

            if (!Capture.Paused)
            {
                ToggleCapture();
            }

            _cursor = frame;
            _selectedVoice = voice;
            Repaint();
            return true;
        }

        internal (AudioClip clip, float seconds) SelectedClip()
        {
            return 0 <= _selectedVoice ? Timeline.Clip(_cursor, _selectedVoice) : default;
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
            EditorApplication.projectChanged += _waveforms.Clear;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.projectChanged -= _waveforms.Clear;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                Capture.Reset();
                Timeline.Reset(0);
                _waveforms.Clear();
                _selectedVoice = -1;
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
                SfxPlayer sfx = (SfxPlayer)EditorGUILayout.ObjectField("SFX Player", _player, typeof(SfxPlayer), allowSceneObjects: true);
                MusicPlayer music = (MusicPlayer)EditorGUILayout.ObjectField("Music Player", _music, typeof(MusicPlayer), allowSceneObjects: true);
                if (sfx != _player || music != _music)
                {
                    SelectPlayers(sfx, music);
                    GUIUtility.ExitGUI();
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to capture audio activity.", MessageType.Info);
                return;
            }

            if (_player == null && _music == null && !Capture.Paused)
            {
                EditorGUILayout.HelpBox("Select an SFX or Music Player from the scene.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(Capture.Paused ? "Resume" : "Pause", EditorStyles.toolbarButton, GUILayout.Width(65f)))
                {
                    ToggleCapture();
                    Repaint();
                    GUIUtility.ExitGUI();
                }

                if (HasSfx)
                {
                    GUILayout.Label($"Drops: {Capture.VisibleCount}  |  Silenced: {Capture.SilencedCount}", EditorStyles.miniLabel);
                }
            }

            if (Capture.Paused && 0 <= _selectedVoice)
            {
                DrawSelectedClip();
            }

            using (EditorGUILayout.ScrollViewScope scroll = new(_scroll))
            {
                _scroll = scroll.scrollPosition;
                if (0 < Timeline.Count)
                {
                    DrawTimeline();
                }

                if (HasSfx)
                {
                    foreach (SfxDropReason reason in MixMonitorDropCapture.Reasons)
                    {
                        DrawDrop(reason);
                    }
                }
            }
        }

        private void DrawSelectedClip()
        {
            (AudioClip clip, float seconds) = SelectedClip();
            if (clip == null)
            {
                EditorGUILayout.LabelField($"Voice {_selectedVoice + 1}", "No clip at this sample");
                return;
            }

            string detail = $"{clip.name}  |  {seconds:F3} s  |  {Timeline.Voice(_cursor, _selectedVoice).priority} priority";
            EditorGUILayout.LabelField(new GUIContent($"Voice {_selectedVoice + 1} clip"), new GUIContent(detail, detail));
            float[] peaks = Timeline.VoicePeaks(_cursor, _selectedVoice);
            if (peaks == null)
            {
                return;
            }

            Rect preview = EditorGUILayout.GetControlRect(false, 40f);
            if (_themeReady && Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(preview, _trackColor);
                DrawWaveform(in preview, peaks, 0f, 1f, _playingColor);
                float x = Mathf.Lerp(preview.x, preview.xMax - 1f, seconds / clip.length);
                EditorGUI.DrawRect(new Rect(x, preview.y, 1f, preview.height), _cursorColor);
            }
        }

        private void DrawTimeline()
        {
            int latest = Timeline.Count - 1;
            double end = Timeline.Time(latest);
            double start = end - HistorySeconds;
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
                    GUI.Label(new Rect(x, ruler.y, 42f, TrackHeight), $"{(tick - 5) * HistorySeconds / 5d:0} s", EditorStyles.miniLabel);
                }
            }

            if (HasSfx)
            {
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

            if (_music != null || Timeline.DuckGain(latest).HasValue)
            {
                _showMusic = EditorGUILayout.Foldout(_showMusic, "Music", toggleOnLabelClick: true);
                if (_showMusic)
                {
                    for (int deck = 0; deck < Timeline.DeckCount; ++deck)
                    {
                        (MusicTrack track, float fade) = Timeline.Deck(_cursor, deck);
                        string detail = track == null ? "Idle" : $"{fade:P0} {(string.IsNullOrEmpty(track.name) ? track.Context : track.name)}";
                        DrawMusicCurve($"Deck {deck + 1}  {detail}", deck, start, end, cursor, _heldColor);
                    }
                }

                _showDucks = EditorGUILayout.Foldout(_showDucks, "Ducks", toggleOnLabelClick: true);
                if (_showDucks)
                {
                    DrawDuckHolds(start, end, cursor);
                    float? gain = Timeline.DuckGain(_cursor);
                    DrawMusicCurve(gain.HasValue ? $"Duck gain  {gain.Value:P0}" : "Duck gain", -1, start, end, cursor, _heldColor);
                }
            }
        }

        private void DrawDuckHolds(double start, double end, double cursor)
        {
            int omitted = Timeline.OmittedDuckHolds(_cursor);
            if (0 < omitted)
            {
                EditorGUILayout.LabelField($"{omitted} additional holds at this sample", EditorStyles.miniLabel);
            }

            for (int row = 0; row < Timeline.DuckCapacity; ++row)
            {
                int first = 0;
                while (first < Timeline.Count && Timeline.DuckHold(first, row).reason == null)
                {
                    ++first;
                }

                if (first == Timeline.Count)
                {
                    continue;
                }

                IMusicDuckReason selected = Timeline.DuckHold(_cursor, row).reason;
                Rect track = ReserveTrack(selected == null ? $"Hold {row + 1}" : DuckName(selected));
                for (int frame = first; frame < Timeline.Count; )
                {
                    (IMusicDuckReason reason, long id) = Timeline.DuckHold(frame, row);
                    if (reason == null)
                    {
                        ++frame;
                        continue;
                    }

                    double from = Timeline.Time(frame++);
                    while (frame < Timeline.Count && Timeline.CanJoin(frame, MaxSampleGapSeconds) && Timeline.DuckHold(frame, row).id == id)
                    {
                        ++frame;
                    }

                    int endFrame = frame < Timeline.Count && Timeline.CanJoin(frame, MaxSampleGapSeconds) ? frame : frame - 1;
                    float left = TrackX(in track, start, end, from);
                    float right = TrackX(in track, start, end, Timeline.Time(endFrame));
                    Rect bar = new(left, track.y + 2f, Mathf.Max(1f, right - left), track.height - 4f);
                    if (_themeReady && Event.current.type == EventType.Repaint)
                    {
                        Color fill = _heldColor;
                        fill.a *= 0.35f;
                        EditorGUI.DrawRect(bar, fill);
                        EditorGUI.DrawRect(new Rect(bar.x, bar.y, 1f, bar.height), _heldColor);
                        EditorGUI.DrawRect(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f), _heldColor);
                    }

                    string name = DuckName(reason);
                    GUI.Label(bar, new GUIContent(name, $"{name} ({reason.Holders}, hold #{id})"), EditorStyles.miniLabel);
                }

                if (_themeReady && Event.current.type == EventType.Repaint)
                {
                    DrawCursor(in track, start, end, cursor);
                }
            }
        }

        private void DrawMusicCurve(string label, int deck, double start, double end, double cursor, Color color)
        {
            Rect row = ReserveTrack(label);
            if (!_themeReady || Event.current.type != EventType.Repaint)
            {
                return;
            }

            MusicTrack previousTrack = null;
            int points = 0;
            using (new Handles.DrawingScope(color))
            {
                for (int frame = 0; frame < Timeline.Count; ++frame)
                {
                    (MusicTrack track, float fade) = 0 <= deck ? Timeline.Deck(frame, deck) : default;
                    float? value = 0 <= deck ? (track != null ? fade : (float?)null) : Timeline.DuckGain(frame);
                    if (!value.HasValue || previousTrack != track || !Timeline.CanJoin(frame, MaxSampleGapSeconds))
                    {
                        DrawCurveRun(points, color);
                        points = 0;
                    }

                    if (value.HasValue)
                    {
                        _line[points++] = new Vector3(TrackX(in row, start, end, Timeline.Time(frame)), Mathf.Lerp(row.yMax - 3f, row.y + 3f, value.Value));
                    }

                    previousTrack = track;
                }

                DrawCurveRun(points, color);
            }

            DrawCursor(in row, start, end, cursor);
        }

        private void DrawCurveRun(int points, Color color)
        {
            if (1 < points)
            {
                Handles.DrawAAPolyLine(2f, points, _line);
            }
            else if (points == 1)
            {
                EditorGUI.DrawRect(new Rect(_line[0].x - 1f, _line[0].y - 1f, 2f, 2f), color);
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
            Event evt = Event.current;
            bool draw = evt.type == EventType.Repaint;
            bool click = evt.type == EventType.MouseDown && evt.button == 0 && track.Contains(evt.mousePosition);
            if (!_themeReady || (!draw && !click))
            {
                return;
            }

            int hit = -1;
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
                    until = Math.Min(Timeline.Time(frame + 1), time + MaxSampleGapSeconds);
                }

                float left = TrackX(in track, start, end, time);
                float right = TrackX(in track, start, end, until);
                Rect cell = new(Mathf.Min(left, track.xMax - 1f), track.y + 3f, Mathf.Max(1f, right - left), track.height - 6f);
                if (draw)
                {
                    Color color =
                        state.isPaused ? _pausedColor
                        : state.isHeld ? _heldColor
                        : _playingColor;
                    EditorGUI.DrawRect(cell, color);
                    float[] peaks = Timeline.VoicePeaks(frame, voice);
                    if (peaks != null)
                    {
                        (AudioClip clip, float seconds) = Timeline.Clip(frame, voice);
                        if (clip != null)
                        {
                            float duration = clip.length;
                            float untilSeconds = Timeline.VoiceClipEnd(frame, voice, duration, MaxSampleGapSeconds);
                            DrawWaveform(in cell, peaks, seconds / duration, untilSeconds / duration, _trackColor);
                        }
                    }

                    if (voice == _selectedVoice && frame == _cursor)
                    {
                        EditorGUI.DrawRect(new Rect(cell.x, cell.yMax - 2f, cell.width, 2f), _cursorColor);
                    }
                }
                else if (cell.Contains(evt.mousePosition))
                {
                    hit = frame;
                }
            }

            if (draw)
            {
                DrawCursor(in track, start, end, cursor);
            }
            else if (0 <= hit && SelectClip(hit, voice))
            {
                evt.Use();
                GUIUtility.ExitGUI();
            }
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
