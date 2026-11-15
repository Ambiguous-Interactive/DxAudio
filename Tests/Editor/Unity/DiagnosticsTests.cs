namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    public sealed class DiagnosticsTests
    {
        /// <summary>Samples per heap probe. Unity's Mono reports nothing for a few hundred KB, so the loop is long.</summary>
        private const int SampleCount = 20_000;

        [Test]
        public void DroppedCuesAreCountedByReason()
        {
            using MadeObjects made = new();
            SfxCue hit = Cue(made, loop: false, minimumIntervalSeconds: 1_000f);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { hit }, voiceCount: 1));

            Assert.That(player.Play((SfxCue)null), Is.False);
            Assert.That(player.Play("missing"), Is.False);
            Assert.That(player.Play(hit), Is.True);
            Assert.That(player.Play(hit), Is.False);

            SfxDropCounts counts = player.DropCounts;
            Assert.That(counts[SfxDropReason.NotPlayable], Is.EqualTo(2), "a null cue and a missing key");
            Assert.That(counts[SfxDropReason.Throttled], Is.EqualTo(1));
            Assert.That(counts.Total, Is.EqualTo(3));
        }

        [Test]
        public void VoiceStateCarriesItsPriorityAndLoopLease()
        {
            using MadeObjects made = new();
            SfxCue hum = Cue(made, loop: true, priority: SfxPriority.High);
            SfxPlayer player = SfxPlayer(made, SfxLibrary.Create(new[] { hum }, voiceCount: 2));

            Assert.That(player.TryPlayLooping(hum, out SfxLoopHandle handle), Is.True);
            Assert.That(player.TryGetVoiceState(handle.voice, out SfxVoiceState state, out int lease), Is.True);
            Assert.That(player.StopLoop(handle), Is.True);
            Assert.That(state.isHeld, Is.True);
            Assert.That(state.priority, Is.EqualTo(SfxPriority.High));
            Assert.That(lease, Is.EqualTo(handle.lease), "the lease is the handle's loop lease");

            Assert.That(player.TryGetVoiceState(7, out _, out int outOfRange), Is.False);
            Assert.That(outOfRange, Is.Zero);
        }

        [Test]
        public void TheOverlayReportsVoicesDucksAndDrops()
        {
            using MadeObjects made = new();
            SfxCue hum = Cue(made, loop: true, priority: SfxPriority.High);
            SfxPlayer sfx = SfxPlayer(made, SfxLibrary.Create(new[] { hum }, voiceCount: 2));
            Assert.That(sfx.TryPlayLooping(hum, out SfxLoopHandle handle), Is.True);
            Assert.That(sfx.Play("missing"), Is.False);

            MusicPlayer music = made.Track(new GameObject("music")).AddComponent<MusicPlayer>();
            MusicDuck dialogue = made.Track(MusicDuck.Create(MusicDuckHolders.Many, volumeScale: 0.5f));
            GameObject speaker = made.Track(new GameObject("Speaker"));
            _ = music.Duck(dialogue, speaker);

            AudioDiagnosticsOverlay overlay = Overlay(made, sfx, music);
            overlay.Enabled = true;
            overlay.Sample();

            string text = overlay.Text;
            Assert.That(sfx.StopLoop(handle), Is.True);
            Assert.That(text, Does.Contain($"loop lease {handle.lease}"));
            Assert.That(text, Does.Contain("High"));
            Assert.That(text, Does.Contain("Dropped: 1 (NotPlayable 1)"));
            Assert.That(text, Does.Contain("Ducks: 1"));
            Assert.That(text, Does.Contain("held by Speaker"));
        }

        [Test]
        public void SamplingRebuildsTheReportOnlyWhileTheOverlayIsEnabled()
        {
            using MadeObjects made = new();
            SfxCue hum = Cue(made, loop: true, priority: SfxPriority.High);
            SfxPlayer sfx = SfxPlayer(made, SfxLibrary.Create(new[] { hum }, voiceCount: 2));
            Assert.That(sfx.TryPlayLooping(hum, out SfxLoopHandle handle), Is.True);
            MusicPlayer music = made.Track(new GameObject("music")).AddComponent<MusicPlayer>();
            AudioDiagnosticsOverlay overlay = Overlay(made, sfx, music);

            Assert.That(overlay.Enabled, Is.False);
            for (int index = 0; index < 32; index++)
            {
                overlay.Sample();
                Assert.That(overlay.Text, Is.SameAs(string.Empty), "a switched-off overlay rebuilds no report");
            }

            overlay.Enabled = true;
            for (int index = 0; index < 32; index++)
            {
                overlay.Sample();
            }

            long enabledGrowth = HeapGrowth(overlay, samples: SampleCount);
            Assert.That(
                enabledGrowth,
                Is.GreaterThan(1_048_576L),
                $"the probe must see the enabled path allocate; {SampleCount} samples grew the heap by {enabledGrowth} bytes"
            );

            Assert.That(sfx.StopLoop(handle), Is.True);
            overlay.Enabled = false;
            Assert.That(overlay.Text, Is.Empty);
        }

        [Test]
        public void TheOverlaySkipsTheLayoutPhaseWhileItIsOff()
        {
            using MadeObjects made = new();
            SfxCue hit = Cue(made, loop: false);
            SfxPlayer sfx = SfxPlayer(made, SfxLibrary.Create(new[] { hit }, voiceCount: 1));
            AudioDiagnosticsOverlay overlay = Overlay(made, sfx, null);

            Assert.That(overlay.useGUILayout, Is.False, "an overlay that is off must not run a layout pass");
            overlay.Enabled = true;
            Assert.That(overlay.useGUILayout, Is.True, "the open overlay lays out its label and scroll view");
            overlay.Sample();
            Assert.That(overlay.Text, Is.Not.Empty);

            using SerializedObject serialized = new(overlay);
            SerializedProperty enabled = serialized.FindProperty("_enabled");
            Assert.That(enabled != null, "the overlay keeps an _enabled field for the Inspector");
            enabled.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(overlay.useGUILayout, Is.False, "an Inspector edit to Enabled stops the layout pass too");
            Assert.That(overlay.Text, Is.Empty, "and it clears the report");
        }

        /// <summary>
        /// The heap growth of <paramref name="samples"/> samples of an enabled overlay, after a collection. Unity's Mono
        /// reports zero from <c>GC.GetAllocatedBytesForCurrentThread</c>, so the heap is the probe that can move here.
        /// </summary>
        /// <remarks>
        /// Every sampled report is kept and both ends of the window force a collection, so the probe reads live bytes.
        /// A plain heap reading was order dependent: a full suite run reported 0 to 16 KB for the same work. Unity's
        /// Mono also reports nothing for a few hundred KB of new strings, so the loop runs 20,000 samples.
        /// </remarks>
        private static long HeapGrowth(AudioDiagnosticsOverlay overlay, int samples)
        {
            string[] kept = new string[samples];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetTotalMemory(forceFullCollection: true);
            for (int index = 0; index < samples; index++)
            {
                overlay.Sample();
                kept[index] = overlay.Text;
            }

            long growth = GC.GetTotalMemory(forceFullCollection: true) - before;
            GC.KeepAlive(kept);
            return growth;
        }

        private static AudioDiagnosticsOverlay Overlay(MadeObjects made, SfxPlayer sfx, MusicPlayer music)
        {
            AudioDiagnosticsOverlay overlay = made.Track(new GameObject("diagnostics"))
                .AddComponent<AudioDiagnosticsOverlay>();
            overlay.Sfx = sfx;
            overlay.Music = music;
            return overlay;
        }

        private static SfxPlayer SfxPlayer(MadeObjects made, SfxLibrary library)
        {
            made.Track(library);
            SfxPlayer player = made.Track(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private static SfxCue Cue(
            MadeObjects made,
            bool loop,
            SfxPriority priority = SfxPriority.Medium,
            float minimumIntervalSeconds = 0f
        )
        {
            return made.Track(
                SfxCue.Create(
                    new[] { new SfxVariant(Clip(made)) },
                    priority: priority,
                    minimumIntervalSeconds: minimumIntervalSeconds,
                    loop: loop
                )
            );
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
