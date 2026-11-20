namespace WallstopStudios.DxAudio
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Audio;

    /// <summary>Music per context, and how the music player fades between them.</summary>
    /// <remarks>
    /// A context is whatever a game changes music on: a screen, a level, an area. Each track names its own
    /// context, and <see cref="MusicPlayer.PlayContext"/> looks it up here.
    /// </remarks>
    [CreateAssetMenu(fileName = "MusicLibrary", menuName = "Wallstop Studios/DxAudio/Music Library")]
    public sealed class MusicLibrary : ScriptableObject, ISerializationCallbackReceiver
    {
        private const int CurrentEaseVersion = 1;

        private static event Action IndexesStale;

        public AudioMixerGroup MixerGroup => _mixerGroup;

        public IReadOnlyList<MusicTrack> Tracks => _tracks;

        public float FadeInSeconds => _fadeInSeconds;

        public MusicEase FadeInEase => _fadeInEase;

        public float FadeOutSeconds => _fadeOutSeconds;

        public MusicEase FadeOutEase => _fadeOutEase;

        [SerializeField]
        [Tooltip("The mixer group music plays through. Empty plays unrouted.")]
        private AudioMixerGroup _mixerGroup;

        [SerializeField]
        private List<MusicTrack> _tracks = new();

        [Min(0f)]
        [SerializeField]
        [Tooltip("Fade applied when a track starts.")]
        private float _fadeInSeconds = 1.5f;

        [SerializeField]
        private MusicEase _fadeInEase = MusicEase.OutSine;

        [Min(0f)]
        [SerializeField]
        [Tooltip("Fade applied when a track is replaced or stopped.")]
        private float _fadeOutSeconds = 1.2f;

        [SerializeField]
        private MusicEase _fadeOutEase = MusicEase.InOutSine;

        // Zero means the asset was written before the Unknown sentinel, when Linear was zero.
        [SerializeField]
        [HideInInspector]
        private int _easeVersion = CurrentEaseVersion;

        private readonly Dictionary<string, MusicTrack> _tracksByContext = new(StringComparer.Ordinal);
        private bool _indexBuilt;

        /// <summary>Builds a library in code, for tests and for games that author audio at runtime.</summary>
        public static MusicLibrary Create(IEnumerable<MusicTrack> tracks, float fadeInSeconds = 1.5f, float fadeOutSeconds = 1.2f, AudioMixerGroup mixerGroup = null)
        {
            MusicLibrary library = CreateInstance<MusicLibrary>();
            library._tracks = tracks == null ? new List<MusicTrack>() : new List<MusicTrack>(tracks);
            library._fadeInSeconds = Mathf.Max(0f, fadeInSeconds);
            library._fadeOutSeconds = Mathf.Max(0f, fadeOutSeconds);
            library._mixerGroup = mixerGroup;
            return library;
        }

        internal static void ForgetEveryIndex()
        {
            IndexesStale?.Invoke();
        }

        /// <inheritdoc />
        public void OnAfterDeserialize()
        {
            if (CurrentEaseVersion <= _easeVersion)
            {
                return;
            }

            _fadeInEase = MusicEasing.FromStoredBeforeUnknown((int)_fadeInEase);
            _fadeOutEase = MusicEasing.FromStoredBeforeUnknown((int)_fadeOutEase);
            _easeVersion = CurrentEaseVersion;
        }

        /// <inheritdoc />
        public void OnBeforeSerialize() { }

        /// <summary>The playable track listed for <paramref name="context"/>.</summary>
        public bool TryGetTrack(string context, out MusicTrack track)
        {
            if (string.IsNullOrWhiteSpace(context))
            {
                track = null;
                return false;
            }

            if (!_indexBuilt)
            {
                RebuildIndex();
            }

            return _tracksByContext.TryGetValue(context, out track) && track != null;
        }

        /// <summary>Drops the context index so the next lookup sees edits made since it was built.</summary>
        public void ForgetIndex()
        {
            _indexBuilt = false;
        }

        private void OnEnable()
        {
            _indexBuilt = false;
            IndexesStale += ForgetIndex;
        }

        private void OnDisable()
        {
            IndexesStale -= ForgetIndex;
        }

        private void OnValidate()
        {
            _indexBuilt = false;
        }

        private void RebuildIndex()
        {
            _indexBuilt = true;
            _tracksByContext.Clear();
            foreach (MusicTrack track in _tracks)
            {
                if (track == null || !track.IsPlayable)
                {
                    continue;
                }

                // A listed track with no context can never be looked up, which reads as a silent context.
                if (string.IsNullOrWhiteSpace(track.Context))
                {
                    Debug.LogWarning($"The music track '{track.name}' in '{name}' names no context, so nothing can look it up.", this);
                    continue;
                }

                if (!_tracksByContext.TryAdd(track.Context, track))
                {
                    Debug.LogWarning($"Two music tracks in '{name}' share the context '{track.Context}'; only the first will play.", this);
                }
            }
        }
    }
}
