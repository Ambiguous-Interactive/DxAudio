namespace WallstopStudios.DxAudio
{
    using System;
    using UnityEngine;

    /// <summary>One of the interchangeable takes a cue may play, and where in it playback begins.</summary>
    /// <remarks>
    /// The offset lives with the clip rather than on <see cref="SfxCue"/>, because a lead-in belongs to the
    /// recording, not to the beat. Pairing them in one object makes a clip without its offset impossible to
    /// author; two parallel lists agree only while every edit remembers to change both.
    /// </remarks>
    [Serializable]
    public sealed class SfxVariant
    {
        [SerializeField]
        private AudioClip _clip;

        [Min(0f)]
        [SerializeField]
        [Tooltip(
            "Where in this clip playback begins. Skips the clip's own lead-in so the sound lands on the frame "
                + "its beat does. Ignored by an engine-looped cue, which always restarts at zero."
        )]
        private float _startTimeSeconds;

        [Range(SfxVariation.MinimumLevelDecibels, 0f)]
        [SerializeField]
        [Tooltip("Level reduction for this recording before cue variation.")]
        private float _attenuationDecibels;

        public SfxVariant() { }

        public SfxVariant(AudioClip clip, float startTimeSeconds = 0f)
            : this(clip, startTimeSeconds, 0f) { }

        public SfxVariant(AudioClip clip, float startTimeSeconds, float attenuationDecibels)
        {
            _clip = clip;
            _startTimeSeconds = startTimeSeconds;
            _attenuationDecibels = SafeLevel(attenuationDecibels);
        }

        public AudioClip Clip => _clip;

        /// <summary>The authored offset, before it is clamped against the clip.</summary>
        public float StartTimeSeconds => _startTimeSeconds;

        /// <summary>Level reduction applied before per-play variation, in decibels.</summary>
        public float AttenuationDecibels => SafeLevel(_attenuationDecibels);

        internal float Gain
        {
            get
            {
                float level = AttenuationDecibels;
                return level == 0f ? 1f : AudioMath.DecibelsToLinear(level);
            }
        }

        /// <summary>Whether this slot holds a clip at all. An empty inspector slot does not.</summary>
        public bool IsPlayable => _clip != null;

        /// <summary>Where playback begins, clamped so it never starts at or past the clip's end.</summary>
        public float PlaybackStartSeconds => AudioClipStart.Clamped(_clip, _startTimeSeconds);

        private static float SafeLevel(float decibels)
        {
            return AudioMath.IsFinite(decibels) ? AudioMath.Clamp(decibels, SfxVariation.MinimumLevelDecibels, 0f) : 0f;
        }
    }

    /// <summary>Where playback of a clip begins, given what was authored for it.</summary>
    /// <remarks>
    /// <c>AudioSource.time</c> past the end of a clip starts a source that is already finished, which sounds
    /// exactly like a cue that never fired. Clamping in one place keeps sound effects and music agreeing.
    /// </remarks>
    public static class AudioClipStart
    {
        /// <summary>How far short of a clip's end playback may start.</summary>
        public const float MinimumAudibleSeconds = 0.01f;

        public static float Clamped(AudioClip clip, float authoredSeconds)
        {
            return clip == null ? 0f : Clamped(clip.length, authoredSeconds);
        }

        public static float Clamped(float clipLengthSeconds, float authoredSeconds)
        {
            if (authoredSeconds <= 0f)
            {
                return 0f;
            }

            float latestStart = Mathf.Max(0f, clipLengthSeconds - MinimumAudibleSeconds);
            return Mathf.Clamp(authoredSeconds, 0f, latestStart);
        }
    }
}
