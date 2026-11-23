namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEngine;

    internal sealed class DxAudioMixMonitorWindow : EditorWindow
    {
        private const double SampleIntervalSeconds = 0.25;

        internal MixMonitorDropCapture Capture { get; } = new();

        [SerializeField]
        private SfxPlayer _player;

        private double _nextSample;

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

        internal void SelectPlayer(SfxPlayer player)
        {
            if (Capture.Paused || player == _player || (player != null && EditorUtility.IsPersistent(player)))
            {
                return;
            }

            _player = player;
            SamplePlayer();
        }

        internal void SamplePlayer()
        {
            Capture.Sample(_player == null ? SfxDropCounts.None : _player.DropCounts);
        }

        private void OnEnable()
        {
            minSize = new Vector2(340f, 225f);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Capture.Reset();
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
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(Capture.Paused))
            {
                SfxPlayer selected = (SfxPlayer)EditorGUILayout.ObjectField("SFX Player", _player, typeof(SfxPlayer), allowSceneObjects: true);
                SelectPlayer(selected);
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to view drop counts.", MessageType.Info);
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
                    Capture.Paused = !Capture.Paused;
                    SamplePlayer();
                }

                GUILayout.Label($"Drops: {Capture.VisibleCount}  |  Silenced: {Capture.SilencedCount}", EditorStyles.miniLabel);
            }

            foreach (SfxDropReason reason in MixMonitorDropCapture.Reasons)
            {
                DrawDrop(reason);
            }
        }

        private void DrawDrop(SfxDropReason reason)
        {
            bool silenced = Capture.IsSilenced(reason);
            GUIContent label = DxAudioValidation.TryFromDrop(null, reason, out DxAudioValidation.Finding finding)
                ? new GUIContent($"{reason} ({DxAudioValidation.Id(finding.Rule)})", DxAudioValidation.Description(finding.Rule))
                : new GUIContent(reason.ToString());
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.ExpandWidth(true));
                GUILayout.Label(silenced ? "-" : Capture.Counts[reason].ToString(), GUILayout.Width(60f));
                if (GUILayout.Button(silenced ? "Show" : "Silence", GUILayout.Width(65f)))
                {
                    Capture.SetSilenced(reason, !silenced);
                }
            }
        }
    }
}
