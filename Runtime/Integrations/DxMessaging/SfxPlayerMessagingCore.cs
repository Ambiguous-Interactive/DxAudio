namespace WallstopStudios.DxAudio.Integration
{
    using System;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using UnityEngine;

    /// <summary>Drives one <see cref="SfxPlayer"/> from messages, and reports what it drops as messages.</summary>
    /// <remarks>
    /// A plain object, so the whole relay can be built and driven from an EditMode test with its own bus, while
    /// <see cref="SfxPlayerMessaging"/> supplies the Unity lifecycle in a game. The player stays a component with no
    /// package dependency: this class is the only place that knows DxMessaging exists.
    /// <para>
    /// A loop is a held state, so a request answers with the id that ends it. This class mints the id, holds the
    /// player's lease, and ends every loop it started when it is disabled or disposed. A stale id therefore stops
    /// nothing, rather than stopping whatever the pool has since started on that voice.
    /// </para>
    /// <para>
    /// Handlers take the message by value, because DxMessaging 3's reference handlers cannot be built from a method
    /// group. A message is a key and a few words, and the steady state allocates nothing: registration and the first
    /// insert of each held loop are the only costs.
    /// </para>
    /// </remarks>
    public sealed class SfxPlayerMessagingCore : IDisposable
    {
        /// <summary>
        /// Room for the loops a pool normally holds. The dictionary never grows past the loop limit, and a resize is a
        /// one-off cost on that path, so a warm play request stays allocation-free.
        /// </summary>
        private const int InitialLoopCapacity = 8;

        /*
            Shared by every relay in the process, so an id minted by one player is never a key in another's map. A
            per-instance counter restarts at 1 and two players would mint the same id, which makes a request aimed at
            the wrong player stop that player's unrelated loop.
        */
        private static long _nextLoopId;

        /// <summary>The player this relay drives, or <see langword="null"/> once it is gone.</summary>
        public SfxPlayer Player
        {
            get { return _player; }
        }

        /// <summary>How many loops this relay started and has not ended.</summary>
        public int HeldLoopCount
        {
            get { return _loops.Count; }
        }

        private readonly Dictionary<long, SfxLoopHandle> _loops = new(InitialLoopCapacity);

        /* Keys are collected before any removal: a dictionary throws when it is changed while enumerated. */
        private readonly List<long> _endedLoops = new(InitialLoopCapacity);

        private readonly SfxPlayer _player;
        private readonly GameObject _source;
        private readonly IMessageBus _bus;
        private readonly IMessageBusProvider _busProvider;

        /// <summary>Builds a relay for <paramref name="player"/> that announces from <paramref name="source"/>.</summary>
        /// <param name="player">The player to drive.</param>
        /// <param name="source">The GameObject a sender addresses; normally the player's own.</param>
        /// <param name="bus">The bus to announce on, or <see langword="null"/> for the global one.</param>
        /// <param name="busProvider">Resolves the bus when <paramref name="bus"/> is null.</param>
        public SfxPlayerMessagingCore(SfxPlayer player, GameObject source, IMessageBus bus = null, IMessageBusProvider busProvider = null)
        {
            _player = player;
            _source = source;
            _bus = bus;
            _busProvider = busProvider;
        }

        /// <summary>Stages the handlers that drive the player.</summary>
        /// <param name="token">The token whose enable and disable own their lifetime.</param>
        public void Register(MessageRegistrationToken token)
        {
            if (token == null)
            {
                return;
            }

            _ = token.RegisterGameObjectTargeted<SfxPlayRequest>(_source, HandlePlay);
            _ = token.RegisterGameObjectTargeted<SfxLoopRequest>(_source, HandleLoop);
            _ = token.RegisterGameObjectTargeted<SfxLoopStopRequest>(_source, HandleStopLoop);
            _ = token.RegisterGameObjectTargeted<SfxStopAllRequest>(_source, HandleStopAll);
            _ = token.RegisterUntargeted<SfxMuteRequest>(HandleMute);
            _ = token.RegisterUntargeted<AudioPauseRequest>(HandlePause);
        }

        /// <summary>Starts relaying the player's own drop reports.</summary>
        public void Enable()
        {
            if (_player == null)
            {
                return;
            }

            _player.CueDropped -= HandleCueDropped;
            _player.CueDropped += HandleCueDropped;
        }

        /// <summary>Stops relaying, and ends every loop this relay started.</summary>
        public void Disable()
        {
            if (_player != null)
            {
                _player.CueDropped -= HandleCueDropped;
            }

            StopEveryHeldLoop();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Disable();
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
                /*
                    The player's own drop report never sees this one: the key was resolved here, before the pool. Report
                    the same fact it would, once, rather than leaving a request that started nothing unexplained.
                */
                Broadcast(new SfxCueDroppedMessage(null, SfxDropReason.NotPlayable));
                return;
            }

            SfxLoopHandle handle;
            bool started = request.isPositional ? _player.TryPlayLooping(cue, request.position, out handle) : _player.TryPlayLooping(cue, out handle);
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

        private void HandlePause(AudioPauseRequest request)
        {
            if (_player == null)
            {
                return;
            }

            _player.Paused = request.paused;
        }

        private void HandleCueDropped(SfxCue cue, SfxDropReason reason)
        {
            Broadcast(new SfxCueDroppedMessage(cue, reason));
        }

        /// <summary>Announces a fact from this player on the bus its registrations use.</summary>
        /// <remarks>
        /// The bus is named explicitly rather than left to the global default: a relay pointed at one bus would otherwise
        /// answer a request it heard there with a fact nobody on that bus can hear.
        /// </remarks>
        private void Broadcast<T>(T message)
            where T : struct, DxMessaging.Core.Messages.IBroadcastMessage
        {
            message.EmitGameObjectBroadcast(_source, _bus, _busProvider);
        }

        /// <summary>Forgets the ids of loops whose lease this player no longer holds.</summary>
        /// <remarks>
        /// A voice is given up behind the relay's back when its clip is destroyed or unloaded. A stale id stops
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
                bool live = _player.TryGetVoiceState(held.Value.voice, out _, out int lease) && lease == held.Value.lease;
                if (!live)
                {
                    _endedLoops.Add(held.Key);
                }
            }

            foreach (long ended in _endedLoops)
            {
                _ = _loops.Remove(ended);
            }

            _endedLoops.Clear();
        }

        private void StopEveryHeldLoop()
        {
            if (_loops.Count == 0)
            {
                return;
            }

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
