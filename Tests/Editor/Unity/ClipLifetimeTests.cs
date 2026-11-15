namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>What the players promise when a clip is released while a voice still refers to it.</summary>
    public sealed class ClipLifetimeTests
    {
        private readonly List<Object> _made = new();

        [TearDown]
        public void TearDown()
        {
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
        public void AReleasedCueClipIsNeverSentToAVoice()
        {
            AudioClip clip = Clip();
            SfxCue cue = Cue(clip, loop: false);
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { cue }, voiceCount: 2));
            List<SfxDropReason> drops = new();
            player.CueDropped += (_, reason) => drops.Add(reason);

            Assert.That(player.Play(cue), Is.True);

            Object.DestroyImmediate(clip);

            Assert.That(player.Play(cue), Is.False, "a cue whose every clip is released has nothing to play");
            Assert.That(drops, Is.EqualTo(new[] { SfxDropReason.NotPlayable }));
        }

        [Test]
        public void AReleasedLoopClipFreesItsVoiceAndItsHandle()
        {
            AudioClip clip = Clip();
            SfxCue hum = Cue(clip, loop: true);
            SfxCue other = Cue(Clip(), loop: true);
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { hum }, voiceCount: 2, loopingVoiceLimit: 1));

            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle held), Is.True);
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1));

            Object.DestroyImmediate(clip);
            player.ReclaimLostHolds();

            Assert.That(player.HeldVoiceCount, Is.Zero, "a released clip must not hold a voice for the session");
            Assert.That(player.StopLoop(held), Is.False, "the hold is gone, so its handle stops nothing");
            Assert.That(
                player.TryPlayLooping(other, out _),
                Is.True,
                "a released clip no longer counts against the looping limit"
            );
        }

        [Test]
        public void AReleasedClipLeavesItsVoiceFreeForTheNextCue()
        {
            AudioClip released = Clip();
            SfxCue hit = Cue(released, loop: false);
            SfxCue next = Cue(Clip(), loop: false);
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { hit, next }, voiceCount: 1));

            Assert.That(player.Play(hit), Is.True, "the pool has one voice, and the cue takes it");

            Object.DestroyImmediate(released);

            Assert.That(
                player.Play(next),
                Is.True,
                "a voice whose clip was released makes no sound, so it is free rather than busy"
            );
        }

        [Test]
        public void AReleasedMusicClipStopsTheVoiceAndTheNextTrackStarts()
        {
            AudioClip released = Clip();
            MusicTrack town = Made(MusicTrack.Create(released, context: "town"));
            MusicTrack cave = Made(MusicTrack.Create(Clip(), context: "cave"));
            MusicPlayer player = MusicPlayer(
                MusicLibrary.Create(new[] { town, cave }, fadeInSeconds: 0f, fadeOutSeconds: 0f)
            );

            Assert.That(player.PlayContext("town"), Is.True);
            Assert.That(player.IsPlaying, Is.True);

            Object.DestroyImmediate(released);
            player.Tick(0.016f);

            Assert.That(player.IsPlaying, Is.False, "a released clip stops the music instead of reporting playback");
            Assert.That(player.PlayContext("town"), Is.True, "the context still exists");
            Assert.That(player.Current == null, "a track whose clip is released is refused, not faded in");
            Assert.That(player.PlayContext("cave"), Is.True);
            Assert.That(player.Current, Is.SameAs(cave));
            Assert.That(player.IsPlaying, Is.True, "the released clip does not stick as what is sounding");
        }

        private SfxPlayer SfxPlayer(SfxLibrary library)
        {
            Made(library);
            SfxPlayer player = Made(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private MusicPlayer MusicPlayer(MusicLibrary library)
        {
            Made(library);
            MusicPlayer player = Made(new GameObject("music")).AddComponent<MusicPlayer>();
            player.Library = library;
            return player;
        }

        private SfxCue Cue(AudioClip clip, bool loop)
        {
            return Made(SfxCue.Create(new[] { new SfxVariant(clip) }, loop: loop, minimumIntervalSeconds: 0f));
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
