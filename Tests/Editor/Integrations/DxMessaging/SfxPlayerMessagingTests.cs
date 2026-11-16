namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DxAudio.Integration;
    using Object = UnityEngine.Object;

    /// <summary>
    /// The optional DxMessaging adapter: a message drives one <see cref="SfxPlayer"/>, and what it drops comes back
    /// as a fact. These tests run only in a project that has DxMessaging installed, which the guarded assemblies
    /// arrange on their own.
    /// </summary>
    public sealed class SfxPlayerMessagingTests
    {
        private const int PlayCount = 100_000;
        private const int WarmupPlays = 1_024;

        /// <summary>Bytes the heap may grow over <see cref="PlayCount"/> requests on a path that allocates nothing.</summary>
        private const long Ceiling = 524_288;

        /// <summary>Measured windows per probe. The smallest is taken, because heap segments commit in coarse steps.</summary>
        private const int Windows = 3;

        private readonly List<Object> _made = new();
        private readonly List<MessageRegistrationToken> _tokens = new();

        [TearDown]
        public void TearDown()
        {
            // The bus is static and shared, so a token that outlives its test hears the next one's messages.
            for (int index = 0; index < _tokens.Count; ++index)
            {
                _tokens[index].Dispose();
            }

            _tokens.Clear();
            for (int index = 0; index < _made.Count; ++index)
            {
                if (_made[index] != null)
                {
                    Object.DestroyImmediate(_made[index]);
                }
            }

            _made.Clear();
        }

        [Test]
        public void APlayRequestPlaysTheCueTheLibraryLists()
        {
            SfxPlayer player = Player(Cue(loop: false, key: "hit"), voiceCount: 4);
            SfxPlayerMessaging messaging = Messaging(player);

            EmitPlay(messaging.gameObject, "hit");

            Assert.That(player.DropCounts.Total, Is.Zero);
            Assert.That(PlayingVoices(player), Is.EqualTo(1), "one request plays one voice");
        }

        [Test]
        public void APlayRequestWithoutAPositionPlaysAPositionalCueFlat()
        {
            SfxCue cue = Cue(loop: false, key: "hit", spatialBlend: 1f);
            SfxPlayer player = Player(cue, voiceCount: 4);
            SfxPlayerMessaging messaging = Messaging(player);

            EmitPlay(messaging.gameObject, "hit");

            Assert.That(player.DropCounts.Total, Is.Zero);
            Assert.That(PlayingVoices(player), Is.EqualTo(1), "a positional cue with no position is played flat");
            AudioSource playing = PlayingSource(player);
            Assert.That(playing.spatialBlend, Is.Zero, "a request that names no position is not placed at the origin");
        }

        [Test]
        public void APlayRequestForAnUnknownKeyBroadcastsItsDrop()
        {
            SfxPlayer player = Player(Cue(loop: false, key: "hit"), voiceCount: 4);
            SfxPlayerMessaging messaging = Messaging(player);
            List<SfxCueDroppedMessage> dropped = CaptureDrops(messaging.gameObject);

            EmitPlay(messaging.gameObject, "missing");

            Assert.That(dropped, Has.Count.EqualTo(1), "one failed request is one fact");
            Assert.That(dropped[0].reason, Is.EqualTo(SfxDropReason.NotPlayable));
            Assert.That(dropped[0].cue == null, "the library listed no cue, so there is none to name");
        }

        [Test]
        public void ALoopRequestAnswersWithAnIdThatStopsOnlyItsOwnLoop()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 4, loopingVoiceLimit: 2);
            SfxPlayerMessaging messaging = Messaging(player);
            List<SfxLoopStartedMessage> started = CaptureLoops(messaging.gameObject);

            EmitLoop(messaging.gameObject, "hum");
            EmitLoop(messaging.gameObject, "hum");

            Assert.That(started, Has.Count.EqualTo(2), "each request answers once");
            Assert.That(player.HeldVoiceCount, Is.EqualTo(2));
            Assert.That(started[0].loopId, Is.Not.EqualTo(started[1].loopId));
            Assert.That(started[0].key, Is.EqualTo("hum"));

            StopLoop(messaging.gameObject, started[0].loopId);
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1), "an id stops its own loop and no other");

            StopLoop(messaging.gameObject, started[0].loopId);
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1), "an id stops once");

            StopLoop(messaging.gameObject, started[1].loopId);
            Assert.That(player.HeldVoiceCount, Is.Zero);
            Assert.That(messaging.HeldLoopCount, Is.Zero);
        }

        [Test]
        public void AnIdFromOnePlayerStopsNothingOnAnother()
        {
            SfxPlayer first = Player(Cue(loop: true, key: "hum"), voiceCount: 2, loopingVoiceLimit: 1);
            SfxPlayer second = Player(Cue(loop: true, key: "hum"), voiceCount: 2, loopingVoiceLimit: 1);
            SfxPlayerMessaging firstMessaging = Messaging(first);
            SfxPlayerMessaging secondMessaging = Messaging(second);
            List<SfxLoopStartedMessage> firstStarted = CaptureLoops(firstMessaging.gameObject);

            EmitLoop(firstMessaging.gameObject, "hum");
            StopLoop(secondMessaging.gameObject, firstStarted[0].loopId);

            Assert.That(first.HeldVoiceCount, Is.EqualTo(1), "a request addressed elsewhere leaves this loop alone");
            Assert.That(second.HeldVoiceCount, Is.Zero);
        }

        [Test]
        public void AStopAllRequestEndsTheLoopsItStarted()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 4, loopingVoiceLimit: 2);
            SfxPlayerMessaging messaging = Messaging(player);

            EmitLoop(messaging.gameObject, "hum");
            EmitLoop(messaging.gameObject, "hum");
            Assert.That(player.HeldVoiceCount, Is.EqualTo(2));

            SfxStopAllRequest stop = default;
            stop.EmitGameObjectTargeted(messaging.gameObject);

            Assert.That(player.HeldVoiceCount, Is.Zero);
            Assert.That(messaging.HeldLoopCount, Is.Zero, "the ids a stop consumed stop nothing afterwards");
        }

        [Test]
        public void AMuteRequestMutesWithoutStopping()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 2, loopingVoiceLimit: 1);
            SfxPlayerMessaging messaging = Messaging(player);
            EmitLoop(messaging.gameObject, "hum");

            SfxMuteRequest mute = new(true);
            mute.EmitUntargeted();

            Assert.That(player.Muted, Is.True);
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1), "a settings toggle is not a stop");

            SfxMuteRequest unmute = new(false);
            unmute.EmitUntargeted();
            Assert.That(player.Muted, Is.False);
        }

        [Test]
        public void DisablingTheAdapterStopsTheLoopsItStarted()
        {
            SfxPlayer player = Player(Cue(loop: true, key: "hum"), voiceCount: 2, loopingVoiceLimit: 1);
            SfxPlayerMessaging messaging = Messaging(player);
            EmitLoop(messaging.gameObject, "hum");
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1));

            messaging.enabled = false;

            Assert.That(player.HeldVoiceCount, Is.Zero, "a loop nobody can address must not hold a voice");
            Assert.That(messaging.HeldLoopCount, Is.Zero);
        }

        [Test]
        public void TheAdapterStopsListeningWhenDisabled()
        {
            SfxPlayer player = Player(Cue(loop: false, key: "hit"), voiceCount: 4);
            SfxPlayerMessaging messaging = Messaging(player);

            messaging.enabled = false;
            EmitPlay(messaging.gameObject, "hit");
            Assert.That(PlayingVoices(player), Is.Zero);

            messaging.enabled = true;
            EmitPlay(messaging.gameObject, "hit");
            Assert.That(PlayingVoices(player), Is.EqualTo(1), "enabling it again resumes listening");
        }

        [Test]
        public void APlayRequestThroughTheBusDoesNotGrowTheHeap()
        {
            SfxPlayer player = Player(Cue(loop: false, key: "hit"), voiceCount: 4);
            SfxPlayerMessaging messaging = Messaging(player);
            for (int index = 0; index < WarmupPlays; ++index)
            {
                EmitPlay(messaging.gameObject, "hit");
            }

            long growth = HeapGrowth(() => EmitPlay(messaging.gameObject, "hit"), PlayCount);

            Assert.That(
                growth,
                Is.LessThan(Ceiling),
                $"a request through the bus grew the heap by {growth} bytes over {PlayCount:N0} plays."
            );
        }

        private static void EmitPlay(GameObject target, string key)
        {
            SfxPlayRequest request = new(key);
            request.EmitGameObjectTargeted(target);
        }

        private static void EmitLoop(GameObject target, string key)
        {
            SfxLoopRequest request = new(key);
            request.EmitGameObjectTargeted(target);
        }

        private static void StopLoop(GameObject target, long loopId)
        {
            SfxLoopStopRequest request = new(loopId);
            request.EmitGameObjectTargeted(target);
        }

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
            for (int index = 0; index < sources.Length; ++index)
            {
                if (sources[index].isPlaying)
                {
                    return sources[index];
                }
            }

            return null;
        }

        /// <summary>The smallest heap growth of <see cref="Windows"/> runs of <paramref name="count"/> calls.</summary>
        /// <remarks>
        /// The same probe as <c>AllocationTests</c>: a collection at the end would reclaim the churn this looks for,
        /// so the raw heap is read, and the smallest window wins because Mono commits segments in coarse steps.
        /// </remarks>
        private static long HeapGrowth(Action emit, int count)
        {
            emit();
            long smallest = long.MaxValue;
            for (int window = 0; window < Windows; ++window)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                long before = GC.GetTotalMemory(forceFullCollection: false);
                for (int index = 0; index < count; ++index)
                {
                    emit();
                }

                long growth = GC.GetTotalMemory(forceFullCollection: false) - before;
                if (growth < smallest)
                {
                    smallest = growth;
                }
            }

            return smallest;
        }

        private List<SfxLoopStartedMessage> CaptureLoops(GameObject source)
        {
            List<SfxLoopStartedMessage> heard = new();
            _ = Capture<SfxLoopStartedMessage>(
                source,
                (in SfxLoopStartedMessage message) =>
                {
                    heard.Add(message);
                }
            );
            return heard;
        }

        private List<SfxCueDroppedMessage> CaptureDrops(GameObject source)
        {
            List<SfxCueDroppedMessage> heard = new();
            _ = Capture<SfxCueDroppedMessage>(
                source,
                (in SfxCueDroppedMessage message) =>
                {
                    heard.Add(message);
                }
            );
            return heard;
        }

        /// <summary>
        /// Listens for broadcasts from <paramref name="source"/> without a component, so the fixture stays EditMode.
        /// </summary>
        /// <remarks>
        /// <c>active</c> and <c>Enable</c> are both required: the flag arms the handler, and enabling commits the
        /// staged registration. The token is disposed in teardown because the bus that holds it is static.
        /// </remarks>
        private MessageRegistrationHandle Capture<T>(GameObject source, MessageHandler.FastHandler<T> handler)
            where T : DxMessaging.Core.Messages.IBroadcastMessage
        {
            MessageHandler messageHandler = new(source) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(messageHandler);
            _tokens.Add(token);
            MessageRegistrationHandle registration = token.RegisterGameObjectBroadcast<T>(source, handler);
            token.Enable();
            return registration;
        }

        private SfxPlayer Player(SfxCue cue, int voiceCount, int loopingVoiceLimit = 4)
        {
            SfxLibrary library = Made(
                SfxLibrary.Create(new[] { cue }, voiceCount: voiceCount, loopingVoiceLimit: loopingVoiceLimit)
            );
            SfxPlayer player = Made(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private SfxPlayerMessaging Messaging(SfxPlayer player)
        {
            return Made(player.gameObject.AddComponent<SfxPlayerMessaging>());
        }

        private SfxCue Cue(bool loop, string key = null, float minimumIntervalSeconds = 0f, float spatialBlend = 0f)
        {
            return Made(
                SfxCue.Create(
                    new[] { new SfxVariant(Clip()) },
                    key: key,
                    loop: loop,
                    minimumIntervalSeconds: minimumIntervalSeconds,
                    spatialBlend: spatialBlend
                )
            );
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
