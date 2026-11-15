namespace WallstopStudios.DxAudio
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Plays music per context with fades, temporary overrides, and ducks that compose.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><see cref="PlayContext"/> or <see cref="Play"/> sets what a screen or level plays. The same music
    /// asked for again keeps playing; music still fading out is faded back in from where it is.</item>
    /// <item><see cref="Override"/> plays something else until its lease is released, then hands back.</item>
    /// <item><see cref="Duck"/> holds the music down for a reason until the lease is released. Every live duck
    /// composes into the most restrictive volume and filters, so systems never need to know about each other.</item>
    /// <item><see cref="Muted"/> is the game's music on/off setting.</item>
    /// </list>
    /// Everything runs on unscaled time, so a paused game still fades. Filters are Unity's low- and high-pass
    /// components on each voice, switched off while open, so no mixer setup is needed.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("DxAudio/Music Player")]
    public sealed class MusicPlayer : MonoBehaviour
    {
        private const int VoiceCount = 3;
        private const float SweptReleaseSeconds = 0.5f;

        [SerializeField]
        private MusicLibrary _library;

        [SerializeField]
        [Tooltip("Silences the music while set. It keeps playing underneath, so unmuting resumes in time.")]
        private bool _muted;

        [SerializeField]
        [Tooltip("Keeps the music playing while AudioListener.pause is set, so a pause menu is not silent.")]
        private bool _playsWhilePaused = true;

        private readonly MusicDuckStack _ducks = new();
        private readonly Voice[] _voices = new Voice[VoiceCount];
        private MusicOverride<MusicTrack> _selection;
        private Voice _current;
        private MusicAttenuation _rampFrom = MusicAttenuation.Unattenuated;
        private MusicAttenuation _rampTo = MusicAttenuation.Unattenuated;
        private MusicRamp _duckProgress = MusicRamp.At(1f);
        private float _clock;

        public MusicLibrary Library
        {
            get => _library;
            set => _library = value;
        }

        /// <summary>The game's music on/off setting.</summary>
        public bool Muted
        {
            get => _muted;
            set
            {
                _muted = value;
                foreach (Voice voice in _voices)
                {
                    if (voice != null && voice.source != null)
                    {
                        voice.source.mute = value;
                    }
                }
            }
        }

        /// <summary>The music wanted now: an override when there is one, otherwise the context's.</summary>
        public MusicTrack Current => Selection.Current;

        public bool IsOverridden => Selection.IsOverridden;

        /// <summary>The attenuation applied this frame, partway through a duck's ramp or settled.</summary>
        public MusicAttenuation Attenuation { get; private set; } = MusicAttenuation.Unattenuated;

        /// <summary>What the live ducks compose to, which <see cref="Attenuation"/> is moving toward.</summary>
        public MusicAttenuation TargetAttenuation => _ducks.Current;

        /// <summary>Whether the current music is audible, rather than stopped or on its way out.</summary>
        public bool IsPlaying => _current != null && _current.source != null && _current.source.isPlaying;

        private MusicOverride<MusicTrack> Selection =>
            _selection ??= new MusicOverride<MusicTrack>(OnSelectionChanged, SameMusicComparer.Instance, IsPlayable);

        /// <summary>Plays the track the library lists for <paramref name="context"/>.</summary>
        /// <returns><see langword="false"/>, changing nothing, when the library lists none.</returns>
        public bool PlayContext(string context)
        {
            if (_library == null || !_library.TryGetTrack(context, out MusicTrack track))
            {
                return false;
            }

            Play(track);
            return true;
        }

        /// <summary>Sets the context's music, clearing any override. Null fades to silence.</summary>
        public void Play(MusicTrack track)
        {
            _ = Selection.SetContextTrack(track != null && track.IsPlayable ? track : null);
        }

        /// <summary>Fades the music out and clears any override.</summary>
        public void Stop()
        {
            _ = Selection.SetContextTrack(null);
        }

        /// <summary>Plays <paramref name="track"/> over the context's music until the lease is released or disposed.</summary>
        public MusicOverrideLease Override(MusicTrack track)
        {
            return Selection.Take(track);
        }

        public bool ReleaseOverride(in MusicOverrideLease lease)
        {
            return Selection.Release(lease);
        }

        /// <summary>Holds the music down for <paramref name="duck"/> until the lease is released.</summary>
        /// <param name="holder">
        /// Who holds a <see cref="MusicDuckHolders.Many"/> duck. A holder destroyed without releasing is
        /// released for it on the next frame.
        /// </param>
        public MusicDuckLease Duck(MusicDuck duck, UnityEngine.Object holder = null)
        {
            if (duck == null)
            {
                return MusicDuckLease.None;
            }

            MusicAttenuation before = _ducks.Current;
            MusicDuckLease lease = _ducks.Take(duck, duck.Attenuation, holder);
            if (!before.Equals(_ducks.Current))
            {
                RampTowardDucks(duck.ApplySeconds, duck.ApplyEase);
            }

            return lease;
        }

        /// <summary>Gives up exactly the duck <paramref name="lease"/> holds.</summary>
        /// <returns>Whether the music's target changed.</returns>
        public bool Release(in MusicDuckLease lease)
        {
            if (!_ducks.Release(lease))
            {
                return false;
            }

            ReleaseRamp(lease.reason as MusicDuck);
            return true;
        }

        /// <summary>Releases <paramref name="duck"/>, or <paramref name="holder"/>'s hold on a shared one.</summary>
        public bool Unduck(MusicDuck duck, UnityEngine.Object holder = null)
        {
            if (!_ducks.Release(duck, holder))
            {
                return false;
            }

            ReleaseRamp(duck);
            return true;
        }

        public bool IsDucked(MusicDuck duck)
        {
            return _ducks.IsActive(duck);
        }

        /// <summary>Every live duck and who holds it, for a diagnostic.</summary>
        public string DescribeDucks()
        {
            return _ducks.DescribeHolds();
        }

        /// <summary>Moves every fade and duck on by <paramref name="deltaSeconds"/>. Called each frame.</summary>
        internal void Tick(float deltaSeconds)
        {
            float step = Math.Max(0f, deltaSeconds);
            _clock += step;
            if (_ducks.SweepAbandoned(out _))
            {
                RampTowardDucks(SweptReleaseSeconds, MusicEase.InOutSine);
            }

            Attenuation = MusicAttenuation.Lerp(_rampFrom, _rampTo, _duckProgress.Advance(step));
            foreach (Voice voice in _voices)
            {
                if (voice == null || voice.source == null)
                {
                    continue;
                }

                float fade = voice.fade.Advance(step);
                if (voice.stop.TryFinish(_clock))
                {
                    voice.source.Stop();
                    voice.track = null;
                    voice.fadingOut = false;
                    continue;
                }

                Apply(voice, fade);
            }
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            foreach (Voice voice in _voices)
            {
                if (voice != null && voice.source != null)
                {
                    // OnDestroy runs only in Play mode for this component, and Destroy is deferred, so a voice
                    // already going down with the hierarchy is not destroyed twice.
                    Destroy(voice.source.gameObject);
                }
            }
        }

        private void OnSelectionChanged(MusicTrack next)
        {
            EnsureVoices();
            float fadeIn = _library != null ? _library.FadeInSeconds : 0f;
            MusicEase fadeInEase = _library != null ? _library.FadeInEase : MusicEase.Linear;
            if (next == null)
            {
                FadeOut(_current);
                _current = null;
                return;
            }

            bool soundingIsAudible = IsPlaying;
            bool sameAsSounding = _current != null && MusicTrack.SameMusic(_current.track, next);
            Voice reclaimable = FindFadingOut(next);
            switch (MusicHandOff.Decide(sameAsSounding, soundingIsAudible, reclaimable != null))
            {
                case MusicHandOffKind.KeepSounding:
                    return;
                case MusicHandOffKind.ReclaimOutgoing:
                    FadeOut(_current);
                    reclaimable.stop.Cancel();
                    reclaimable.fadingOut = false;
                    reclaimable.fade = reclaimable.fade.Retarget(1f, fadeIn, fadeInEase);
                    _current = reclaimable;
                    return;
                default:
                    FadeOut(_current);
                    _current = StartVoice(next, fadeIn, fadeInEase);
                    return;
            }
        }

        private Voice StartVoice(MusicTrack track, float fadeIn, MusicEase fadeInEase)
        {
            Voice voice = FreeVoice();
            AudioSource source = voice.source;
            source.Stop();
            source.clip = track.Clip;
            source.loop = track.Loop;
            source.time = track.StartTimeSeconds;
            source.mute = _muted;
            source.ignoreListenerPause = _playsWhilePaused;
            source.outputAudioMixerGroup = _library != null ? _library.MixerGroup : null;
            voice.track = track;
            voice.fadingOut = false;
            voice.stop.Cancel();
            voice.fade = new MusicRamp(0f, 1f, fadeIn, fadeInEase);
            Apply(voice, voice.fade.Value);
            source.Play();
            return voice;
        }

        private void FadeOut(Voice voice)
        {
            if (voice == null || voice.fadingOut || voice.source == null)
            {
                return;
            }

            // A voice not reporting playback (a clip still loading, a paused listener) is stopped outright,
            // so it cannot resume later under the music that replaced it.
            if (!voice.source.isPlaying)
            {
                voice.source.Stop();
                voice.track = null;
                voice.fade = MusicRamp.At(0f);
                return;
            }

            float fadeOut = _library != null ? _library.FadeOutSeconds : 0f;
            MusicEase fadeOutEase = _library != null ? _library.FadeOutEase : MusicEase.Linear;
            voice.fadingOut = true;
            voice.fade = voice.fade.Retarget(0f, fadeOut, fadeOutEase);
            voice.stop.Arm(_clock, fadeOut);
        }

        private Voice FindFadingOut(MusicTrack track)
        {
            foreach (Voice voice in _voices)
            {
                if (voice != null && voice.fadingOut && MusicTrack.SameMusic(voice.track, track))
                {
                    return voice;
                }
            }

            return null;
        }

        /// <summary>A silent voice, or else the quietest one fading out, cut short.</summary>
        private Voice FreeVoice()
        {
            Voice quietest = null;
            foreach (Voice voice in _voices)
            {
                if (voice == _current)
                {
                    continue;
                }

                if (!voice.source.isPlaying)
                {
                    return voice;
                }

                if (quietest == null || voice.fade.Value < quietest.fade.Value)
                {
                    quietest = voice;
                }
            }

            return quietest;
        }

        private void RampTowardDucks(float seconds, MusicEase ease)
        {
            _rampFrom = Attenuation;
            _rampTo = _ducks.Current;
            _duckProgress = new MusicRamp(0f, 1f, seconds, ease);
            Attenuation = MusicAttenuation.Lerp(_rampFrom, _rampTo, _duckProgress.Value);
        }

        private void ReleaseRamp(MusicDuck released)
        {
            float seconds = released != null ? released.ReleaseSeconds : SweptReleaseSeconds;
            MusicEase ease = released != null ? released.ReleaseEase : MusicEase.InOutSine;
            RampTowardDucks(seconds, ease);
        }

        private void Apply(Voice voice, float fade)
        {
            MusicAttenuation attenuation = Attenuation;
            float trim = voice.track != null ? voice.track.VolumeScale : 1f;
            voice.source.volume = trim * fade * attenuation.volumeScale;
            voice.lowPass.cutoffFrequency = attenuation.lowPassCutoffHz;
            voice.lowPass.enabled = attenuation.lowPassCutoffHz < MusicAttenuation.OpenLowPassCutoffHz;
            voice.highPass.cutoffFrequency = attenuation.highPassCutoffHz;
            voice.highPass.enabled = MusicAttenuation.OpenHighPassCutoffHz < attenuation.highPassCutoffHz;
        }

        private void EnsureVoices()
        {
            for (int index = 0; index < VoiceCount; index++)
            {
                if (_voices[index] != null && _voices[index].source != null)
                {
                    continue;
                }

                // Never saved with a scene in edit mode. In Play mode the flag would hide the voices from
                // FindObjectsOfType, so a game's own pause-all could not reach them.
                GameObject child = new($"Music Voice {index}")
                {
                    hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSave,
                };
                child.transform.SetParent(transform, worldPositionStays: false);
                AudioSource source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.mute = _muted;
                _voices[index] = new Voice(
                    source,
                    child.AddComponent<AudioLowPassFilter>(),
                    child.AddComponent<AudioHighPassFilter>()
                );
            }
        }

        private sealed class Voice
        {
            public readonly AudioSource source;
            public readonly AudioLowPassFilter lowPass;
            public readonly AudioHighPassFilter highPass;
            public readonly MusicStopSchedule stop = new();
            public MusicTrack track;
            public MusicRamp fade = MusicRamp.At(0f);
            public bool fadingOut;

            public Voice(AudioSource source, AudioLowPassFilter lowPass, AudioHighPassFilter highPass)
            {
                this.source = source;
                this.lowPass = lowPass;
                this.highPass = highPass;
                lowPass.enabled = false;
                highPass.enabled = false;
            }
        }

        private static bool IsPlayable(MusicTrack track)
        {
            return track.IsPlayable;
        }

        private sealed class SameMusicComparer : System.Collections.Generic.IEqualityComparer<MusicTrack>
        {
            public static readonly SameMusicComparer Instance = new();

            public bool Equals(MusicTrack first, MusicTrack second)
            {
                return MusicTrack.SameMusic(first, second);
            }

            public int GetHashCode(MusicTrack track)
            {
                return track == null || track.Clip == null ? 0 : track.Clip.GetHashCode();
            }
        }
    }
}
