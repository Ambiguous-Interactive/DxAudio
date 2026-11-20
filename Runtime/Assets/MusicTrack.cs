namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>One piece of music and how it plays: its trim, whether it loops, and where it starts.</summary>
    [CreateAssetMenu(fileName = "MusicTrack", menuName = "Wallstop Studios/DxAudio/Music Track")]
    public sealed class MusicTrack : ScriptableObject
    {
        public string Context => _context;

        public AudioClip Clip => _clip;

        public bool Loop => _loop;

        public float VolumeScale => _volumeScale;

        /// <summary>The effects this track's voice runs, or null to play the clip untouched.</summary>
        public DspChainAsset Chain => _chain;

        /// <summary>The graph this track's voice runs after <see cref="Chain" />, or null to run none.</summary>
        public DspGraphAsset Graph => _graph;

        public bool IsPlayable => _clip != null;

        /// <summary>What the track asks for; <see cref="ResolvedClipLoadType" /> turns it into a concrete value.</summary>
        public MusicClipLoadType ClipLoadType => _clipLoadType;

        /// <summary>The load type this track's clip should use, from its preference and the clip's length.</summary>
        public MusicClipLoadType ResolvedClipLoadType => MusicLoadPolicy.Resolve(_clip == null ? 0f : _clip.length, _clipLoadType);

        /// <summary>Where playback begins, clamped so it never starts at or past the clip's end.</summary>
        public float StartTimeSeconds => AudioClipStart.Clamped(_clip, _startTimeSeconds);

        [SerializeField]
        [Tooltip("The context this plays for (a screen, a level, an area) when looked up through a MusicLibrary. " + "Optional when the track is only played by reference.")]
        private string _context;

        [SerializeField]
        [Tooltip("What plays. A track with no clip is refused rather than played as silence.")]
        private AudioClip _clip;

        [SerializeField]
        [Tooltip("AudioClip carries no loop flag of its own; looping is a property of playback.")]
        private bool _loop = true;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip("Per-track trim, so one loud master does not force every other track to be re-authored.")]
        private float _volumeScale = 1f;

        [Min(0f)]
        [SerializeField]
        [Tooltip("Where the track begins on its first play. A looping track still loops back to zero, so this skips " + "an intro rather than setting a loop point.")]
        private float _startTimeSeconds;

        [SerializeField]
        [Tooltip(
            "How much of the clip sits in memory while it plays. Auto streams clips longer than 30 seconds and "
                + "decompresses the rest. The clip's import settings hold the real value, which every user of the "
                + "clip shares; the track inspector applies it."
        )]
        private MusicClipLoadType _clipLoadType = MusicClipLoadType.Auto;

        [SerializeField]
        [Tooltip("Effects this track's voice runs, in order. Empty plays the clip untouched. The voice clears them when the track starts.")]
        private DspChainAsset _chain;

        [SerializeField]
        [Tooltip("A graph this track's voice runs after the chain, with its branches side by side. Empty runs no graph.")]
        private DspGraphAsset _graph;

        /// <summary>Builds a track in code, for tests and for games that author audio at runtime.</summary>
        public static MusicTrack Create(
            AudioClip clip,
            string context = null,
            bool loop = true,
            float volumeScale = 1f,
            float startTimeSeconds = 0f,
            MusicClipLoadType clipLoadType = MusicClipLoadType.Auto,
            DspChainAsset chain = null
        )
        {
            MusicTrack track = CreateInstance<MusicTrack>();
            track._clip = clip;
            track._context = context;
            track._loop = loop;
            track._volumeScale = Mathf.Clamp01(volumeScale);
            track._startTimeSeconds = Mathf.Max(0f, startTimeSeconds);
            track._clipLoadType = clipLoadType;
            track._chain = chain;
            return track;
        }

        /// <summary>Builds a track that runs a graph after its chain, for tests and for games that author audio at runtime.</summary>
        /// <remarks>
        /// A distinct name, not one more overload: a second <c>Create</c> taking a graph would make an existing call that
        /// passes a null context ambiguous, and a caller compiled against the published package would stop compiling.
        /// </remarks>
        public static MusicTrack CreateWithGraph(
            AudioClip clip,
            DspGraphAsset graph,
            string context = null,
            bool loop = true,
            float volumeScale = 1f,
            float startTimeSeconds = 0f,
            MusicClipLoadType clipLoadType = MusicClipLoadType.Auto,
            DspChainAsset chain = null
        )
        {
            MusicTrack track = Create(clip, context, loop, volumeScale, startTimeSeconds, clipLoadType, chain);
            track._graph = graph;
            return track;
        }

        /// <summary>Whether two tracks would sound the same, so switching between them is not a change.</summary>
        /// <remarks>
        /// By value, not reference: two contexts that share a clip should keep it playing across the switch
        /// rather than restart it. The context is not compared, for the same reason. The chain and the graph are
        /// compared by asset, because a track that names a different one is heard differently even with the same clip.
        /// </remarks>
        public static bool SameMusic(MusicTrack first, MusicTrack second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }

            if (first == null || second == null)
            {
                return false;
            }

            return first._clip == second._clip
                && first._loop == second._loop
                && Mathf.Approximately(first._volumeScale, second._volumeScale)
                && Mathf.Approximately(first._startTimeSeconds, second._startTimeSeconds)
                && first._chain == second._chain
                && first._graph == second._graph;
        }

        private void OnValidate()
        {
            MusicLibrary.ForgetEveryIndex();
        }
    }
}
