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
    /// Handlers read the message by reference and the steady state allocates nothing: registration and the first
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

        private SfxPlayer _player;
        private long _nextLoopId;

        /// <summary>The player this adapter drives, or <see langword="null"/> once it is gone.</summary>
        public SfxPlayer Player
        {
            get { return _player; }
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
            _nextLoopId = 1;
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

        private void HandlePlay(in SfxPlayRequest request)
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

        private void HandleLoop(in SfxLoopRequest request)
        {
            if (_player == null)
            {
                return;
            }

            SfxLibrary library = _player.Library;
            if (library == null || !library.TryGetCue(request.key, out SfxCue cue))
            {
                // Answered by the player's own drop report, which names NotPlayable. Saying it twice would make a
                // listener count one failure as two.
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

            long loopId = _nextLoopId;
            ++_nextLoopId;
            _loops.Add(loopId, handle);

            SfxLoopStartedMessage startedMessage = new(loopId, request.key);
            startedMessage.EmitGameObjectBroadcast(gameObject);
        }

        private void HandleStopLoop(in SfxLoopStopRequest request)
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

        private void HandleStopAll(in SfxStopAllRequest request)
        {
            _loops.Clear();
            if (_player == null)
            {
                return;
            }

            _player.StopAll();
        }

        private void HandleMute(in SfxMuteRequest request)
        {
            if (_player == null)
            {
                return;
            }

            _player.Muted = request.muted;
        }

        private void HandleCueDropped(SfxCue cue, SfxDropReason reason)
        {
            SfxCueDroppedMessage message = new(cue, reason);
            message.EmitGameObjectBroadcast(gameObject);
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
