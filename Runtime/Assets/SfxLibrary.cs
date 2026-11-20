namespace WallstopStudios.DxAudio
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Audio;

    /// <summary>A bank of sound effect cues, and how many voices play them.</summary>
    /// <remarks>
    /// Cues are found by <see cref="SfxCue.Key"/>, so a game can name a sound in data or in a message without
    /// holding the asset. A cue played by reference does not need to be listed here at all.
    /// </remarks>
    [CreateAssetMenu(fileName = "SfxLibrary", menuName = "DxAudio/Sfx Library")]
    public sealed class SfxLibrary : ScriptableObject
    {
        private static event Action IndexesStale;

        public AudioMixerGroup MixerGroup => _mixerGroup;

        public int VoiceCount => _voiceCount;

        public int LoopingVoiceLimit => _loopingVoiceLimit;

        public int DrawSeed => _drawSeed;

        public IReadOnlyList<SfxCue> Cues => _cues;

        [SerializeField]
        [Tooltip("The mixer group every cue plays through unless it names its own. Empty plays unrouted.")]
        private AudioMixerGroup _mixerGroup;

        [Min(1)]
        [SerializeField]
        [Tooltip("Voices one-shots and loops share. A one-shot arriving with every voice busy is dropped, not queued.")]
        private int _voiceCount = 16;

        [Min(0)]
        [SerializeField]
        [Tooltip(
            "How many of the voices looping cues may hold at once. The rest are always left for one-shots, so a "
                + "background bed cannot starve the beats the player is meant to hear."
        )]
        private int _loopingVoiceLimit = 6;

        [SerializeField]
        [Tooltip("Seeds the variant and pitch draws. A fixed value makes a recorded take repeat exactly; 0 seeds from the clock.")]
        private int _drawSeed;

        [SerializeField]
        private List<SfxCue> _cues = new();

        private readonly Dictionary<string, SfxCue> _cuesByKey = new(StringComparer.Ordinal);
        private bool _indexBuilt;

        /// <summary>Builds a library in code, for tests and for games that author audio at runtime.</summary>
        public static SfxLibrary Create(IEnumerable<SfxCue> cues, int voiceCount = 16, int loopingVoiceLimit = 6, AudioMixerGroup mixerGroup = null, int drawSeed = 0)
        {
            SfxLibrary library = CreateInstance<SfxLibrary>();
            library._cues = cues == null ? new List<SfxCue>() : new List<SfxCue>(cues);
            library._voiceCount = Mathf.Max(1, voiceCount);
            library._loopingVoiceLimit = Mathf.Max(0, loopingVoiceLimit);
            library._mixerGroup = mixerGroup;
            library._drawSeed = drawSeed;
            return library;
        }

        internal static void ForgetEveryIndex()
        {
            IndexesStale?.Invoke();
        }

        /// <summary>The playable cue listed under <paramref name="key"/>.</summary>
        public bool TryGetCue(string key, out SfxCue cue)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                cue = null;
                return false;
            }

            if (!_indexBuilt)
            {
                RebuildIndex();
            }

            return _cuesByKey.TryGetValue(key, out cue) && cue != null;
        }

        /// <summary>Drops the key index so the next lookup sees edits made since it was built.</summary>
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
            // Set before the walk: a duplicate key would otherwise rebuild, and re-warn, on every lookup.
            _indexBuilt = true;
            _cuesByKey.Clear();
            foreach (SfxCue cue in _cues)
            {
                if (cue == null || !cue.IsPlayable || string.IsNullOrWhiteSpace(cue.Key))
                {
                    continue;
                }

                // Reported rather than dropped silently: by ear, a cue that never plays is a cue that is wrong.
                if (!_cuesByKey.TryAdd(cue.Key, cue))
                {
                    Debug.LogWarning($"Two SFX cues in '{name}' share the key '{cue.Key}'; only the first will play.", this);
                }
            }
        }
    }
}
