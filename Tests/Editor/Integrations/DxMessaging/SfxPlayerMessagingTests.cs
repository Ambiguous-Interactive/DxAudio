namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using NUnit.Framework;
    using Unity.Profiling;
    using UnityEngine;
    using WallstopStudios.DxAudio.Integration;
    using Object = UnityEngine.Object;

    /// <summary>
    /// The optional DxMessaging relay: a message drives one <see cref="SfxPlayer"/>, and what it drops comes back as a
    /// fact. These tests run only in a project that has DxMessaging installed, which the guarded assemblies arrange on
    /// their own.
    /// </summary>
    /// <remarks>
    /// Every test builds its own <see cref="MessageBus"/> instead of using the static global one, so a registration
    /// cannot leak into the next test and nothing else in the editor hears these messages. The relay is a plain
    /// object for exactly this reason: an EditMode test never gets a component's enable pass.
    /// </remarks>
    public sealed class SfxPlayerMessagingTests
    {
        private const int PlayCount = 100_000;
        private const int WarmupPlays = 1_024;

        private readonly List<Object> _made = new();
        private readonly List<MessageRegistrationToken> _tokens = new();
        private readonly List<SfxPlayerMessagingCore> _relays = new();

        private MessageBus _bus;

        private static int PlayingVoices(SfxPlayer player)
        {
            int playing = 0;
            for (int voice = 0; voice < player.VoiceCount; ++voice)
            {
                if (player.TryGetVoiceState(voice, out SfxVoiceState state, out _) && state.isPlaying)
                {
                    ++playing;
                }
            }

            return playing;
        }

        private static AudioSource PlayingSource(SfxPlayer player)
        {
            AudioSource[] sources = player.GetComponentsInChildren<AudioSource>();
            foreach (AudioSource source in sources)
            {
                if (source.isPlaying)
                {
                    return source;
                }
            }

            return null;
        }

        [SetUp]
        public void SetUp()
        {
            _bus = new MessageBus();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (SfxPlayerMessagingCore relay in _relays)
            {
                relay.Dispose();
            }

            _relays.Clear();
            foreach (MessageRegistrationToken token in _tokens)
            {
                token.Dispose();
            }

            _tokens.Clear();
            foreach (Object made in _made)
            {
                if (made != null)
                {
                    Object.DestroyImmediate(made);
                }
            }

            _made.Clear();
        }

        [Test]
        public void APlayRequestPlaysTheCueTheLibraryLists()
        {
            SfxPlayer player = Player(Cue(loop: false, key: "hit"), voiceCount: 4);
            SfxPlayerMessagingCore relay = Relay(player);

            EmitPlay(relay, "hit");

            Assert.That(player.DropCounts.Total, Is.Zero);
            Assert.That(PlayingVoices(player), Is.EqualTo(1), "one request plays one voice");
        }

        [Test]
        public void APlayRequestWithoutAPositionPlaysAPositionalCueFlat()
        {
            SfxCue cue = Cue(loop: false, key: "hit", spatialBlend: 1f);
            SfxPlayer player = Player(cue, voiceCount: 4);
            SfxPlayerMessagingCore relay = Relay(player);

            EmitPlay(relay, "hit");

            Assert.That(player.DropCounts.Total, Is.Zero);
            Assert.That(PlayingVoices(player), Is.EqualTo(1), "a positional cue with no position is played flat");
            AudioSource playing = PlayingSource(player);
            Assert.That(playing.spatialBlend, Is.Zero, "a request that names no position is not placed at the origin");
        }

        [Test]
        public void APlayRequestForAnUnknownKeyBroadcastsItsDrop()
        {
            SfxPlayer player = Player(Cue(loop: false, key: "hit"), voiceCount: 4);
            SfxPlayerMessagingCore relay = Relay(player);
            List<SfxCueDroppedMessage> dropped = CaptureDrops(relay);

            EmitPlay(relay, "missing");

            Assert.That(dropped, Has.Count.EqualTo(1), "one failed request is one fact");
            Assert.That(dropped[0].reason, Is.EqualTo(SfxDropReason.NotPlayable));
            Assert.That(dropped[0].cue == null, "the library listed no cue, so there is none to name");
        }

        [Test]
        public void ALoopRequestAnswersWithAnIdThatStopsOnlyItsOwnLoop()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 4, loopingVoiceLimit: 2);
            SfxPlayerMessagingCore relay = Relay(player);
            List<SfxLoopStartedMessage> started = CaptureLoops(relay);

            EmitLoop(relay, "hum");
            EmitLoop(relay, "hum");

            Assert.That(started, Has.Count.EqualTo(2), "each request answers once");
            Assert.That(player.HeldVoiceCount, Is.EqualTo(2));
            Assert.That(started[0].loopId, Is.Not.EqualTo(started[1].loopId));
            Assert.That(started[0].key, Is.EqualTo("hum"));

            StopLoop(relay, started[0].loopId);
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1), "an id stops its own loop and no other");

            StopLoop(relay, started[0].loopId);
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1), "an id stops once");

            StopLoop(relay, started[1].loopId);
            Assert.That(player.HeldVoiceCount, Is.Zero);
            Assert.That(relay.HeldLoopCount, Is.Zero);
        }

        [Test]
        public void AnIdFromOnePlayerStopsNothingOnAnother()
        {
            SfxPlayer first = Player(Cue(loop: true, key: "hum"), voiceCount: 2, loopingVoiceLimit: 1);
            SfxPlayer second = Player(Cue(loop: true, key: "hum"), voiceCount: 2, loopingVoiceLimit: 1);
            SfxPlayerMessagingCore firstRelay = Relay(first);
            SfxPlayerMessagingCore secondRelay = Relay(second);
            List<SfxLoopStartedMessage> firstStarted = CaptureLoops(firstRelay);
            List<SfxLoopStartedMessage> secondStarted = CaptureLoops(secondRelay);

            EmitLoop(firstRelay, "hum");
            EmitLoop(secondRelay, "hum");

            StopLoop(secondRelay, firstStarted[0].loopId);

            Assert.That(firstStarted[0].loopId, Is.Not.EqualTo(secondStarted[0].loopId), "ids are unique across players");
            Assert.That(first.HeldVoiceCount, Is.EqualTo(1), "the other player's id names no loop here");
            Assert.That(second.HeldVoiceCount, Is.EqualTo(1), "nor does it end this player's own loop");
        }

        [Test]
        public void ALoopRequestForAnUnknownKeyBroadcastsItsDrop()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 2, loopingVoiceLimit: 1);
            SfxPlayerMessagingCore relay = Relay(player);
            List<SfxCueDroppedMessage> dropped = CaptureDrops(relay);
            List<SfxLoopStartedMessage> started = CaptureLoops(relay);

            EmitLoop(relay, "missing");

            Assert.That(started, Is.Empty, "a loop that did not start answers with nothing");
            Assert.That(dropped, Has.Count.EqualTo(1), "the key was resolved before the pool, so say so once");
            Assert.That(dropped[0].reason, Is.EqualTo(SfxDropReason.NotPlayable));
            Assert.That(relay.HeldLoopCount, Is.Zero);
        }

        [Test]
        public void LoopsThePlayerEndedElsewhereAreForgotten()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 4, loopingVoiceLimit: 2);
            SfxPlayerMessagingCore relay = Relay(player);

            EmitLoop(relay, "hum");
            EmitLoop(relay, "hum");
            Assert.That(relay.HeldLoopCount, Is.EqualTo(2));

            // A stop that never passed through the relay: the holds are gone, the ids are not.
            player.StopAll();
            Assert.That(relay.HeldLoopCount, Is.EqualTo(2), "nothing has asked the relay to reconcile yet");

            EmitLoop(relay, "hum");

            Assert.That(relay.HeldLoopCount, Is.EqualTo(1), "the ids of ended holds are forgotten");
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1));
        }

        [Test]
        public void AStopAllRequestEndsTheLoopsItStarted()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 4, loopingVoiceLimit: 2);
            SfxPlayerMessagingCore relay = Relay(player);

            EmitLoop(relay, "hum");
            EmitLoop(relay, "hum");
            Assert.That(player.HeldVoiceCount, Is.EqualTo(2));

            SfxStopAllRequest stop = default;
            stop.EmitGameObjectTargeted(relay.Player.gameObject, _bus);

            Assert.That(player.HeldVoiceCount, Is.Zero);
            Assert.That(relay.HeldLoopCount, Is.Zero, "the ids a stop consumed stop nothing afterwards");
        }

        [Test]
        public void AMuteRequestMutesWithoutStopping()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 2, loopingVoiceLimit: 1);
            SfxPlayerMessagingCore relay = Relay(player);
            EmitLoop(relay, "hum");

            SfxMuteRequest mute = new(true);
            mute.EmitUntargeted(_bus);

            Assert.That(player.Muted, Is.True);
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1), "a settings toggle is not a stop");

            SfxMuteRequest unmute = new(false);
            unmute.EmitUntargeted(_bus);
            Assert.That(player.Muted, Is.False);
        }

        [Test]
        public void DisablingTheRelayStopsTheLoopsItStarted()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 2, loopingVoiceLimit: 1);
            SfxPlayerMessagingCore relay = Relay(player);
            EmitLoop(relay, "hum");
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1));

            relay.Disable();

            Assert.That(player.HeldVoiceCount, Is.Zero, "a loop nobody can address must not hold a voice");
            Assert.That(relay.HeldLoopCount, Is.Zero);
        }

        [TestCase(false, TestName = "PlayRequestsThatStartAVoiceDoNotAllocate")]
        [TestCase(true, TestName = "PlayRequestsDroppedByAFullPoolDoNotAllocate")]
        public void WarmedPlayRequestsThroughTheBusDoNotAllocate(bool poolFull)
        {
            SfxPlayer player = Player(Cue(loop: false, key: "hit"), voiceCount: 1);
            SfxPlayerMessagingCore relay = Relay(player);
            EmitPlay(relay, "hit");
            AudioSource source = PlayingSource(player);
            Assert.That(source != null, Is.True, "The first request must start a voice.");
            // Keep the occupied voice playing for the full-pool case.
            source.loop = poolFull;
            int started = 0;
            void SendPlayRequest()
            {
                if (!poolFull)
                {
                    source.Stop();
                }

                EmitPlay(relay, "hit");
                if (!poolFull && source.isPlaying)
                {
                    ++started;
                }
            }

            for (int index = 0; index < WarmupPlays; ++index)
            {
                SendPlayRequest();
            }

            started = 0;
            int initialDrops = player.DropCounts.noVoice;
            using ProfilerRecorder recorder = new(
                ProfilerCategory.Internal,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread
            );
            Assert.That(recorder.Valid, Is.True, "Unity must expose the GC.Alloc marker.");
            recorder.Start();
            for (int index = 0; index < PlayCount; ++index)
            {
                SendPlayRequest();
            }

            recorder.Stop();
            long allocations = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
            source.Stop();
            int dropped = player.DropCounts.noVoice - initialDrops;
            Assert.That(poolFull ? dropped : started, Is.EqualTo(PlayCount), "Every request must reach the player.");
            Assert.That(allocations, Is.Zero, $"The relay path recorded {allocations} allocation events over {PlayCount:N0} requests.");
        }

        private void EmitPlay(SfxPlayerMessagingCore relay, string key)
        {
            SfxPlayRequest request = new(key);
            request.EmitGameObjectTargeted(relay.Player.gameObject, _bus);
        }

        private void EmitLoop(SfxPlayerMessagingCore relay, string key)
        {
            SfxLoopRequest request = new(key);
            request.EmitGameObjectTargeted(relay.Player.gameObject, _bus);
        }

        private void StopLoop(SfxPlayerMessagingCore relay, long loopId)
        {
            SfxLoopStopRequest request = new(loopId);
            request.EmitGameObjectTargeted(relay.Player.gameObject, _bus);
        }

        private SfxPlayerMessagingCore Relay(SfxPlayer player)
        {
            MessageHandler handler = new(new InstanceId(1), _bus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler, _bus);
            _tokens.Add(token);

            SfxPlayerMessagingCore relay = new(player, player.gameObject, _bus);
            _relays.Add(relay);
            relay.Register(token);
            relay.Enable();
            token.Enable();
            return relay;
        }

        private List<SfxLoopStartedMessage> CaptureLoops(SfxPlayerMessagingCore relay)
        {
            List<SfxLoopStartedMessage> heard = new();
            _ = Capture<SfxLoopStartedMessage>(
                relay.Player.gameObject,
                (SfxLoopStartedMessage message) =>
                {
                    heard.Add(message);
                }
            );
            return heard;
        }

        private List<SfxCueDroppedMessage> CaptureDrops(SfxPlayerMessagingCore relay)
        {
            List<SfxCueDroppedMessage> heard = new();
            _ = Capture<SfxCueDroppedMessage>(
                relay.Player.gameObject,
                (SfxCueDroppedMessage message) =>
                {
                    heard.Add(message);
                }
            );
            return heard;
        }

        /// <summary>Listens for broadcasts from <paramref name="source"/> without a component, on this test's bus.</summary>
        /// <remarks>
        /// <c>active</c> and <c>Enable</c> are both required: the flag arms the handler, and enabling commits the
        /// staged registration.
        /// </remarks>
        private MessageRegistrationHandle Capture<T>(GameObject source, Action<T> handler)
            where T : DxMessaging.Core.Messages.IBroadcastMessage
        {
            MessageHandler messageHandler = new(new InstanceId(2), _bus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(messageHandler, _bus);
            _tokens.Add(token);
            MessageRegistrationHandle registration = token.RegisterGameObjectBroadcast<T>(source, handler);
            token.Enable();
            return registration;
        }

        private SfxPlayer Player(SfxCue cue, int voiceCount, int loopingVoiceLimit = 4)
        {
            SfxLibrary library = Made(SfxLibrary.Create(new[] { cue }, voiceCount: voiceCount, loopingVoiceLimit: loopingVoiceLimit));
            SfxPlayer player = Made(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private SfxCue Cue(bool loop, string key = null, float minimumIntervalSeconds = 0f, float spatialBlend = 0f)
        {
            return Made(SfxCue.Create(new[] { new SfxVariant(Clip()) }, key: key, loop: loop, minimumIntervalSeconds: minimumIntervalSeconds, spatialBlend: spatialBlend));
        }

        private AudioClip Clip()
        {
            return Made(AudioClip.Create("test clip", 4_000, 1, 8_000, stream: false));
        }

        private T Made<T>(T made)
            where T : Object
        {
            _made.Add(made);
            return made;
        }
    }
}
