namespace WallstopStudios.DxAudio.Integration
{
    using System;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using UnityEngine;

    /// <summary>Drives one <see cref="MusicPlayer"/> from messages, and reports what it plays.</summary>
    /// <remarks>
    /// A plain object, so the whole relay can be built and driven from an EditMode test with its own bus, while
    /// <see cref="MusicPlayerMessaging"/> supplies the Unity lifecycle in a game. The player stays a component with no
    /// package dependency: this class is the only place that knows DxMessaging exists.
    /// <para>
    /// A duck is a held state, so a request answers with the id that releases it. This class mints the id and holds
    /// the player's lease. The player releases a duck whose holder was destroyed, and the stale id is forgotten on
    /// the next request, so an id cannot release a duck that replaced it.
    /// </para>
    /// <para>
    /// Handlers take the message by value, because DxMessaging 3's reference handlers cannot be built from a method
    /// group. A message is a key and a reference, and the steady state allocates nothing: registration and the first
    /// insert of each held duck are the only costs.
    /// </para>
    /// </remarks>
    public sealed class MusicPlayerMessagingCore : IDisposable
    {
        /// <summary>
        /// Room for the ducks a game normally holds. The dictionary never grows past that in a normal scene, and a
        /// resize is a one-off cost on the duck path, so a warm play request stays allocation-free.
        /// </summary>
        private const int InitialDuckCapacity = 8;

        /*
            Shared by every relay in the process, so an id minted by one player is never a key in another's map. A
            per-instance counter restarts at 1 and two players would mint the same id, which makes a request aimed at
            the wrong player release that player's unrelated duck.
        */
        private static long _nextDuckId;

        /// <summary>The player this relay drives, or <see langword="null"/> once it is gone.</summary>
        public MusicPlayer Player
        {
            get { return _player; }
        }

        /// <summary>How many ducks this relay took and has not released.</summary>
        /// <remarks>
        /// A duck whose holder was destroyed is released by the player, and its id is forgotten on the next duck or
        /// unduck request.
        /// </remarks>
        public int HeldDuckCount
        {
            get { return _ducks.Count; }
        }

        private readonly Dictionary<long, MusicDuckLease> _ducks = new(InitialDuckCapacity);

        /* Keys are collected before any removal: a dictionary throws when it is changed while enumerated. */
        private readonly List<long> _endedDucks = new(InitialDuckCapacity);

        private readonly MusicPlayer _player;
        private readonly GameObject _source;
        private readonly IMessageBus _bus;
        private readonly IMessageBusProvider _busProvider;

        /// <summary>Builds a relay for <paramref name="player"/> that announces from <paramref name="source"/>.</summary>
        /// <param name="player">The player to drive.</param>
        /// <param name="source">The GameObject a sender addresses; normally the player's own.</param>
        /// <param name="bus">The bus to announce on, or <see langword="null"/> for the global one.</param>
        /// <param name="busProvider">Resolves the bus when <paramref name="bus"/> is null.</param>
        public MusicPlayerMessagingCore(MusicPlayer player, GameObject source, IMessageBus bus = null, IMessageBusProvider busProvider = null)
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

            _ = token.RegisterUntargeted<MusicPlayContextRequest>(HandlePlayContext);
            _ = token.RegisterUntargeted<MusicStopRequest>(HandleStop);
            _ = token.RegisterGameObjectTargeted<MusicDuckRequest>(_source, HandleDuck);
            _ = token.RegisterGameObjectTargeted<MusicUnduckRequest>(_source, HandleUnduck);
            _ = token.RegisterUntargeted<AudioPauseRequest>(HandlePause);
        }

        /// <summary>Starts relaying the player's own track facts.</summary>
        public void Enable()
        {
            if (_player == null)
            {
                return;
            }

            _player.TrackChanged -= HandleTrackChanged;
            _player.TrackChanged += HandleTrackChanged;
            _player.Stopped -= HandleStopped;
            _player.Stopped += HandleStopped;
        }

        /// <summary>Stops relaying, and releases every duck this relay took.</summary>
        public void Disable()
        {
            if (_player != null)
            {
                _player.TrackChanged -= HandleTrackChanged;
                _player.Stopped -= HandleStopped;
            }

            ReleaseEveryHeldDuck();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Disable();
        }

        /// <summary>Forgets the ids of ducks whose holder the player has already released.</summary>
        /// <remarks>
        /// A holder destroyed behind the relay's back is released by the player's own sweep. The id then releases
        /// nothing, so it is harmless, but it would keep <see cref="HeldDuckCount"/> above the truth.
        /// </remarks>
        public void PruneEndedDucks()
        {
            if (_ducks.Count == 0 || _player == null)
            {
                return;
            }

            foreach (KeyValuePair<long, MusicDuckLease> held in _ducks)
            {
                if (!_player.IsHeld(held.Value))
                {
                    _endedDucks.Add(held.Key);
                }
            }

            foreach (long ended in _endedDucks)
            {
                _ = _ducks.Remove(ended);
            }

            _endedDucks.Clear();
        }

        private void HandlePlayContext(MusicPlayContextRequest request)
        {
            if (_player == null)
            {
                return;
            }

            _ = _player.PlayContext(request.context);
        }

        private void HandleStop(MusicStopRequest request)
        {
            if (_player == null)
            {
                return;
            }

            _player.Stop();
        }

        private void HandleDuck(MusicDuckRequest request)
        {
            if (_player == null)
            {
                return;
            }

            PruneEndedDucks();

            MusicDuckLease lease = _player.Duck(request.duck, request.holder);
            if (!lease.IsValid)
            {
                return;
            }

            // A One duck asserted again is the same hold, so it keeps the id a caller already has.
            long duckId = FindDuckId(lease);
            if (duckId == 0)
            {
                duckId = ++_nextDuckId;
                _ducks.Add(duckId, lease);
            }

            Broadcast(new MusicDuckStartedMessage(duckId, request.duck));
        }

        private void HandleUnduck(MusicUnduckRequest request)
        {
            PruneEndedDucks();
            if (!_ducks.TryGetValue(request.duckId, out MusicDuckLease lease))
            {
                return;
            }

            // Removed before the release, so a handler that re-enters sees no duck it must release.
            _ = _ducks.Remove(request.duckId);
            if (_player == null)
            {
                return;
            }

            _ = _player.Release(lease);
        }

        private void HandlePause(AudioPauseRequest request)
        {
            if (_player == null)
            {
                return;
            }

            _player.Paused = request.paused;
        }

        private void HandleTrackChanged(MusicTrack track)
        {
            Broadcast(new MusicTrackChangedMessage(track));
        }

        private void HandleStopped()
        {
            Broadcast(new MusicStoppedMessage());
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

        private void ReleaseEveryHeldDuck()
        {
            if (_ducks.Count == 0)
            {
                return;
            }

            if (_player != null)
            {
                foreach (KeyValuePair<long, MusicDuckLease> held in _ducks)
                {
                    _ = _player.Release(held.Value);
                }
            }

            _ducks.Clear();
        }

        /// <summary>The id already issued for <paramref name="lease"/>, or zero when it has none.</summary>
        private long FindDuckId(in MusicDuckLease lease)
        {
            foreach (KeyValuePair<long, MusicDuckLease> held in _ducks)
            {
                if (held.Value.Equals(lease))
                {
                    return held.Key;
                }
            }

            return 0;
        }
    }
}
