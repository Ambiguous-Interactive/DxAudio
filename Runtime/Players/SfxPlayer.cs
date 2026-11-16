namespace WallstopStudios.DxAudio
{
    using System;
    using UnityEngine;

    /// <summary>A claim on one held voice, returned by <see cref="SfxPlayer.TryPlayLooping(SfxCue, out SfxLoopHandle)"/>.</summary>
    /// <remarks>
    /// The lease number makes stopping safe. A bare voice index would let an owner that stops twice, or stops
    /// after the voice was reused, silence whatever plays there now. The player advances the lease on every
    /// start and stop, so a stale handle matches nothing and stopping it does nothing.
    /// </remarks>
    public readonly struct SfxLoopHandle : IEquatable<SfxLoopHandle>
    {
        public readonly int voice;
        public readonly int lease;

        public SfxLoopHandle(int voice, int lease)
        {
            this.voice = voice;
            this.lease = lease;
        }

        public bool Equals(SfxLoopHandle other)
        {
            return voice == other.voice && lease == other.lease;
        }

        public override bool Equals(object obj)
        {
            return obj is SfxLoopHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (voice * 397) ^ lease;
        }
    }

    /// <summary>
    /// Plays sound effect cues on a fixed pool of voices, with priority, per-cue rate limits and no-repeat
    /// variant picks.
    /// </summary>
    /// <remarks>
    /// The voices are made once, as children, so playing a cue never allocates. A cue that finds no voice is
    /// dropped rather than queued: a late sound is a wrong sound. Wire a game's own events to
    /// <see cref="Play(SfxCue)"/>, and its sound setting to <see cref="Muted"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("DxAudio/Sfx Player")]
    public sealed class SfxPlayer : MonoBehaviour
    {
        [SerializeField]
        private SfxLibrary _library;

        [SerializeField]
        [Tooltip("Silences every voice while set. Cues still claim voices and rate limits, so loops resume on unmute.")]
        private bool _muted;

        [SerializeField]
        [Tooltip("The listener a centred cue is placed in front of. Empty uses the main camera's.")]
        private AudioListener _listener;

        private bool _paused;

        private readonly SfxThrottle<SfxCue> _throttle = new();
        private readonly SfxSelection<SfxCue> _selection = new();

        private AudioSource[] _voices = Array.Empty<AudioSource>();
        private SfxVoiceState[] _states = Array.Empty<SfxVoiceState>();
        private SfxPriority[] _priorities = Array.Empty<SfxPriority>();
        private float[] _startedAt = Array.Empty<float>();
        private bool[] _held = Array.Empty<bool>();
        private int[] _leases = Array.Empty<int>();
        private int _heldCount;
        private int _nextLease;
        private int _roundRobin;
        private System.Random _random;
        private int _droppedNotPlayable;
        private int _droppedThrottled;
        private int _droppedNoVoice;
        private int _droppedLoopLimit;
        private int _droppedWrongPlayMethod;

        /// <summary>Raised for every cue that did not play, and why.</summary>
        public event Action<SfxCue, SfxDropReason> CueDropped;

        /// <summary>The bank cues are looked up in, and the source of the voice count. Setting it rebuilds the voices.</summary>
        public SfxLibrary Library
        {
            get => _library;
            set
            {
                _library = value;
                BuildVoices();
            }
        }

        /// <summary>The game's sound on/off setting. Muting keeps every voice's state, so loops resume on unmute.</summary>
        public bool Muted
        {
            get => _muted;
            set
            {
                _muted = value;
                foreach (AudioSource voice in _voices)
                {
                    if (voice != null)
                    {
                        voice.mute = value;
                    }
                }
            }
        }

        public AudioListener Listener
        {
            get => _listener;
            set => _listener = value;
        }

        /// <summary>Whether every sounding voice is held at its playhead. Loops and leases are kept while paused.</summary>
        /// <remarks>
        /// A voice started during the pause waits at its start. A one-shot voice that is paused may be reused by a cue
        /// that starts during the pause, because neither is audible.
        /// </remarks>
        public bool Paused
        {
            get => _paused;
            set
            {
                if (_paused == value)
                {
                    return;
                }

                _paused = value;
                foreach (AudioSource voice in _voices)
                {
                    if (voice == null)
                    {
                        continue;
                    }

                    if (value)
                    {
                        voice.Pause();
                    }
                    else
                    {
                        voice.UnPause();
                    }
                }
            }
        }

        public int VoiceCount => _voices.Length;

        public int HeldVoiceCount => _heldCount;

        /// <summary>Every cue this player dropped, counted by reason. Reading it allocates nothing.</summary>
        public SfxDropCounts DropCounts =>
            new(_droppedNotPlayable, _droppedThrottled, _droppedNoVoice, _droppedLoopLimit, _droppedWrongPlayMethod);

        /// <summary>What voice <paramref name="voice"/> is doing, for a diagnostic.</summary>
        /// <param name="lease">The loop lease when a looping cue holds the voice, otherwise zero.</param>
        /// <returns><see langword="false"/> when the pool has no such voice.</returns>
        public bool TryGetVoiceState(int voice, out SfxVoiceState state, out int lease)
        {
            if (voice < 0 || _voices.Length <= voice)
            {
                state = default;
                lease = 0;
                return false;
            }

            AudioSource source = _voices[voice];
            bool usable = source != null;
            if (usable && VoiceClip.HasLostSound(source))
            {
                // A source whose clip was released, or whose data was unloaded while it played, reports playback
                // while it makes no sound. It is not busy, and it is silent, so stop it while the pool is being read
                // anyway.
                source.Stop();
            }

            state = new SfxVoiceState(
                usable,
                VoiceClip.IsSounding(source),
                _held[voice],
                _priorities[voice],
                _startedAt[voice]
            );
            lease = _held[voice] ? _leases[voice] : 0;
            return true;
        }

        /// <summary>Plays the cue the library lists under <paramref name="key"/>, flat.</summary>
        public bool Play(string key)
        {
            return TryResolve(key, out SfxCue cue) && Play(cue);
        }

        /// <summary>Plays the cue the library lists under <paramref name="key"/>, at <paramref name="position"/>.</summary>
        public bool Play(string key, Vector3 position)
        {
            return TryResolve(key, out SfxCue cue) && Play(cue, position);
        }

        /// <summary>Plays a one-shot cue, flat.</summary>
        /// <returns>Whether it started. <see cref="CueDropped"/> says why not.</returns>
        public bool Play(SfxCue cue)
        {
            return TryStart(cue, looping: false, position: null, out _);
        }

        /// <summary>Plays a one-shot cue at <paramref name="position"/>. A cue with no spatial blend still plays flat.</summary>
        public bool Play(SfxCue cue, Vector3 position)
        {
            return TryStart(cue, looping: false, position, out _);
        }

        /// <summary>Starts a looping cue that holds its voice until <see cref="StopLoop"/> is given the handle.</summary>
        public bool TryPlayLooping(SfxCue cue, out SfxLoopHandle handle)
        {
            return TryStart(cue, looping: true, position: null, out handle);
        }

        public bool TryPlayLooping(SfxCue cue, Vector3 position, out SfxLoopHandle handle)
        {
            return TryStart(cue, looping: true, position, out handle);
        }

        /// <summary>Stops the loop <paramref name="handle"/> holds. A stale handle stops nothing.</summary>
        /// <returns>Whether a loop was stopped.</returns>
        public bool StopLoop(in SfxLoopHandle handle)
        {
            int index = handle.voice;
            if (index < 0 || _voices.Length <= index || !_held[index] || _leases[index] != handle.lease)
            {
                return false;
            }

            Release(index);
            AudioSource voice = _voices[index];
            if (voice != null)
            {
                voice.Stop();
                voice.loop = false;
            }

            return true;
        }

        /// <summary>Stops every voice, loops included, and forgets every rate limit and take.</summary>
        public void StopAll()
        {
            for (int index = 0; index < _voices.Length; index++)
            {
                if (_held[index])
                {
                    Release(index);
                }

                AudioSource voice = _voices[index];
                if (voice != null)
                {
                    voice.Stop();
                    voice.loop = false;
                }
            }

            _throttle.Clear();
            _selection.Clear();
        }

        private void Awake()
        {
            if (_library != null && _voices.Length == 0)
            {
                BuildVoices();
            }
        }

        private void OnDestroy()
        {
            CueDropped = null;
            DestroyVoices();
        }

        /// <summary>Releases every hold whose voice or clip is gone, so it stops counting against the limit.</summary>
        /// <remarks>
        /// Unity clears a source's clip when that clip's asset is destroyed, and leaves the clip assigned when its audio
        /// data is unloaded. Both leave the source reporting playback with no sound. Without this sweep that voice stays
        /// held, and silent, for the rest of the session. It runs from <c>Update</c> and again before every play, so a
        /// released loop is freed within a frame.
        /// </remarks>
        internal void ReclaimLostHolds()
        {
            if (_heldCount == 0)
            {
                return;
            }

            for (int index = 0; index < _voices.Length; index++)
            {
                if (!_held[index])
                {
                    continue;
                }

                AudioSource voice = _voices[index];
                if (!VoiceClip.HasLostSound(voice))
                {
                    continue;
                }

                Release(index);
                if (voice != null)
                {
                    voice.Stop();
                    voice.loop = false;
                }
            }
        }

        private void Update()
        {
            ReclaimLostHolds();
        }

        private bool TryResolve(string key, out SfxCue cue)
        {
            if (_library != null && _library.TryGetCue(key, out cue))
            {
                return true;
            }

            cue = null;
            return Drop(null, SfxDropReason.NotPlayable);
        }

        private bool TryStart(SfxCue cue, bool looping, Vector3? position, out SfxLoopHandle handle)
        {
            handle = default;
            if (cue == null || !cue.IsPlayable)
            {
                return Drop(cue, SfxDropReason.NotPlayable);
            }

            if (cue.IsLooping != looping)
            {
                return Drop(cue, SfxDropReason.WrongPlayMethod);
            }

            if (_voices.Length == 0)
            {
                BuildVoices();
                if (_voices.Length == 0)
                {
                    return Drop(cue, SfxDropReason.NoVoice);
                }
            }

            // A loop stands for a state, so it is never rate-limited: stopping and restarting one in a
            // frame, or two owners starting the same hum together, must both get their loop.
            float now = Time.unscaledTime;
            if (!looping && !_throttle.IsReady(cue, now, cue.MinimumIntervalSeconds))
            {
                return Drop(cue, SfxDropReason.Throttled);
            }

            ReclaimLostHolds();

            int loopingLimit = _library != null ? _library.LoopingVoiceLimit : _voices.Length;
            if (looping && loopingLimit <= _heldCount)
            {
                return Drop(cue, SfxDropReason.LoopLimit);
            }

            ReadStates();
            if (!SfxVoiceArbiter.TryChoose(_states, cue.Priority, _roundRobin, out int index, out _))
            {
                return Drop(cue, SfxDropReason.NoVoice);
            }

            if (!TryPickVariant(cue, out SfxVariant variant))
            {
                return Drop(cue, SfxDropReason.NotPlayable);
            }

            if (!looping)
            {
                _ = _throttle.TryBegin(cue, now, cue.MinimumIntervalSeconds);
            }

            _roundRobin = index + 1;
            AudioSource voice = _voices[index];
            voice.Stop();
            Configure(voice, cue, variant, position);
            _priorities[index] = cue.Priority;
            _startedAt[index] = now;
            _leases[index] = ++_nextLease;
            if (looping)
            {
                _held[index] = true;
                _heldCount++;
                handle = new SfxLoopHandle(index, _leases[index]);
            }

            voice.Play();
            if (_paused)
            {
                voice.Pause();
            }

            return true;
        }

        private bool TryPickVariant(SfxCue cue, out SfxVariant variant)
        {
            int count = cue.PlayableVariantCount;
            if (!_selection.TryFollow(cue, cue.FollowsTakeOf, count, out int index))
            {
                float draw = (float)NextRandom();
                if (!_selection.TryNext(cue, count, draw, out index))
                {
                    variant = null;
                    return false;
                }
            }

            return cue.TryGetPlayableVariant(index, out variant);
        }

        private void Configure(AudioSource voice, SfxCue cue, SfxVariant variant, Vector3? position)
        {
            voice.clip = variant.Clip;
            voice.time = cue.PlaybackStartSecondsFor(variant);
            voice.volume = cue.Volume;
            voice.pitch = cue.PitchFor((float)NextRandom());
            voice.loop = cue.IsLooping;
            voice.mute = _muted;
            voice.ignoreListenerPause = cue.IgnoresListenerPause;
            voice.outputAudioMixerGroup =
                cue.MixerGroupOverride != null ? cue.MixerGroupOverride
                : _library != null ? _library.MixerGroup
                : null;

            bool placed = position.HasValue && cue.IsPositional;
            voice.spatialBlend = placed ? cue.SpatialBlend : 0f;
            if (!placed)
            {
                voice.transform.localPosition = Vector3.zero;
                return;
            }

            voice.minDistance = cue.FalloffStartUnits;
            voice.maxDistance = cue.FalloffEndUnits;
            voice.transform.position = cue.IsHeardCentred ? Centred(position.Value) : position.Value;
        }

        /// <summary>The same distance from the listener as <paramref name="source"/>, straight ahead of it.</summary>
        private Vector3 Centred(Vector3 source)
        {
            AudioListener listener = _listener;
            if (listener == null)
            {
                Camera main = Camera.main;
                listener = main != null ? main.GetComponent<AudioListener>() : null;
            }

            if (listener == null)
            {
                return source;
            }

            Transform heardFrom = listener.transform;
            float distance = Vector3.Distance(source, heardFrom.position);
            return heardFrom.position + (heardFrom.forward * distance);
        }

        private void ReadStates()
        {
            for (int index = 0; index < _voices.Length; index++)
            {
                _ = TryGetVoiceState(index, out _states[index], out _);
            }
        }

        private void Release(int index)
        {
            _held[index] = false;
            _heldCount--;
            _leases[index] = ++_nextLease;
        }

        private bool Drop(SfxCue cue, SfxDropReason reason)
        {
            CountDrop(reason);
            CueDropped?.Invoke(cue, reason);
            return false;
        }

        private void CountDrop(SfxDropReason reason)
        {
            switch (reason)
            {
                case SfxDropReason.NotPlayable:
                    _droppedNotPlayable++;
                    break;
                case SfxDropReason.Throttled:
                    _droppedThrottled++;
                    break;
                case SfxDropReason.NoVoice:
                    _droppedNoVoice++;
                    break;
                case SfxDropReason.LoopLimit:
                    _droppedLoopLimit++;
                    break;
                case SfxDropReason.WrongPlayMethod:
                    _droppedWrongPlayMethod++;
                    break;
                default:
                    break;
            }
        }

        private double NextRandom()
        {
            if (_random == null)
            {
                int seed = _library != null && _library.DrawSeed != 0 ? _library.DrawSeed : Environment.TickCount;
                _random = new System.Random(seed);
            }

            return _random.NextDouble();
        }

        private void DestroyVoices()
        {
            foreach (AudioSource old in _voices)
            {
                if (old != null)
                {
                    DestroyVoice(old.gameObject);
                }
            }
        }

        private void BuildVoices()
        {
            DestroyVoices();
            int count = _library != null ? _library.VoiceCount : 0;
            _voices = new AudioSource[count];
            _states = new SfxVoiceState[count];
            _priorities = new SfxPriority[count];
            _startedAt = new float[count];
            _held = new bool[count];
            _leases = new int[count];
            _heldCount = 0;
            _roundRobin = 0;
            _random = null;
            for (int index = 0; index < count; index++)
            {
                // Never saved with a scene in edit mode. In Play mode the flag would hide the voices from
                // FindObjectsOfType, so a game's own pause-all could not reach them.
                GameObject child = new($"Sfx Voice {index}")
                {
                    hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSave,
                };
                child.transform.SetParent(transform, worldPositionStays: false);
                AudioSource voice = child.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.rolloffMode = AudioRolloffMode.Linear;

                // A moving listener would otherwise pitch-bend every placed cue by its own speed.
                voice.dopplerLevel = 0f;
                voice.mute = _muted;
                _voices[index] = voice;
            }
        }

        private static void DestroyVoice(GameObject voice)
        {
            if (Application.isPlaying)
            {
                Destroy(voice);
            }
            else
            {
                DestroyImmediate(voice);
            }
        }
    }
}
