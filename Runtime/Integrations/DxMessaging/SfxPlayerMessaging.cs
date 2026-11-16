namespace WallstopStudios.DxAudio.Integration
{
    using System.Collections.Generic;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Unity;
    using UnityEngine;

    /// <summary>Drives one <see cref="SfxPlayer"/> from messages, and reports what it drops as messages.</summary>
    /// <remarks>
    /// The player stays a plain component with no package dependency: this adapter is the only place that knows
    /// DxMessaging exists, and a project without the package never compiles it. Put it beside a player and send
    /// <see cref="SfxPlayRequest"/>, <see cref="SfxLoopRequest"/>, <see cref="SfxLoopStopRequest"/> and
    /// <see cref="SfxStopAllRequest"/> to that GameObject; listen for <see cref="SfxLoopStartedMessage"/> and
    /// <see cref="SfxCueDroppedMessage"/>, which it broadcasts.
    /// <para>
    /// A loop's lifetime is bookkeeping a caller would otherwise keep: this adapter mints the id, holds the
    /// player's lease, and ends every loop it started when the component is disabled or destroyed. A stale id
    /// therefore stops nothing, rather than stopping whatever the pool has since started on that voice.
    /// </para>
    /// <para>
    /// Handlers take the message by value, because DxMessaging 3's reference handlers cannot be built from a method
    /// group. A message is a key and a few words, and the steady state allocates nothing: registration and the first
    /// insert of each held loop are the only costs.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SfxPlayer))]
    public sealed class SfxPlayerMessaging : MessageAwareComponent
    {
        /// <summary>
        /// Room for the loops a pool normally holds. The dictionary never grows past the loop limit, and a resize is
        /// a one-off cost on that path, so a warm play request stays allocation-free.
        /// </summary>
        private const int InitialLoopCapacity = 8;

        private readonly Dictionary<long, SfxLoopHandle> _loops = new(InitialLoopCapacity);

        /* Keys are collected before any removal: a dictionary throws when it is changed while enumerated. */
        private readonly List<long> _endedLoops = new(InitialLoopCapacity);

        /*
            Shared by every adapter in the process, so an id minted by one player is never a key in another's map.
            A per-instance counter restarts at 1 and two players would mint the same id, which makes a request aimed
            at the wrong player stop that player's unrelated loop.
        */
        private static long _nextLoopId;

        private SfxPlayer _player;

        /// <summary>The player this adapter drives, or <see langword="null"/> once it is gone.</summary>
        public SfxPlayer Player
        {
            get { return _player; }
        }

        /// <summary>
        /// No string-message demos. Nothing sends one, and DxMessaging 3 registers three handlers for them by default,
        /// which costs work on every enable and nothing else.
        /// </summary>
        protected override bool RegisterForStringMessages
        {
            get { return false; }
        }

        /// <summary>How many loops this adapter started and has not ended.</summary>
        public int HeldLoopCount
        {
            get { return _loops.Count; }
        }

        protected override void Awake()
        {
            // Resolved before the base stages registrations, so a handler never runs against a missing player.
            _player = GetComponent<SfxPlayer>();
            base.Awake();
        }

        protected override void RegisterMessageHandlers()
        {
            base.RegisterMessageHandlers();
            _ = _messageRegistrationToken.RegisterGameObjectTargeted<SfxPlayRequest>(gameObject, HandlePlay);
            _ = _messageRegistrationToken.RegisterGameObjectTargeted<SfxLoopRequest>(gameObject, HandleLoop);
            _ = _messageRegistrationToken.RegisterGameObjectTargeted<SfxLoopStopRequest>(gameObject, HandleStopLoop);
            _ = _messageRegistrationToken.RegisterGameObjectTargeted<SfxStopAllRequest>(gameObject, HandleStopAll);
            _ = _messageRegistrationToken.RegisterUntargeted<SfxMuteRequest>(HandleMute);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_player == null)
            {
                return;
            }

            _player.CueDropped += HandleCueDropped;
        }

        protected override void OnDisable()
        {
            if (_player != null)
            {
                _player.CueDropped -= HandleCueDropped;
            }

            // Before the token goes quiet, so a stop keeps working while it runs. A loop nobody can address any
            // more must not hold a voice for the rest of the session.
            StopEveryHeldLoop();
            base.OnDisable();
        }

        private void HandlePlay(SfxPlayRequest request)
        {
            if (_player == null)
            {
                return;
            }

            if (request.isPositional)
            {
                _ = _player.Play(request.key, request.position);
                return;
            }

            _ = _player.Play(request.key);
        }

        private void HandleLoop(SfxLoopRequest request)
        {
            if (_player == null)
            {
                return;
            }

            PruneEndedLoops();

            SfxLibrary library = _player.Library;
            if (library == null || !library.TryGetCue(request.key, out SfxCue cue))
            {
                // The player's own drop report never sees this one: the key was resolved here, before the pool. Report
                // the same fact it would, once, rather than leaving a request that started nothing unexplained.
                Broadcast(new SfxCueDroppedMessage(null, SfxDropReason.NotPlayable));
                return;
            }

            SfxLoopHandle handle;
            bool started = request.isPositional
                ? _player.TryPlayLooping(cue, request.position, out handle)
                : _player.TryPlayLooping(cue, out handle);
            if (!started)
            {
                return;
            }

            long loopId = ++_nextLoopId;
            _loops.Add(loopId, handle);

            Broadcast(new SfxLoopStartedMessage(loopId, request.key));
        }

        private void HandleStopLoop(SfxLoopStopRequest request)
        {
            if (!_loops.TryGetValue(request.loopId, out SfxLoopHandle handle))
            {
                return;
            }

            // Removed before the stop, so a handler that re-enters through the drop report sees no loop it must end.
            _ = _loops.Remove(request.loopId);
            if (_player == null)
            {
                return;
            }

            _ = _player.StopLoop(handle);
        }

        private void HandleStopAll(SfxStopAllRequest request)
        {
            _loops.Clear();
            if (_player == null)
            {
                return;
            }

            _player.StopAll();
        }

        private void HandleMute(SfxMuteRequest request)
        {
            if (_player == null)
            {
                return;
            }

            _player.Muted = request.muted;
        }

        private void HandleCueDropped(SfxCue cue, SfxDropReason reason)
        {
            Broadcast(new SfxCueDroppedMessage(cue, reason));
        }

        /// <summary>
        /// Announces a fact from this player on the bus its registrations use.
        /// </summary>
        /// <remarks>
        /// The bus is named explicitly rather than left to the global default: a component configured with its own bus
        /// would otherwise answer a request it heard on that bus with a fact nobody there can hear.
        /// </remarks>
        private void Broadcast<T>(T message)
            where T : DxMessaging.Core.Messages.IBroadcastMessage
        {
            message.EmitGameObjectBroadcast(gameObject, _configuredMessageBus, _configuredMessageBusProvider);
        }

        /// <summary>
        /// Forgets the ids of loops whose lease this player no longer holds.
        /// </summary>
        /// <remarks>
        /// A voice is given up behind the adapter's back when its clip is destroyed or unloaded. A stale id stops
        /// nothing, so it is harmless, but it would keep <see cref="HeldLoopCount"/> above the truth for the rest of
        /// the session. The lease is the player's own answer to "is this hold still mine".
        /// </remarks>
        private void PruneEndedLoops()
        {
            if (_loops.Count == 0 || _player == null)
            {
                return;
            }

            foreach (KeyValuePair<long, SfxLoopHandle> held in _loops)
            {
                bool live =
                    _player.TryGetVoiceState(held.Value.voice, out _, out int lease) && lease == held.Value.lease;
                if (!live)
                {
                    _endedLoops.Add(held.Key);
                }
            }

            for (int index = 0; index < _endedLoops.Count; ++index)
            {
                _ = _loops.Remove(_endedLoops[index]);
            }

            _endedLoops.Clear();
        }

        private void StopEveryHeldLoop()
        {
            if (0 < _loops.Count)
            {
                if (_player != null)
                {
                    foreach (KeyValuePair<long, SfxLoopHandle> held in _loops)
                    {
                        _ = _player.StopLoop(held.Value);
                    }
                }

                _loops.Clear();
            }
        }
    }
}
