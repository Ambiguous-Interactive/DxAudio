namespace WallstopStudios.DxAudio
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Audio;

    /// <summary>
    /// The sound one beat of a game makes: which takes it may pick from, how much they vary, how often it may
    /// fire, and what it is worth when every voice is busy.
    /// </summary>
    /// <remarks>
    /// Played by <see cref="SfxPlayer"/>, by reference or by <see cref="Key"/> through an
    /// <see cref="SfxLibrary"/>. The cue itself is the identity for rate limits and no-repeat memory, so two
    /// cues never share either.
    /// </remarks>
    [CreateAssetMenu(fileName = "SfxCue", menuName = "DxAudio/Sfx Cue")]
    public sealed class SfxCue : ScriptableObject
    {
        private const float DefaultMinimumIntervalSeconds = 0.05f;

        public string Key => _key;

        public IReadOnlyList<SfxVariant> Variants => _variants;

        public float Volume => _volume;

        public float PitchJitter => _pitchJitter;

        public SfxVariation Variation => _variation;

        public float MinimumIntervalSeconds => _minimumIntervalSeconds;

        public SfxPriority Priority => _priority;

        /// <summary>Whether this cue is held for as long as its state lasts, rather than played once.</summary>
        public bool IsLooping => _loop;

        public bool IgnoresListenerPause => _ignoresListenerPause;

        public AudioMixerGroup MixerGroupOverride => _mixerGroupOverride;

        public float SpatialBlend => _spatialBlend;

        public bool IsPositional => 0f < _spatialBlend;

        public float FalloffStartUnits => _falloffStartUnits;

        /// <summary>Distance past which a placed cue is silent, never nearer than its falloff start.</summary>
        /// <remarks>
        /// Held here rather than trusted, because Unity silently reorders <c>minDistance</c> and
        /// <c>maxDistance</c> when they cross, and the cue is then audible everywhere.
        /// </remarks>
        public float FalloffEndUnits => Mathf.Max(_falloffStartUnits, _falloffEndUnits);

        public bool IsHeardCentred => _heardCentred;

        public SfxCue FollowsTakeOf => _followsTakeOf;

        /// <summary>The effects this cue's voices run, or null to play the take untouched.</summary>
        public DspChainAsset Chain => _chain;

        /// <summary>The graph this cue's voices run after <see cref="Chain" />, or null to run none.</summary>
        public DspGraphAsset Graph => _graph;

        /// <summary>How many variants hold a clip. Selection draws from this, so an empty slot is never picked.</summary>
        public int PlayableVariantCount
        {
            get
            {
                int count = 0;
                foreach (SfxVariant variant in _variants)
                {
                    if (variant != null && variant.IsPlayable)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool IsPlayable => 0 < PlayableVariantCount;

        internal int TakeRevision { get; private set; }

        [SerializeField]
        [Tooltip("Looks this cue up in an SfxLibrary. Optional when the cue is only played by reference.")]
        private string _key;

        [SerializeField]
        [Tooltip("Interchangeable recordings. Each take has its own start offset and level.")]
        private List<SfxVariant> _variants = new();

        [Range(0f, 1f)]
        [SerializeField]
        private float _volume = 1f;

        [Range(0f, 0.5f)]
        [SerializeField]
        [Tooltip("Legacy random pitch offset either side of 1. Set Pitch in Cents overrides this range.")]
        private float _pitchJitter = 0.08f;

        [SerializeField]
        private SfxVariation _variation;

        [Min(0f)]
        [SerializeField]
        [Tooltip("Shortest gap between two plays of this one-shot cue. Zero lets every request through. Loops are never limited.")]
        private float _minimumIntervalSeconds = DefaultMinimumIntervalSeconds;

        [SerializeField]
        [Tooltip(
            "What this cue is worth when every voice is busy. A cue may take a voice from one of strictly lower "
                + "priority, never from an equal, so frequent texture cannot starve a beat the player must hear."
        )]
        private SfxPriority _priority = SfxPriority.Medium;

        [SerializeField]
        [Tooltip("Held until whoever started it stops it, instead of played once. Only for a sound that stands for a " + "state, because something has to end it.")]
        private bool _loop;

        [SerializeField]
        [Tooltip("Keeps playing while AudioListener.pause is set, for menus shown over a paused game.")]
        private bool _ignoresListenerPause;

        [SerializeField]
        [Tooltip("Plays through this mixer group instead of the library's. Empty uses the library's.")]
        private AudioMixerGroup _mixerGroupOverride;

        [Range(0f, 1f)]
        [SerializeField]
        [Tooltip(
            "0 plays this cue flat, the same from anywhere. 1 places it in the world, so it pans and fades with "
                + "distance. Only for a sound whose source the player can see from far away."
        )]
        private float _spatialBlend;

        [Min(0f)]
        [SerializeField]
        [Tooltip("Distance within which a placed cue plays at full volume.")]
        private float _falloffStartUnits = 10f;

        [Min(0f)]
        [SerializeField]
        [Tooltip("Distance past which a placed cue is silent. Linear in between.")]
        private float _falloffEndUnits = 40f;

        [SerializeField]
        [Tooltip("A placed cue that still fades with distance but is never panned left or right.")]
        private bool _heardCentred;

        [SerializeField]
        [Tooltip("Another cue whose last take number this one plays, so two halves of one performance match. Empty " + "draws at random.")]
        private SfxCue _followsTakeOf;

        [SerializeField]
        [Tooltip(
            "Effects this cue's voices run, in order. Empty plays the take untouched. A voice builds its own copy, "
                + "cleared at the start of every play, so a pooled voice never inherits the last cue's envelope."
        )]
        private DspChainAsset _chain;

        [SerializeField]
        [Tooltip(
            "A graph this cue's voices run after the chain, with its branches side by side. Empty runs no graph. A "
                + "voice builds its own copy, cleared at the start of every play."
        )]
        private DspGraphAsset _graph;

        /// <summary>Builds a cue in code, for tests and for games that author cues at runtime.</summary>
        public static SfxCue Create(
            IEnumerable<SfxVariant> variants,
            string key = null,
            SfxPriority priority = SfxPriority.Medium,
            float minimumIntervalSeconds = DefaultMinimumIntervalSeconds,
            bool loop = false,
            float volume = 1f,
            float pitchJitter = 0f,
            float spatialBlend = 0f,
            float falloffStartUnits = 10f,
            float falloffEndUnits = 40f,
            bool heardCentred = false,
            SfxCue followsTakeOf = null,
            bool ignoresListenerPause = false,
            AudioMixerGroup mixerGroupOverride = null,
            DspChainAsset chain = null
        )
        {
            SfxCue cue = CreateInstance<SfxCue>();
            cue._key = key;
            cue._variants = variants == null ? new List<SfxVariant>() : new List<SfxVariant>(variants);
            cue._priority = priority;
            cue._minimumIntervalSeconds = minimumIntervalSeconds;
            cue._loop = loop;
            cue._volume = volume;
            cue._pitchJitter = pitchJitter;
            cue._spatialBlend = Mathf.Clamp01(spatialBlend);
            cue._falloffStartUnits = Mathf.Max(0f, falloffStartUnits);
            cue._falloffEndUnits = Mathf.Max(0f, falloffEndUnits);
            cue._heardCentred = heardCentred;
            cue._followsTakeOf = followsTakeOf;
            cue._ignoresListenerPause = ignoresListenerPause;
            cue._mixerGroupOverride = mixerGroupOverride;
            cue._chain = chain;
            return cue;
        }

        /// <summary>Builds a cue that runs a graph after its chain, for tests and for games that author audio at runtime.</summary>
        /// <remarks>
        /// A distinct name, not one more overload: a second <c>Create</c> taking a graph would make an existing call that
        /// passes a null key ambiguous, and a caller compiled against the published package would stop compiling.
        /// </remarks>
        public static SfxCue CreateWithGraph(
            IEnumerable<SfxVariant> variants,
            DspGraphAsset graph,
            string key = null,
            SfxPriority priority = SfxPriority.Medium,
            float minimumIntervalSeconds = DefaultMinimumIntervalSeconds,
            bool loop = false,
            float volume = 1f,
            float pitchJitter = 0f,
            float spatialBlend = 0f,
            float falloffStartUnits = 10f,
            float falloffEndUnits = 40f,
            bool heardCentred = false,
            SfxCue followsTakeOf = null,
            bool ignoresListenerPause = false,
            AudioMixerGroup mixerGroupOverride = null,
            DspChainAsset chain = null
        )
        {
            SfxCue cue = Create(
                variants,
                key,
                priority,
                minimumIntervalSeconds,
                loop,
                volume,
                pitchJitter,
                spatialBlend,
                falloffStartUnits,
                falloffEndUnits,
                heardCentred,
                followsTakeOf,
                ignoresListenerPause,
                mixerGroupOverride,
                chain
            );
            cue._graph = graph;
            return cue;
        }

        /// <summary>Builds a cue with opt-in per-play variation and optional effects.</summary>
        public static SfxCue CreateWithVariation(
            IEnumerable<SfxVariant> variants,
            SfxVariation variation,
            string key = null,
            SfxPriority priority = SfxPriority.Medium,
            float minimumIntervalSeconds = DefaultMinimumIntervalSeconds,
            bool loop = false,
            float volume = 1f,
            float pitchJitter = 0f,
            float spatialBlend = 0f,
            float falloffStartUnits = 10f,
            float falloffEndUnits = 40f,
            bool heardCentred = false,
            SfxCue followsTakeOf = null,
            bool ignoresListenerPause = false,
            AudioMixerGroup mixerGroupOverride = null,
            DspChainAsset chain = null,
            DspGraphAsset graph = null
        )
        {
            SfxCue cue = CreateWithGraph(
                variants,
                graph,
                key,
                priority,
                minimumIntervalSeconds,
                loop,
                volume,
                pitchJitter,
                spatialBlend,
                falloffStartUnits,
                falloffEndUnits,
                heardCentred,
                followsTakeOf,
                ignoresListenerPause,
                mixerGroupOverride,
                chain
            );
            cue._variation = variation;
            return cue;
        }

        /// <summary>The pitch for a draw of <paramref name="unitRandom"/> in [0, 1].</summary>
        public float PitchFor(float unitRandom)
        {
            return _variation.Pitch(_pitchJitter, unitRandom);
        }

        /// <summary>The <paramref name="playableIndex"/>-th variant that holds a clip, counting only those.</summary>
        public bool TryGetPlayableVariant(int playableIndex, out SfxVariant variant)
        {
            if (0 <= playableIndex)
            {
                int seen = 0;
                foreach (SfxVariant candidate in _variants)
                {
                    if (candidate == null || !candidate.IsPlayable)
                    {
                        continue;
                    }

                    if (seen == playableIndex)
                    {
                        variant = candidate;
                        return true;
                    }

                    seen++;
                }
            }

            variant = null;
            return false;
        }

        /// <summary>Where playback of <paramref name="variant"/> begins under this cue, in seconds.</summary>
        /// <remarks>
        /// <c>AudioSource.loop</c> restarts at sample zero, so an engine-looped cue starts there too: trimming
        /// its first pass only would put a hitch at every loop point after it.
        /// </remarks>
        public float PlaybackStartSecondsFor(SfxVariant variant)
        {
            if (variant == null || _loop)
            {
                return 0f;
            }

            return variant.PlaybackStartSeconds;
        }

        private void OnValidate()
        {
            ++TakeRevision;
            SfxLibrary.ForgetEveryIndex();
        }
    }
}
