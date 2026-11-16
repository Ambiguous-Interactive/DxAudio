namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>What the players promise when a clip is released while a voice still refers to it.</summary>
    public sealed class ClipLifetimeTests
    {
        [Test]
        public void AReleasedCueIsRefusedAsNotPlayable()
        {
            using MadeObjects made = new();
            AudioClip clip = Clip(made);
            SfxCue cue = Cue(made, clip, loop: false);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { cue }, voiceCount: 2));
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
            using MadeObjects made = new();
            AudioClip clip = Clip(made);
            SfxCue hum = Cue(made, clip, loop: true);
            SfxCue other = Cue(made, Clip(made), loop: true);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { hum }, voiceCount: 2, loopingVoiceLimit: 1));

            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle held), Is.True);
            Assert.That(player.HeldVoiceCount, Is.EqualTo(1));

            Object.DestroyImmediate(clip);

            // The same entry point the component's Update calls each frame. A Unity message cannot run in EditMode.
            player.ReclaimLostHolds();
            int heldAfterRelease = player.HeldVoiceCount;
            bool staleStops = player.StopLoop(held);
            bool restarted = player.TryPlayLooping(other, out SfxLoopHandle restartedHandle);

            Assert.That(heldAfterRelease, Is.Zero, "a released clip must not hold a voice for the session");
            Assert.That(staleStops, Is.False, "the hold is gone, so its handle stops nothing");
            Assert.That(restarted, Is.True, "a released clip no longer counts against the looping limit");
            Assert.That(player.StopLoop(restartedHandle), Is.True);
        }

        [Test]
        public void AReleasedClipLeavesItsVoiceFreeForTheNextCue()
        {
            using MadeObjects made = new();
            AudioClip released = Clip(made);
            SfxCue hit = Cue(made, released, loop: false);
            SfxCue next = Cue(made, Clip(made), loop: false);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { hit, next }, voiceCount: 1));

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
            using MadeObjects made = new();
            AudioClip released = Clip(made);
            MusicTrack town = made.Track(MusicTrack.Create(released, context: "town"));
            MusicTrack cave = made.Track(MusicTrack.Create(Clip(made), context: "cave"));
            MusicPlayer player = MusicPlayer(
                made,
                MusicLibrary.Create(new[] { town, cave }, fadeInSeconds: 0f, fadeOutSeconds: 0f)
            );

            Assert.That(player.PlayContext("town"), Is.True);
            Assert.That(player.IsPlaying, Is.True);

            Object.DestroyImmediate(released);

            Assert.That(player.IsPlaying, Is.False, "a released clip is not music anyone can hear");
            player.Tick(0.016f);
            Assert.That(player.IsPlaying, Is.False, "the voice stops instead of reporting playback");
            Assert.That(player.PlayContext("town"), Is.True, "the context still exists");
            Assert.That(player.Current == null, "a track whose clip is released is refused, not faded in");
            Assert.That(player.PlayContext("cave"), Is.True);
            Assert.That(player.Current, Is.SameAs(cave));
            Assert.That(player.IsPlaying, Is.True, "the released clip does not stick as what is sounding");
        }

        [Test]
        public void AContextAsksForItsMusicAgainOnceAClipIsBack()
        {
            using MadeObjects made = new();
            AudioClip released = Clip(made);
            MusicTrack town = made.Track(MusicTrack.Create(released, context: "town"));
            MusicPlayer player = MusicPlayer(
                made,
                MusicLibrary.Create(new[] { town }, fadeInSeconds: 0f, fadeOutSeconds: 0f)
            );

            Assert.That(player.PlayContext("town"), Is.True);
            Assert.That(player.IsPlaying, Is.True);
            Object.DestroyImmediate(released);
            Assert.That(player.IsPlaying, Is.False, "the released clip stopped the music");

            AudioClip returned = Clip(made);
            using (SerializedObject serialized = new(town))
            {
                SerializedProperty clip = serialized.FindProperty("_clip");
                Assert.That(clip != null, "MusicTrack keeps a private _clip field for this test");
                clip.objectReferenceValue = returned;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // No tick in between: a clip can come back in the frame that stopped the voice.
            Assert.That(player.PlayContext("town"), Is.True);
            Assert.That(player.Current, Is.SameAs(town), "the same music is still what the context wants");
            Assert.That(player.IsPlaying, Is.True, "the selection did not change, but nothing was sounding it");

            player.Tick(0.016f);
            Assert.That(player.IsPlaying, Is.True, "the frame sweep keeps the new clip playing");
        }

        [Test]
        public void ALoopWhoseClipDataWasUnloadedFreesItsVoice()
        {
            using MadeObjects made = new();
            AudioClip unloaded = ImportedClip();
            try
            {
                SfxCue hum = Cue(made, unloaded, loop: true);
                SfxCue other = Cue(made, Clip(made), loop: true);
                SfxPlayer player = SfxPlayer(
                    made,
                    SfxLibrary.Create(new[] { hum }, voiceCount: 2, loopingVoiceLimit: 1)
                );

                Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle held), Is.True);
                Assert.That(player.HeldVoiceCount, Is.EqualTo(1));
                Assert.That(unloaded.UnloadAudioData(), Is.True, "an imported clip unloads its audio data");
                Assert.That(unloaded.loadState, Is.EqualTo(AudioDataLoadState.Unloaded));

                player.ReclaimLostHolds();
                int heldAfterUnload = player.HeldVoiceCount;
                bool staleStops = player.StopLoop(held);
                _ = player.TryGetVoiceState(held.voice, out SfxVoiceState read, out _);

                Assert.That(heldAfterUnload, Is.Zero, "a clip with no audio data cannot hold a voice");
                Assert.That(staleStops, Is.False, "the hold is gone, so its handle stops nothing");
                Assert.That(read.isPlaying, Is.False, "a voice over a clip with no data is not busy");
                Assert.That(
                    player.TryPlayLooping(other, out SfxLoopHandle restarted),
                    Is.True,
                    "a clip with no data no longer counts against the looping limit"
                );
                Assert.That(player.StopLoop(restarted), Is.True);
            }
            finally
            {
                Assert.That(unloaded.LoadAudioData(), Is.True, "the fixture leaves its clip playable");
            }
        }

        [Test]
        public void MusicWhoseClipDataWasUnloadedStopsTheVoice()
        {
            using MadeObjects made = new();
            AudioClip unloaded = ImportedClip();
            try
            {
                MusicTrack town = made.Track(MusicTrack.Create(unloaded, context: "town"));
                MusicTrack cave = made.Track(MusicTrack.Create(Clip(made), context: "cave"));
                MusicPlayer player = MusicPlayer(
                    made,
                    MusicLibrary.Create(new[] { town, cave }, fadeInSeconds: 0f, fadeOutSeconds: 0f)
                );

                Assert.That(player.PlayContext("town"), Is.True);
                Assert.That(player.IsPlaying, Is.True);
                Assert.That(unloaded.UnloadAudioData(), Is.True, "an imported clip unloads its audio data");

                Assert.That(player.IsPlaying, Is.False, "music over a clip with no data makes no sound");
                player.Tick(0.016f);
                Assert.That(player.IsPlaying, Is.False, "the voice stops instead of reporting playback");
                Assert.That(player.PlayContext("cave"), Is.True);
                Assert.That(player.IsPlaying, Is.True, "a voice the unloaded clip gave up plays the next track");
            }
            finally
            {
                Assert.That(unloaded.LoadAudioData(), Is.True, "the fixture leaves its clip playable");
            }
        }

        /// <summary>The committed sound file, with its audio data loaded: only an imported clip can unload data.</summary>
        private static AudioClip ImportedClip()
        {
            string[] candidates = AssetDatabase.FindAssets("t:AudioClip");
            for (int index = 0; index < candidates.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(candidates[index]);
                AudioClip found = path.EndsWith("/Data/TestTone.wav", StringComparison.Ordinal)
                    ? AssetDatabase.LoadAssetAtPath<AudioClip>(path)
                    : null;
                if (found == null)
                {
                    continue;
                }

                // A freshly imported clip reports its data as unloaded until something loads it.
                Assert.That(found.LoadAudioData(), Is.True, "the fixture clip loads its audio data");
                Assert.That(found.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
                return found;
            }

            Assert.Fail("Tests/Editor/Unity/Data/TestTone.wav is missing or was not imported as an AudioClip.");
            return null;
        }

        private static SfxPlayer SfxPlayer(MadeObjects made, SfxLibrary library)
        {
            made.Track(library);
            SfxPlayer player = made.Track(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private static MusicPlayer MusicPlayer(MadeObjects made, MusicLibrary library)
        {
            made.Track(library);
            MusicPlayer player = made.Track(new GameObject("music")).AddComponent<MusicPlayer>();
            player.Library = library;
            return player;
        }

        private static SfxCue Cue(MadeObjects made, AudioClip clip, bool loop)
        {
            return made.Track(SfxCue.Create(new[] { new SfxVariant(clip) }, loop: loop, minimumIntervalSeconds: 0f));
        }

        private static AudioClip Clip(MadeObjects made)
        {
            return made.Track(AudioClip.Create("test clip", 4_000, 1, 8_000, stream: false));
        }

        private sealed class MadeObjects : IDisposable
        {
            private readonly List<Object> _made = new();

            public T Track<T>(T made)
                where T : Object
            {
                _made.Add(made);
                return made;
            }

            public void Dispose()
            {
                for (int index = 0; index < _made.Count; ++index)
                {
                    if (_made[index] != null)
                    {
                        Object.DestroyImmediate(_made[index]);
                    }
                }

                _made.Clear();
            }
        }
    }
}
