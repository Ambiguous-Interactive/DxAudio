namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class PlayerTests
    {
        [Test]
        public void TheSfxPlayerRefusesWhatItCannotPlayAndSaysWhy()
        {
            using MadeObjects made = new();
            SfxCue oneShot = Cue(made, loop: false, key: "hit");
            SfxCue loop = Cue(made, loop: true);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { oneShot }, voiceCount: 4));
            List<SfxDropReason> drops = new();
            player.CueDropped += (_, reason) => drops.Add(reason);

            Assert.That(player.VoiceCount, Is.EqualTo(4));
            Assert.That(player.Play((SfxCue)null), Is.False);
            Assert.That(player.Play("missing"), Is.False);
            Assert.That(player.Play(loop), Is.False, "a loop sent to the one-shot path would hold a voice forever");
            Assert.That(player.TryPlayLooping(oneShot, out _), Is.False);

            Assert.That(drops, Is.EqualTo(new[] { SfxDropReason.NotPlayable, SfxDropReason.NotPlayable, SfxDropReason.WrongPlayMethod, SfxDropReason.WrongPlayMethod }));
        }

        [Test]
        public void TheSfxPlayerThrottlesACueInsideItsWindow()
        {
            using MadeObjects made = new();
            SfxCue hit = Cue(made, loop: false, minimumIntervalSeconds: 1_000f);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { hit }, voiceCount: 4));
            List<SfxDropReason> drops = new();
            player.CueDropped += (_, reason) => drops.Add(reason);

            Assert.That(player.Play(hit), Is.True);
            Assert.That(player.Play(hit), Is.False);
            Assert.That(drops, Is.EqualTo(new[] { SfxDropReason.Throttled }));
        }

        [Test]
        public void ALoopHoldsItsVoiceUntilItsOwnHandleStopsIt()
        {
            using MadeObjects made = new();
            SfxCue hum = Cue(made, loop: true, minimumIntervalSeconds: 0f);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { hum }, voiceCount: 4, loopingVoiceLimit: 2));
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
            using MadeObjects made = new();
            // The default interval, so a rate limit on loops would refuse the restart in the same frame.
            SfxCue hum = Cue(made, loop: true, minimumIntervalSeconds: 0.05f);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { hum }, voiceCount: 1, loopingVoiceLimit: 1));

            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle first), Is.True);
            Assert.That(player.StopLoop(first), Is.True);
            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle reused), Is.True, "a loop is never rate-limited");
            Assert.That(reused.voice, Is.EqualTo(first.voice), "the only voice is reused");
            Assert.That(player.StopLoop(first), Is.False, "a stale handle never stops a newer loop on its voice");
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1));

            player.Library = made.Track(SfxLibrary.Create(new[] { hum }, voiceCount: 1, loopingVoiceLimit: 1));
            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle rebuilt), Is.True);
            Assert.That(player.StopLoop(first), Is.False, "the first lease is never handed out again after a rebuild");
            Assert.That(player.StopLoop(reused), Is.False, "a handle from before a rebuild matches nothing after it");
            Assert.That(player.StopLoop(rebuilt), Is.True);
        }

        [Test]
        public void MutingReachesEveryVoice()
        {
            using MadeObjects made = new();
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { Cue(made, loop: false) }, voiceCount: 3));
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
            using MadeObjects made = new();
            MusicPlayer player = MusicPlayer(made);
            MusicDuck menu = made.Track(MusicDuck.Create(MusicDuckHolders.One, volumeScale: 0.5f, applySeconds: 1f, releaseSeconds: 2f));
            MusicDuck dialogue = made.Track(MusicDuck.Create(MusicDuckHolders.Many, volumeScale: 0.2f, lowPassCutoffHz: 1_000f, applySeconds: 0f));

            MusicDuckLease menuLease = player.Duck(menu);
            Assert.That(player.Attenuation.volumeScale, Is.EqualTo(1f).Within(0.001f), "a duck ramps rather than jumps");
            player.Tick(1f);
            Assert.That(player.Attenuation.volumeScale, Is.EqualTo(0.5f).Within(0.001f));

            GameObject speaker = made.Track(new GameObject("speaker"));
            _ = player.Duck(dialogue, speaker);
            Assert.That(player.Attenuation, Is.EqualTo(new MusicAttenuation(0.2f, 1_000f, MusicAttenuation.OpenHighPassCutoffHz)));
            Assert.That(player.IsDucked(dialogue), Is.True);

            Object.DestroyImmediate(speaker);
            player.Tick(0.5f);
            Assert.That(player.IsDucked(dialogue), Is.False, "a holder destroyed without releasing is released for it");
            Assert.That(player.TargetAttenuation, Is.EqualTo(menu.Attenuation));

            player.Tick(10f);
            Assert.That(player.Release(menuLease), Is.True);
            player.Tick(1f);
            Assert.That(player.Attenuation.volumeScale, Is.InRange(0.5f, 1f), "halfway through the menu's two-second release");
            player.Tick(1f);
            Assert.That(player.Attenuation, Is.EqualTo(MusicAttenuation.Unattenuated));
        }

        [Test]
        public void OverridesAndContextsChooseTheMusic()
        {
            using MadeObjects made = new();
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicTrack boss = made.Track(MusicTrack.Create(Clip(made)));
            MusicPlayer player = MusicPlayer(made);
            player.Library = made.Track(MusicLibrary.Create(new[] { town }, fadeInSeconds: 0f, fadeOutSeconds: 0f));

            Assert.That(player.PlayContext("cave"), Is.False);
            Assert.That(player.Current == null);
            Assert.That(player.PlayContext("town"), Is.True);
            Assert.That(player.Current, Is.SameAs(town));

            using (player.Override(boss))
            {
                Assert.That(player.Current, Is.SameAs(boss));
                Assert.That(player.IsOverridden, Is.True);
            }

            Assert.That(player.Current, Is.SameAs(town), "disposing the lease hands back");
            player.Stop();
            Assert.That(player.Current == null);
        }

        [Test]
        public void ADestroyedOverrideTrackHandsTheMusicBack()
        {
            using MadeObjects made = new();
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicTrack boss = made.Track(MusicTrack.Create(Clip(made)));
            List<MusicTrack> heard = new();
            MusicOverride<MusicTrack> selection = new(heard.Add);
            _ = selection.SetContextTrack(town);
            MusicOverrideLease lease = selection.Take(boss);

            Object.DestroyImmediate(boss);

            Assert.That(selection.Current, Is.SameAs(town), "a destroyed override no longer counts");
            Assert.That(selection.Take(boss).IsValid, Is.False, "a destroyed track is refused");
            Assert.That(selection.Release(lease), Is.True, "the player still plays the destroyed track");
            Assert.That(heard, Has.Count.EqualTo(3));
            Assert.That(heard[2], Is.SameAs(town), "the release hands the music back");
        }

        [Test]
        public void PausingHoldsEachPlayerAtItsPlayheadAndResumingContinues()
        {
            using MadeObjects made = new();
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicPlayer music = MusicPlayer(made);
            music.Library = made.Track(MusicLibrary.Create(new[] { town }, fadeInSeconds: 0f, fadeOutSeconds: 0f));
            SfxCue hum = Cue(made, loop: true);
            SfxPlayer sfx = SfxPlayer(made, SfxLibrary.Create(new[] { hum }, voiceCount: 2, loopingVoiceLimit: 1));
            Assert.That(music.PlayContext("town"), Is.True);
            Assert.That(sfx.TryPlayLooping(hum, out SfxLoopHandle loop), Is.True);
            try
            {
                sfx.Muted = true;

                AudioSource musicSource = PlayingSource(music);
                AudioSource sfxSource = PlayingSource(sfx);
                Assert.That(musicSource != null && sfxSource != null, "both players sound before the pause");
                musicSource.timeSamples = 2_000;
                sfxSource.timeSamples = 1_000;

                music.Paused = true;
                sfx.Paused = true;

                Assert.That(musicSource.isPlaying, Is.False, "a paused voice stops sounding");
                Assert.That(sfxSource.isPlaying, Is.False);
                Assert.That(musicSource.timeSamples, Is.EqualTo(2_000), "the music is held at its playhead");
                Assert.That(sfxSource.timeSamples, Is.EqualTo(1_000), "the loop is held at its playhead");
                Assert.That(sfx.HeldVoiceCount, Is.EqualTo(1), "a pause is not a stop");

                music.Paused = false;
                sfx.Paused = false;

                Assert.That(musicSource.isPlaying, Is.True);
                Assert.That(sfxSource.isPlaying, Is.True);
                Assert.That(musicSource.timeSamples, Is.EqualTo(2_000).Within(64), "resuming continues rather than restarts");
                Assert.That(sfxSource.timeSamples, Is.EqualTo(1_000).Within(64));
                Assert.That(sfx.HeldVoiceCount, Is.EqualTo(1), "the loop keeps its lease");
                Assert.That(sfx.Muted, Is.True, "the sound setting survives a pause");
            }
            finally
            {
                _ = sfx.StopLoop(loop);
            }
        }

        [Test]
        public void VoicesStartedDuringAPauseWaitAtTheirStart()
        {
            using MadeObjects made = new();
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicPlayer music = MusicPlayer(made);
            music.Library = made.Track(MusicLibrary.Create(new[] { town }, fadeInSeconds: 0f, fadeOutSeconds: 0f));
            SfxCue hit = Cue(made, loop: false, key: "hit");
            SfxPlayer sfx = SfxPlayer(made, SfxLibrary.Create(new[] { hit }, voiceCount: 1));
            music.Paused = true;
            sfx.Paused = true;

            Assert.That(music.PlayContext("town"), Is.True);
            Assert.That(sfx.Play(hit), Is.True);
            AudioSource musicVoice = SourceWithClip(music, town.Clip);
            Assert.That(hit.TryGetPlayableVariant(0, out SfxVariant variant), Is.True);
            AudioSource sfxVoice = SourceWithClip(sfx, variant.Clip);

            Assert.That(musicVoice != null && sfxVoice != null, "a started voice holds its clip while paused");
            Assert.That(musicVoice.isPlaying, Is.False, "music started during a pause waits");
            Assert.That(sfxVoice.isPlaying, Is.False, "a cue started during a pause waits");
            Assert.That(musicVoice.timeSamples, Is.Zero);
            Assert.That(sfxVoice.timeSamples, Is.Zero);

            music.Paused = false;
            sfx.Paused = false;
            Assert.That(musicVoice.isPlaying, Is.True);
            Assert.That(sfxVoice.isPlaying, Is.True);
        }

        [Test]
        public void ACueStartedDuringAPauseMayReuseAPausedOneShot()
        {
            using MadeObjects made = new();
            SfxCue hit = Cue(made, loop: false, key: "hit");
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { hit }, voiceCount: 1));
            Assert.That(player.Play(hit), Is.True);
            player.Paused = true;

            Assert.That(player.Play(hit), Is.True, "a paused one-shot is not audible, so its voice can be reused");
            Assert.That(player.DropCounts.Total, Is.Zero);
            Assert.That(player.VoiceCount, Is.EqualTo(1));
            Assert.That(PlayingSource(player) == null, "the reused voice waits at its start");
        }

        [Test]
        public void ALowerPriorityCueDoesNotReplaceAPausedOneShot()
        {
            using MadeObjects made = new();
            SfxCue high = Cue(made, loop: false, key: "high", priority: SfxPriority.High);
            SfxCue low = Cue(made, loop: false, key: "low", priority: SfxPriority.Low);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { high, low }, voiceCount: 1));
            Assert.That(player.Play(high), Is.True);
            player.Paused = true;

            Assert.That(player.Play(low), Is.False, "the paused High cue will resume, so Low cannot take its voice");
            Assert.That(player.DropCounts.Total, Is.EqualTo(1));
        }

        [Test]
        public void TheMusicPlayerReportsOneStopWhenItStopsWhilePaused()
        {
            using MadeObjects made = new();
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicPlayer player = MusicPlayer(made);
            player.Library = made.Track(MusicLibrary.Create(new[] { town }, fadeInSeconds: 0f, fadeOutSeconds: 0f));
            int stops = 0;
            player.Stopped += () =>
            {
                ++stops;
            };
            Assert.That(player.PlayContext("town"), Is.True);
            player.Paused = true;

            player.Stop();

            Assert.That(stops, Is.EqualTo(1), "one stop is one fact");
            Assert.That(player.Current == null);
            player.Paused = false;
            player.Tick(1f);
            Assert.That(stops, Is.EqualTo(1), "finishing the fade reports nothing more");
        }

        [Test]
        public void ANonLoopingTrackReportsStoppedWhenItEnds()
        {
            using MadeObjects made = new();
            MusicTrack sting = made.Track(MusicTrack.Create(Clip(made), context: "sting", loop: false));
            MusicPlayer player = MusicPlayer(made);
            player.Library = made.Track(MusicLibrary.Create(new[] { sting }, fadeInSeconds: 0f, fadeOutSeconds: 0f));
            int stops = 0;
            player.Stopped += () =>
            {
                ++stops;
            };
            Assert.That(player.PlayContext("sting"), Is.True);
            AudioSource source = PlayingSource(player);
            Assert.That(source != null);

            source.Stop();
            player.Tick(0.1f);

            Assert.That(player.IsPlaying, Is.False, "a finished track is no longer sounding");
            Assert.That(stops, Is.EqualTo(1));
            Assert.That(player.PlayContext("sting"), Is.True, "the same context can start again");
            Assert.That(player.IsPlaying, Is.True);
        }

        [Test]
        public void TheMusicPlayerReportsTheTrackThatPlaysAndTheStop()
        {
            using MadeObjects made = new();
            MusicTrack town = made.Track(MusicTrack.Create(Clip(made), context: "town"));
            MusicPlayer player = MusicPlayer(made);
            player.Library = made.Track(MusicLibrary.Create(new[] { town }, fadeInSeconds: 0f, fadeOutSeconds: 0f));
            List<MusicTrack> changed = new();
            int stops = 0;
            player.TrackChanged += changed.Add;
            player.Stopped += () =>
            {
                ++stops;
            };

            Assert.That(player.PlayContext("town"), Is.True);
            Assert.That(changed, Is.EqualTo(new[] { town }), "the change names the track that plays");

            player.Stop();
            Assert.That(stops, Is.EqualTo(1));
            Assert.That(player.Current == null);
        }

        private static SfxPlayer SfxPlayer(MadeObjects made, SfxLibrary library)
        {
            made.Track(library);
            SfxPlayer player = made.Track(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
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

        private static AudioSource SourceWithClip(MonoBehaviour player, AudioClip clip)
        {
            AudioSource[] sources = player.GetComponentsInChildren<AudioSource>();
            for (int index = 0; index < sources.Length; ++index)
            {
                if (sources[index].clip == clip)
                {
                    return sources[index];
                }
            }

            return null;
        }

        private static MusicPlayer MusicPlayer(MadeObjects made)
        {
            return made.Track(new GameObject("music")).AddComponent<MusicPlayer>();
        }

        private static SfxCue Cue(MadeObjects made, bool loop, string key = null, float minimumIntervalSeconds = 0f, SfxPriority priority = SfxPriority.Medium)
        {
            return made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, key: key, priority: priority, loop: loop, minimumIntervalSeconds: minimumIntervalSeconds));
        }

        private static AudioClip Clip(MadeObjects made)
        {
            return made.Track(AudioClip.Create("test clip", 4_000, 1, 8_000, stream: false));
        }
    }
}
