namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Unity;
    using UnityEngine;

    /// <summary>Drives the <see cref="MusicPlayer"/> beside it from messages, for a project that has DxMessaging.</summary>
    /// <remarks>
    /// Put this beside a music player. Send it <see cref="MusicPlayContextRequest"/> and
    /// <see cref="MusicStopRequest"/>; listen for <see cref="MusicTrackChangedMessage"/> and
    /// <see cref="MusicStoppedMessage"/>, which it broadcasts. Ducking is the stateful part: send a targeted
    /// <see cref="MusicDuckRequest"/>, keep the id from <see cref="MusicDuckStartedMessage"/>, and release it with
    /// <see cref="MusicUnduckRequest"/>. Destroying the request's holder releases the duck on the next frame.
    /// <see cref="AudioPauseRequest"/> pauses or resumes this player and every sound-effect player.
    /// <see cref="Core"/> holds everything that decides, so a game can drive the same relay without this component,
    /// and an EditMode test can build it with its own bus.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MusicPlayer))]
    public sealed class MusicPlayerMessaging : MessageAwareComponent
    {
        private MusicPlayerMessagingCore _core;

        /// <summary>The relay this component enables and disables, or <see langword="null"/> before it is built.</summary>
        public MusicPlayerMessagingCore Core
        {
            get { return _core; }
        }

        /// <summary>The player this adapter drives, or <see langword="null"/> once it is gone.</summary>
        public MusicPlayer Player
        {
            get { return _core == null ? null : _core.Player; }
        }

        /// <summary>How many ducks this adapter took and has not released.</summary>
        public int HeldDuckCount
        {
            get { return _core == null ? 0 : _core.HeldDuckCount; }
        }

        /// <summary>
        /// No string-message demos. Nothing sends one, and DxMessaging 3 registers three handlers for them by default,
        /// which costs work on every enable and nothing else.
        /// </summary>
        protected override bool RegisterForStringMessages
        {
            get { return false; }
        }

        protected override void RegisterMessageHandlers()
        {
            base.RegisterMessageHandlers();

            // Built here, after the base resolved this component's bus, so a fact is announced on the bus the request
            // arrived on.
            _core = new MusicPlayerMessagingCore(
                GetComponent<MusicPlayer>(),
                gameObject,
                _configuredMessageBus,
                _configuredMessageBusProvider
            );
            _core.Register(_messageRegistrationToken);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_core != null)
            {
                _core.Enable();
            }
        }

        private void Update()
        {
            if (_core != null)
            {
                // A duck whose holder was destroyed is released by the player; this forgets its id.
                _core.PruneEndedDucks();
            }
        }

        protected override void OnDisable()
        {
            if (_core != null)
            {
                // Before the base goes quiet, so a release keeps working while the token still runs.
                _core.Disable();
            }

            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (_core != null)
            {
                // A destroy that arrives without a disable must not leave a duck holding the music down.
                _core.Dispose();
                _core = null;
            }

            base.OnDestroy();
        }
    }
}
