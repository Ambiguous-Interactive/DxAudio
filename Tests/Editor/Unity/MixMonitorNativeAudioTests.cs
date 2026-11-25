namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Reflection;
    using NUnit.Framework;
    using Unity.Collections;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.DxAudio.Editor;

    public sealed class MixMonitorNativeAudioTests
    {
        private const int LowBin = 8;
        private const int HighBin = 128;

        private static IEnumerator CheckDuckResponse(MadeObjects made)
        {
            made.Track(new GameObject("Meter listener")).AddComponent<AudioListener>();
            MusicPlayer music = made.Track(new GameObject("Meter music")).AddComponent<MusicPlayer>();
            MusicTrack track = made.Track(MusicTrack.Create(CreateStereoTone(made)));
            MusicDuck gain = made.Track(MusicDuck.Create(MusicDuckHolders.Many, 0.5f, applySeconds: 0f, releaseSeconds: 0f));
            MusicDuck filter = made.Track(MusicDuck.Create(MusicDuckHolders.Many, 1f, lowPassCutoffHz: 1000f, applySeconds: 0f, releaseSeconds: 0f));
            DxAudioMixMonitorWindow window = made.Track(ScriptableObject.CreateInstance<DxAudioMixMonitorWindow>());
            window.SelectPlayers(null, music);
            window.ToggleCapture();
            int frozenTimelineCount = window.Timeline.Count;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type windowType = typeof(DxAudioMixMonitorWindow);
            MethodInfo poll = windowType.GetMethod("SamplePlayerMeters", flags);
            MixMonitorPlayerMeter meter = (MixMonitorPlayerMeter)windowType.GetField("_musicMeter", flags).GetValue(window);
            float[] peaks = new float[2];
            AudioLayout layout = new();
            layout.Refresh();
            music.Play(track);

            try
            {
                yield return RenderAndSample(window, poll, meter, peaks, layout.Channels);
                float dryLow = peaks[0];
                float dryHigh = peaks[1];
                Assert.That(dryLow, Is.GreaterThan(0.05f), "Expected the monitor to sample the left tone.");
                Assert.That(dryHigh, Is.GreaterThan(0.025f), "Expected the monitor to sample the right tone.");
                Assert.That(meter.Left.Level, Is.GreaterThan(meter.Right.Level * 1.5f), "Expected distinct left and right peaks.");

                MusicDuckLease lease = music.Duck(gain);
                yield return RenderAndSample(window, poll, meter, peaks, layout.Channels);
                float gainLow = peaks[0] / dryLow;
                float gainHigh = peaks[1] / dryHigh;
                music.Release(lease);

                lease = music.Duck(filter);
                yield return RenderAndSample(window, poll, meter, peaks, layout.Channels);
                float filteredLow = peaks[0] / dryLow;
                float filteredHigh = peaks[1] / dryHigh;
                bool gainMatches = Math.Abs(gainLow - 0.5f) <= 0.1f && Math.Abs(gainHigh - 0.5f) <= 0.1f;
                bool filterMatches = 0.6f < filteredLow && filteredHigh < 0.2f;
                Assert.That(gainMatches && filterMatches, Is.True, $"Dry-relative ratios. Gain {gainLow}, {gainHigh}. Low-pass {filteredLow}, {filteredHigh}.");
                Assert.That(window.Timeline.Count, Is.EqualTo(frozenTimelineCount));
                music.Release(lease);
            }
            finally
            {
                music.Stop();
            }
        }

        private static IEnumerator CheckSfxPan(MadeObjects made)
        {
            SfxPlayer sfx = made.Track(new GameObject("Meter SFX")).AddComponent<SfxPlayer>();
            const int sampleCount = MixMonitorPlayerMeter.SampleCount;
            AudioClip clip = made.Track(AudioClip.Create("Mono meter tone", sampleCount, 1, AudioSettings.outputSampleRate, stream: false));
            float[] pcm = new float[sampleCount];
            for (int index = 0; index < sampleCount; ++index)
            {
                pcm[index] = 0.5f * (float)Math.Sin(2d * Math.PI * LowBin * index / sampleCount);
            }
            Assert.That(clip.SetData(pcm, 0), Is.True);

            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(clip) }, loop: true));
            sfx.Library = made.Track(SfxLibrary.Create(new[] { cue }, voiceCount: 1, loopingVoiceLimit: 1));
            DxAudioMixMonitorWindow window = made.Track(ScriptableObject.CreateInstance<DxAudioMixMonitorWindow>());
            window.SelectPlayers(sfx, null);
            AudioLayout layout = new();
            layout.Refresh();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type windowType = typeof(DxAudioMixMonitorWindow);
            MethodInfo poll = windowType.GetMethod("SamplePlayerMeters", flags);
            MixMonitorPlayerMeter meter = (MixMonitorPlayerMeter)windowType.GetField("_sfxMeter", flags).GetValue(window);
            Assert.That(sfx.TryPlayLooping(cue, out SfxLoopHandle loop), Is.True);
            AudioSource source = sfx.GetMonitorSource(0);
            float[] unusedSpectrum = new float[2];

            try
            {
                source.panStereo = -1f;
                yield return RenderAndSample(window, poll, meter, unusedSpectrum, layout.Channels);
                Assert.That(meter.Left.Level, Is.GreaterThan(0.1f), "Hard-left mono signal.");
                Assert.That(meter.Right.Level, Is.Zero, "Hard-left mono must not register on the right.");
                source.panStereo = 1f;
                yield return RenderAndSample(window, poll, meter, unusedSpectrum, layout.Channels);
                Assert.That(meter.Left.Level, Is.Zero, "Hard-right mono must not register on the left.");
                Assert.That(meter.Right.Level, Is.GreaterThan(0.1f), "Hard-right mono signal.");
            }
            finally
            {
                sfx.StopLoop(loop);
            }
        }

        private static AudioClip CreateStereoTone(MadeObjects made)
        {
            const int sampleCount = MixMonitorPlayerMeter.SampleCount;
            int rate = Math.Max(1, AudioSettings.outputSampleRate);
            AudioClip clip = made.Track(AudioClip.Create("Meter tones", sampleCount, 2, rate, stream: false));
            float[] pcm = new float[sampleCount * 2];
            for (int index = 0; index < sampleCount; ++index)
            {
                double phase = 2d * Math.PI * index / sampleCount;
                pcm[2 * index] = 0.5f * (float)Math.Sin(LowBin * phase);
                pcm[2 * index + 1] = 0.25f * (float)Math.Sin(HighBin * phase);
            }
            Assert.That(clip.SetData(pcm, 0), Is.True);
            return clip;
        }

        private static IEnumerator RenderAndSample(DxAudioMixMonitorWindow window, MethodInfo poll, MixMonitorPlayerMeter meter, float[] peaks, int channels)
        {
            peaks[0] = 0f;
            peaks[1] = 0f;
            bool renderedSignal = false;
            for (int frame = 0; frame < 45; ++frame)
            {
                yield return null;
                int count = AudioRenderer.GetSampleCountForCaptureFrame();
                using NativeArray<float> output = new(count * channels, Allocator.Temp);
                bool rendered = AudioRenderer.Render(output);
                if (0 < count)
                {
                    Assert.That(rendered, Is.True);
                }
                poll.Invoke(window, new object[] { Time.realtimeSinceStartupAsDouble });
                if (frame < 30)
                {
                    continue;
                }

                foreach (float sample in output)
                {
                    renderedSignal = renderedSignal || 0.01f < Math.Abs(sample);
                }
                if (0 < meter.Spectrum.Length)
                {
                    peaks[0] = Math.Max(peaks[0], meter.Spectrum[LowBin]);
                    peaks[1] = Math.Max(peaks[1], meter.Spectrum[HighBin]);
                }
            }

            Assert.That(renderedSignal, Is.True, "Offline render must contain the fixture signal.");
        }

        [UnityTest]
        public IEnumerator TransportLoopsCapturedFramesAndCopiesTheirDataInPlayMode()
        {
            yield return new EnterPlayMode();

            using MadeObjects made = new();
            made.Track(new GameObject("Transport listener")).AddComponent<AudioListener>();
            SfxPlayer sfx = made.Track(new GameObject("Transport SFX")).AddComponent<SfxPlayer>();
            AudioClip clip = made.Track(AudioClip.Create("Loop \"fixture\"", 1024, 1, 48000, stream: false));
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(clip) }, loop: true));
            sfx.Library = made.Track(SfxLibrary.Create(new[] { cue }, voiceCount: 1, loopingVoiceLimit: 1));
            DxAudioMixMonitorWindow window = made.Track(ScriptableObject.CreateInstance<DxAudioMixMonitorWindow>());
            window.SelectPlayers(sfx, null);
            double start = window.Timeline.Time(0);
            Assert.That(window.Follow, Is.True);
            Assert.That(window.Cursor, Is.Zero);
            Assert.That(window.Timeline.OccupiedVoiceCount(window.Cursor), Is.Zero);
            Assert.That(sfx.TryPlayLooping(cue, out SfxLoopHandle loop), Is.True);

            try
            {
                window.SamplePlayer(start + 1d);
                Assert.That(window.Cursor, Is.EqualTo(1));
                window.SetFollow(false);
                Assert.That(sfx.Play(cue), Is.False);
                window.SamplePlayer(start + 2d);
                Assert.That(window.Cursor, Is.EqualTo(1));
                Assert.That(window.Timeline.ElapsedSeconds(window.Cursor), Is.EqualTo(1d).Within(0.0001d));
                Assert.That(window.Timeline.OccupiedVoiceCount(window.Cursor), Is.EqualTo(1));

                window.ToggleCapture();
                window.SetHistoryLoop(true, start + 3d);
                window.AdvanceHistoryLoop(start + 4.01d);
                Assert.That(window.Cursor, Is.EqualTo(2));
                window.AdvanceHistoryLoop(start + 4.1d);
                Assert.That(window.Cursor, Is.Zero);
                window.SamplePlayer(start + 5d);
                Assert.That(window.Timeline.Count, Is.EqualTo(3));
                Assert.That(sfx.TryObserveVoiceState(0, out SfxVoiceState state, out _, out _, out _, out _), Is.True);
                Assert.That(state.isHeld, Is.True);

                window.Scrub(1);
                Assert.That(window.IsHistoryLooping, Is.False);
                string clipboard = EditorGUIUtility.systemCopyBuffer;
                try
                {
                    window.CopyCapture();
                    MixMonitorCaptureExport export = JsonUtility.FromJson<MixMonitorCaptureExport>(EditorGUIUtility.systemCopyBuffer);
                    Assert.That(export.version, Is.EqualTo(1));
                    Assert.That(export.cursor, Is.EqualTo(1));
                    Assert.That(export.voiceSlots, Is.EqualTo(1));
                    Assert.That(export.frames, Has.Length.EqualTo(3));
                    Assert.That(export.frames[0].startsSegment, Is.True);
                    Assert.That(export.frames[0].voices, Is.Empty);
                    Assert.That(export.frames[1].seconds, Is.EqualTo(1d).Within(0.0001d));
                    Assert.That(export.frames[1].voices[0].clip, Is.EqualTo(clip.name));
                    Assert.That(export.frames[1].voices[0].held, Is.True);
                    Assert.That(export.frames[2].drops[0].reason, Is.EqualTo("WrongPlayMethod"));
                    Assert.That(export.dropTotals[0].count, Is.EqualTo(1));
                    Assert.That(export.silencedReasons, Does.Contain("Throttled"));
                }
                finally
                {
                    EditorGUIUtility.systemCopyBuffer = clipboard;
                }

                window.SetHistoryLoop(true, start + 5d);
                window.ToggleCapture(start + 6d);
                Assert.That(window.IsHistoryLooping, Is.False);
                Assert.That(window.Timeline.StartsSegment(3), Is.True);
                window.SamplePlayer(start + 10.5d);
                Assert.That(window.Cursor, Is.Zero);
                Assert.That(window.Timeline.ElapsedSeconds(window.Cursor), Is.EqualTo(1d).Within(0.0001d));
                window.SamplePlayer(start + 11.5d);
                Assert.That(window.Cursor, Is.Zero);
                Assert.That(window.Timeline.ElapsedSeconds(window.Cursor), Is.EqualTo(2d).Within(0.0001d));
                window.SetFollow(true);
                Assert.That(window.Cursor, Is.EqualTo(window.Timeline.Count - 1));
            }
            finally
            {
                sfx.StopLoop(loop);
            }
        }

        [Test]
        public void CaptureJsonPreservesMusicFadesAndDuckHolds()
        {
            using MadeObjects made = new();
            MusicTrack track = made.Track(ScriptableObject.CreateInstance<MusicTrack>());
            track.name = "Cave";
            MusicDuckReasonTable<string> reasons = new(_ => MusicDuckHolders.Many);
            IMusicDuckReason reason = reasons.Of("Dialogue");
            MixMonitorTimeline<AudioClip, MusicTrack> timeline = new(2, deckCount: 2, duckCapacity: 1);
            timeline.Sample(10d, SfxDropCounts.None, duckGain: 0.4f);
            timeline.SetDeck(1, track, 0.75f);
            timeline.SetDuckHolds(new[] { reason }, new[] { 19L }, 2);
            timeline.Sample(11d, SfxDropCounts.None);

            string json = JsonUtility.ToJson(new MixMonitorCaptureExport(timeline, new MixMonitorDropCapture(), 1));
            MixMonitorCaptureExport export = JsonUtility.FromJson<MixMonitorCaptureExport>(json);
            Assert.That(export.frames[0].decks[0].slot, Is.EqualTo(2));
            Assert.That(export.frames[0].decks[0].track, Is.EqualTo("Cave"));
            Assert.That(export.frames[0].decks[0].fade, Is.EqualTo(0.75f));
            Assert.That(export.frames[0].hasDuckGain, Is.True);
            Assert.That(export.frames[0].duckGain, Is.EqualTo(0.4f));
            Assert.That(export.frames[0].duckHolds[0].id, Is.EqualTo(19L));
            Assert.That(export.frames[0].duckHolds[0].reason, Is.EqualTo("Dialogue"));
            Assert.That(export.frames[0].omittedDuckHolds, Is.EqualTo(1));
            Assert.That(export.frames[1].decks, Is.Empty);
            Assert.That(export.frames[1].duckHolds, Is.Empty);
            Assert.That(export.frames[1].hasDuckGain, Is.False);
        }

        [UnityTest]
        public IEnumerator MusicSpectrumRespondsToDucksAndSfxMeterFollowsPan()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("This probe requires a disposable batchmode project.");
            }

            yield return new EnterPlayMode();

            if (AudioSettings.speakerMode == AudioSpeakerMode.Mono)
            {
                Assert.Ignore("The stereo fixture requires left and right output channels.");
            }

            using MadeObjects made = new();
            int frameRate = Time.captureFramerate;
            Time.captureFramerate = 30;
            try
            {
                if (!AudioRenderer.Start())
                {
                    Assert.Ignore("Unity could not start an offline audio capture.");
                }

                try
                {
                    yield return CheckDuckResponse(made);
                    yield return CheckSfxPan(made);
                }
                finally
                {
                    AudioRenderer.Stop();
                }
            }
            finally
            {
                Time.captureFramerate = frameRate;
            }
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }
    }
}
