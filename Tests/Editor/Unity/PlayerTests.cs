namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class PlayerTests
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
        public void TheSfxPlayerRefusesWhatItCannotPlayAndSaysWhy()
        {
            SfxCue oneShot = Cue(loop: false, key: "hit");
            SfxCue loop = Cue(loop: true);
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { oneShot }, voiceCount: 4));
            List<SfxDropReason> drops = new();
            player.CueDropped += (_, reason) => drops.Add(reason);

            Assert.That(player.VoiceCount, Is.EqualTo(4));
            Assert.That(player.Play((SfxCue)null), Is.False);
            Assert.That(player.Play("missing"), Is.False);
            Assert.That(player.Play(loop), Is.False, "a loop sent to the one-shot path would hold a voice forever");
            Assert.That(player.TryPlayLooping(oneShot, out _), Is.False);

            Assert.That(
                drops,
                Is.EqualTo(
                    new[]
                    {
                        SfxDropReason.NotPlayable,
                        SfxDropReason.NotPlayable,
                        SfxDropReason.WrongPlayMethod,
                        SfxDropReason.WrongPlayMethod,
                    }
                )
            );
        }

        [Test]
        public void TheSfxPlayerThrottlesACueInsideItsWindow()
        {
            SfxCue hit = Cue(loop: false, minimumIntervalSeconds: 1_000f);
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { hit }, voiceCount: 4));
            List<SfxDropReason> drops = new();
            player.CueDropped += (_, reason) => drops.Add(reason);

            Assert.That(player.Play(hit), Is.True);
            Assert.That(player.Play(hit), Is.False);
            Assert.That(drops, Is.EqualTo(new[] { SfxDropReason.Throttled }));
        }

        [Test]
        public void ALoopHoldsItsVoiceUntilItsOwnHandleStopsIt()
        {
            SfxCue hum = Cue(loop: true, minimumIntervalSeconds: 0f);
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { hum }, voiceCount: 4, loopingVoiceLimit: 2));
            List<SfxDropReason> drops = new();
            player.CueDropped += (_, reason) => drops.Add(reason);

            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle first), Is.True);
            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle second), Is.True);
            Assert.That(player.TryPlayLooping(hum, out _), Is.False, "the looping limit leaves voices for one-shots");
            Assert.That(drops, Is.EqualTo(new[] { SfxDropReason.LoopLimit }));
            Assert.That(player.HeldVoiceCount, Is.EqualTo(2));
            Assert.That(first.voice, Is.Not.EqualTo(second.voice));

            Assert.That(player.StopLoop(first), Is.True);
            Assert.That(player.StopLoop(first), Is.False, "a handle stops once");
            player.StopAll();
            Assert.That(player.HeldVoiceCount, Is.Zero);
            Assert.That(player.StopLoop(second), Is.False);
        }

        [Test]
        public void AStaleHandleNeverStopsANewerLoopOnItsVoice()
        {
            // The default interval, so a rate limit on loops would refuse the restart in the same frame.
            SfxCue hum = Cue(loop: true, minimumIntervalSeconds: 0.05f);
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { hum }, voiceCount: 1, loopingVoiceLimit: 1));

            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle first), Is.True);
            Assert.That(player.StopLoop(first), Is.True);
            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle reused), Is.True, "a loop is never rate-limited");
            Assert.That(reused.voice, Is.EqualTo(first.voice), "the only voice is reused");
            Assert.That(player.StopLoop(first), Is.False, "a stale handle never stops a newer loop on its voice");
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1));

            player.Library = Made(SfxLibrary.Create(new[] { hum }, voiceCount: 1, loopingVoiceLimit: 1));
            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle rebuilt), Is.True);
            Assert.That(player.StopLoop(first), Is.False, "the first lease is never handed out again after a rebuild");
            Assert.That(player.StopLoop(reused), Is.False, "a handle from before a rebuild matches nothing after it");
            Assert.That(player.StopLoop(rebuilt), Is.True);
        }

        [Test]
        public void MutingReachesEveryVoice()
        {
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { Cue(loop: false) }, voiceCount: 3));
            player.Muted = true;

            foreach (AudioSource voice in player.GetComponentsInChildren<AudioSource>())
            {
                Assert.That(voice.mute, Is.True);
            }

            Assert.That(player.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(3));
        }

        [Test]
        public void DucksComposeRampAndRelease()
        {
            MusicPlayer player = MusicPlayer();
            MusicDuck menu = Made(
                MusicDuck.Create(MusicDuckHolders.One, volumeScale: 0.5f, applySeconds: 1f, releaseSeconds: 2f)
            );
            MusicDuck dialogue = Made(
                MusicDuck.Create(MusicDuckHolders.Many, volumeScale: 0.2f, lowPassCutoffHz: 1_000f, applySeconds: 0f)
            );

            MusicDuckLease menuLease = player.Duck(menu);
            Assert.That(
                player.Attenuation.volumeScale,
                Is.EqualTo(1f).Within(0.001f),
                "a duck ramps rather than jumps"
            );
            player.Tick(1f);
            Assert.That(player.Attenuation.volumeScale, Is.EqualTo(0.5f).Within(0.001f));

            GameObject speaker = Made(new GameObject("speaker"));
            _ = player.Duck(dialogue, speaker);
            Assert.That(
                player.Attenuation,
                Is.EqualTo(new MusicAttenuation(0.2f, 1_000f, MusicAttenuation.OpenHighPassCutoffHz))
            );
            Assert.That(player.IsDucked(dialogue), Is.True);

            Object.DestroyImmediate(speaker);
            player.Tick(0.5f);
            Assert.That(player.IsDucked(dialogue), Is.False, "a holder destroyed without releasing is released for it");
            Assert.That(player.TargetAttenuation, Is.EqualTo(menu.Attenuation));

            player.Tick(10f);
            Assert.That(player.Release(menuLease), Is.True);
            player.Tick(1f);
            Assert.That(
                player.Attenuation.volumeScale,
                Is.InRange(0.5f, 1f),
                "halfway through the menu's two-second release"
            );
            player.Tick(1f);
            Assert.That(player.Attenuation, Is.EqualTo(MusicAttenuation.Unattenuated));
        }

        [Test]
        public void OverridesAndContextsChooseTheMusic()
        {
            MusicTrack town = Made(MusicTrack.Create(Clip(), context: "town"));
            MusicTrack boss = Made(MusicTrack.Create(Clip()));
            MusicPlayer player = MusicPlayer();
            player.Library = Made(MusicLibrary.Create(new[] { town }, fadeInSeconds: 0f, fadeOutSeconds: 0f));

            Assert.That(player.PlayContext("cave"), Is.False);
            Assert.That(player.Current, Is.Null);
            Assert.That(player.PlayContext("town"), Is.True);
            Assert.That(player.Current, Is.SameAs(town));

            using (player.Override(boss))
            {
                Assert.That(player.Current, Is.SameAs(boss));
                Assert.That(player.IsOverridden, Is.True);
            }

            Assert.That(player.Current, Is.SameAs(town), "disposing the lease hands back");
            player.Stop();
            Assert.That(player.Current, Is.Null);
        }

        [Test]
        public void ADestroyedOverrideTrackHandsTheMusicBack()
        {
            MusicTrack town = Made(MusicTrack.Create(Clip(), context: "town"));
            MusicTrack boss = Made(MusicTrack.Create(Clip()));
            MusicOverride<MusicTrack> selection = new();
            _ = selection.SetContextTrack(town);
            MusicOverrideLease lease = selection.Take(boss);

            Object.DestroyImmediate(boss);

            Assert.That(selection.Current, Is.SameAs(town), "a destroyed override no longer counts");
            Assert.That(selection.Take(boss).IsValid, Is.False, "a destroyed track is refused");
            Assert.That(selection.Release(lease), Is.False, "the context's music was already wanted");
            Assert.That(selection.Current, Is.SameAs(town));
        }

        private SfxPlayer SfxPlayer(SfxLibrary library)
        {
            Made(library);
            SfxPlayer player = Made(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private MusicPlayer MusicPlayer()
        {
            return Made(new GameObject("music")).AddComponent<MusicPlayer>();
        }

        private SfxCue Cue(bool loop, string key = null, float minimumIntervalSeconds = 0f)
        {
            return Made(
                SfxCue.Create(
                    new[] { new SfxVariant(Clip()) },
                    key: key,
                    loop: loop,
                    minimumIntervalSeconds: minimumIntervalSeconds
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
