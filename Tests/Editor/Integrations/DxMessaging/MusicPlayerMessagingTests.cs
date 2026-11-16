namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.MessageBus;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DxAudio.Integration;
    using Object = UnityEngine.Object;

    /// <summary>
    /// The music and pause half of the optional DxMessaging relay: a request drives a <see cref="MusicPlayer"/>, and
    /// every fact it announces comes back as a message. These tests run only where DxMessaging is installed.
    /// </summary>
    /// <remarks>
    /// Every test builds its own <see cref="MessageBus"/>, as <c>SfxPlayerMessagingTests</c> does, so no registration
    /// leaks and nothing else in the editor hears these messages. The relays are plain objects because an EditMode
    /// test never gets a component's enable pass.
    /// </remarks>
    public sealed class MusicPlayerMessagingTests
    {
        private readonly List<Object> _made = new();
        private readonly List<MessageRegistrationToken> _tokens = new();
        private readonly List<IDisposable> _relays = new();

        private MessageBus _bus;

        [SetUp]
        public void SetUp()
        {
            _bus = new MessageBus();
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < _relays.Count; ++index)
            {
                _relays[index].Dispose();
            }

            _relays.Clear();
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
        public void AContextRequestPlaysTheTrackAndAnnouncesIt()
        {
            MusicTrack town = Made(MusicTrack.Create(Clip(), context: "town"));
            MusicPlayer player = Player(town);
            MusicPlayerMessagingCore relay = Relay(player);
            List<MusicTrackChangedMessage> heard = CaptureTrackChanges(relay);

            MusicPlayContextRequest unknown = new("cave");
            unknown.EmitUntargeted(_bus);
            Assert.That(player.Current == null, "a context the library does not list plays nothing");
            Assert.That(heard, Is.Empty, "a request that changed nothing answers with nothing");

            MusicPlayContextRequest request = new("town");
            request.EmitUntargeted(_bus);

            Assert.That(player.Current, Is.SameAs(town));
            Assert.That(heard, Has.Count.EqualTo(1), "one change is one fact");
            Assert.That(heard[0].track, Is.SameAs(town), "the fact carries the track that now plays");
        }

        [Test]
        public void AStopRequestAnnouncesTheStop()
        {
            MusicTrack town = Made(MusicTrack.Create(Clip(), context: "town"));
            MusicPlayer player = Player(town);
            MusicPlayerMessagingCore relay = Relay(player);
            List<MusicStoppedMessage> stopped = CaptureStops(relay);
            MusicPlayContextRequest play = new("town");
            play.EmitUntargeted(_bus);

            MusicStopRequest stop = default;
            stop.EmitUntargeted(_bus);

            Assert.That(player.Current == null, "the stop clears the selection");
            Assert.That(stopped, Has.Count.EqualTo(1));
        }

        [Test]
        public void ADuckRequestReturnsToTheAttenuationItFound()
        {
            MusicPlayer player = Player();
            MusicPlayerMessagingCore relay = Relay(player);
            MusicDuck menu = Made(MusicDuck.Create(MusicDuckHolders.One, 0.5f, applySeconds: 0f, releaseSeconds: 0f));
            MusicDuck dialogue = Made(
                MusicDuck.Create(MusicDuckHolders.Many, 0.25f, applySeconds: 0f, releaseSeconds: 0f)
            );
            MusicDuckLease menuLease = player.Duck(menu);
            player.Tick(1f);
            Assert.That(player.Attenuation, Is.EqualTo(menu.Attenuation));

            List<MusicDuckStartedMessage> started = CaptureDucks(relay);
            MusicDuckRequest request = new(dialogue);
            request.EmitGameObjectTargeted(player.gameObject, _bus);

            Assert.That(started, Has.Count.EqualTo(1), "a duck answers once");
            Assert.That(started[0].duck, Is.SameAs(dialogue));
            Assert.That(started[0].duckId, Is.Not.Zero);
            player.Tick(1f);
            Assert.That(player.Attenuation, Is.EqualTo(dialogue.Attenuation), "the requested duck is in force");

            Unduck(relay, started[0].duckId);
            player.Tick(1f);
            Assert.That(
                player.Attenuation,
                Is.EqualTo(menu.Attenuation),
                "releasing the message duck returns the music to the hold it found"
            );
            Assert.That(relay.HeldDuckCount, Is.Zero);

            Unduck(relay, started[0].duckId);
            Assert.That(player.Attenuation, Is.EqualTo(menu.Attenuation), "an id releases once");

            Assert.That(player.Release(menuLease), Is.True);
            player.Tick(1f);
            Assert.That(player.Attenuation, Is.EqualTo(MusicAttenuation.Unattenuated));
        }

        [Test]
        public void ADestroyedDuckHolderReleasesTheDuck()
        {
            MusicPlayer player = Player();
            MusicPlayerMessagingCore relay = Relay(player);
            MusicDuck dialogue = Made(
                MusicDuck.Create(MusicDuckHolders.Many, 0.25f, applySeconds: 0f, releaseSeconds: 0f)
            );
            GameObject speaker = Made(new GameObject("speaker"));
            List<MusicDuckStartedMessage> started = CaptureDucks(relay);

            MusicDuckRequest request = new(dialogue, speaker);
            request.EmitGameObjectTargeted(player.gameObject, _bus);
            player.Tick(1f);
            Assert.That(started, Has.Count.EqualTo(1));
            Assert.That(player.Attenuation, Is.EqualTo(dialogue.Attenuation));

            Object.DestroyImmediate(speaker);
            player.Tick(1f);

            Assert.That(
                player.TargetAttenuation,
                Is.EqualTo(MusicAttenuation.Unattenuated),
                "the player releases a duck whose holder was destroyed"
            );
            relay.PruneEndedDucks();
            Assert.That(relay.HeldDuckCount, Is.Zero, "the released id is forgotten");

            Unduck(relay, started[0].duckId);
            player.Tick(1f);
            Assert.That(
                player.Attenuation,
                Is.EqualTo(MusicAttenuation.Unattenuated),
                "a released id releases nothing"
            );
        }

        [Test]
        public void APauseRequestHoldsBothPlayersAtTheirPlayhead()
        {
            MusicTrack town = Made(MusicTrack.Create(Clip(), context: "town"));
            MusicPlayer music = Player(town);
            MusicPlayerMessagingCore musicRelay = Relay(music);
            SfxCue hum = Cue(loop: true);
            SfxPlayer sfx = Player(SfxLibrary.Create(new[] { hum }, voiceCount: 2, loopingVoiceLimit: 1));
            _ = SfxRelay(sfx);

            MusicPlayContextRequest play = new("town");
            play.EmitUntargeted(_bus);
            Assert.That(sfx.TryPlayLooping(hum, out _), Is.True);
            sfx.Muted = true;

            AudioSource musicSource = PlayingSource(music);
            AudioSource sfxSource = PlayingSource(sfx);
            Assert.That(musicSource != null && sfxSource != null, "both players are sounding before the pause");
            musicSource.timeSamples = 2_000;
            sfxSource.timeSamples = 1_000;

            AudioPauseRequest pause = new(true);
            pause.EmitUntargeted(_bus);

            Assert.That(music.Paused, Is.True);
            Assert.That(sfx.Paused, Is.True);
            Assert.That(musicSource.isPlaying, Is.False, "a paused voice stops sounding");
            Assert.That(sfxSource.isPlaying, Is.False);
            Assert.That(musicSource.timeSamples, Is.EqualTo(2_000), "the music is held at its playhead");
            Assert.That(sfxSource.timeSamples, Is.EqualTo(1_000), "the loop is held at its playhead");

            AudioPauseRequest resume = new(false);
            resume.EmitUntargeted(_bus);

            Assert.That(music.Paused, Is.False);
            Assert.That(sfx.Paused, Is.False);
            Assert.That(musicSource.isPlaying, Is.True);
            Assert.That(sfxSource.isPlaying, Is.True);
            Assert.That(musicSource.timeSamples, Is.EqualTo(2_000), "resuming continues rather than restarts");
            Assert.That(sfxSource.timeSamples, Is.EqualTo(1_000));
            Assert.That(sfx.HeldVoiceCount, Is.EqualTo(1), "the loop keeps its lease");
            Assert.That(sfx.Muted, Is.True, "the sound setting survives a pause");
            Assert.That(music.Current, Is.SameAs(town));
        }

        private void Unduck(MusicPlayerMessagingCore relay, long duckId)
        {
            MusicUnduckRequest request = new(duckId);
            request.EmitGameObjectTargeted(relay.Player.gameObject, _bus);
        }

        private MusicPlayerMessagingCore Relay(MusicPlayer player)
        {
            MusicPlayerMessagingCore relay = new(player, player.gameObject, _bus);
            _relays.Add(relay);
            relay.Register(Token(1));
            relay.Enable();
            return relay;
        }

        private SfxPlayerMessagingCore SfxRelay(SfxPlayer player)
        {
            SfxPlayerMessagingCore relay = new(player, player.gameObject, _bus);
            _relays.Add(relay);
            relay.Register(Token(3));
            relay.Enable();
            return relay;
        }

        /// <summary>Stages a registration on this test's bus and commits it, as a running component would.</summary>
        private MessageRegistrationToken Token(int instanceId)
        {
            MessageHandler handler = new(new InstanceId(instanceId), _bus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(handler, _bus);
            _tokens.Add(token);
            token.Enable();
            return token;
        }

        private List<MusicTrackChangedMessage> CaptureTrackChanges(MusicPlayerMessagingCore relay)
        {
            List<MusicTrackChangedMessage> heard = new();
            _ = Capture<MusicTrackChangedMessage>(
                relay.Player.gameObject,
                (MusicTrackChangedMessage message) =>
                {
                    heard.Add(message);
                }
            );
            return heard;
        }

        private List<MusicStoppedMessage> CaptureStops(MusicPlayerMessagingCore relay)
        {
            List<MusicStoppedMessage> heard = new();
            _ = Capture<MusicStoppedMessage>(
                relay.Player.gameObject,
                (MusicStoppedMessage message) =>
                {
                    heard.Add(message);
                }
            );
            return heard;
        }

        private List<MusicDuckStartedMessage> CaptureDucks(MusicPlayerMessagingCore relay)
        {
            List<MusicDuckStartedMessage> heard = new();
            _ = Capture<MusicDuckStartedMessage>(
                relay.Player.gameObject,
                (MusicDuckStartedMessage message) =>
                {
                    heard.Add(message);
                }
            );
            return heard;
        }

        /// <summary>Listens for broadcasts from <paramref name="source"/> without a component, on this test's bus.</summary>
        private MessageRegistrationHandle Capture<T>(GameObject source, Action<T> handler)
            where T : DxMessaging.Core.Messages.IBroadcastMessage
        {
            MessageHandler messageHandler = new(new InstanceId(4), _bus) { active = true };
            MessageRegistrationToken token = MessageRegistrationToken.Create(messageHandler, _bus);
            _tokens.Add(token);
            MessageRegistrationHandle registration = token.RegisterGameObjectBroadcast<T>(source, handler);
            token.Enable();
            return registration;
        }

        private static AudioSource PlayingSource(MonoBehaviour player)
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

        private MusicPlayer Player(MusicTrack track = null)
        {
            MusicPlayer player = Made(new GameObject("music")).AddComponent<MusicPlayer>();
            if (track != null)
            {
                player.Library = Made(MusicLibrary.Create(new[] { track }, fadeInSeconds: 0f, fadeOutSeconds: 0f));
            }

            return player;
        }

        private SfxPlayer Player(SfxLibrary library)
        {
            Made(library);
            SfxPlayer player = Made(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private SfxCue Cue(bool loop)
        {
            return Made(SfxCue.Create(new[] { new SfxVariant(Clip()) }, key: "hum", loop: loop));
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
