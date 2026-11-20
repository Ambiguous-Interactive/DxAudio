namespace WallstopStudios.DxAudio.Integration
{
    using DxMessaging.Unity;
    using UnityEngine;

    /// <summary>Drives the <see cref="SfxPlayer"/> beside it from messages, for a project that has DxMessaging.</summary>
    /// <remarks>
    /// Put this beside a player and send <see cref="SfxPlayRequest"/>, <see cref="SfxLoopRequest"/>,
    /// <see cref="SfxLoopStopRequest"/> and <see cref="SfxStopAllRequest"/> to that GameObject; listen for
    /// <see cref="SfxLoopStartedMessage"/> and <see cref="SfxCueDroppedMessage"/>, which it broadcasts.
    /// It also answers the shared <see cref="AudioPauseRequest"/> for its own player.
    /// <see cref="Core"/> holds everything that decides, so a game can drive the same relay without this component,
    /// and an EditMode test can build it with its own bus.
    /// <para>
    /// A loop's lifetime is bookkeeping a caller would otherwise keep: the relay mints the id, holds the player's
    /// lease, and ends every loop it started when the component is disabled or destroyed. A stale id therefore stops
    /// nothing, rather than stopping whatever the pool has since started on that voice.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SfxPlayer))]
    public sealed class SfxPlayerMessaging : MessageAwareComponent
    {
        /// <summary>The relay this component enables and disables, or <see langword="null"/> before it is built.</summary>
        public SfxPlayerMessagingCore Core
        {
            get { return _core; }
        }

        /// <summary>The player this adapter drives, or <see langword="null"/> once it is gone.</summary>
        public SfxPlayer Player
        {
            get { return _core == null ? null : _core.Player; }
        }

        /// <summary>How many loops this adapter started and has not ended.</summary>
        public int HeldLoopCount
        {
            get { return _core == null ? 0 : _core.HeldLoopCount; }
        }

        /// <summary>
        /// No string-message demos. Nothing sends one, and DxMessaging 3 registers three handlers for them by default,
        /// which costs work on every enable and nothing else.
        /// </summary>
        protected override bool RegisterForStringMessages
        {
            get { return false; }
        }
        private SfxPlayerMessagingCore _core;

        protected override void RegisterMessageHandlers()
        {
            base.RegisterMessageHandlers();

            /*
                Built here, after the base resolved this component's bus, so a fact is announced on the bus the request
                arrived on.
            */
            _core = new SfxPlayerMessagingCore(GetComponent<SfxPlayer>(), gameObject, _configuredMessageBus, _configuredMessageBusProvider);
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

        protected override void OnDisable()
        {
            if (_core != null)
            {
                // Before the base goes quiet, so a stop keeps working while the token still runs.
                _core.Disable();
            }

            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (_core != null)
            {
                // A destroy that arrives without a disable must not leave a loop holding a voice.
                _core.Dispose();
                _core = null;
            }

            base.OnDestroy();
        }
    }
}
