namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Reflection;
    using NUnit.Framework;
    using Unity.Collections;
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
