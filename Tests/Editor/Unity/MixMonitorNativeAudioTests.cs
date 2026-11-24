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
            music.Play(track);

            try
            {
                yield return RenderAndSample(window, poll, meter, peaks, allowUnavailable: true);
                float dryLow = peaks[0];
                float dryHigh = peaks[1];
                Assert.That(dryLow, Is.GreaterThan(0.05f), "Expected the monitor to sample the left tone.");
                Assert.That(dryHigh, Is.GreaterThan(0.025f), "Expected the monitor to sample the right tone.");
                Assert.That(meter.Left.Level, Is.GreaterThan(meter.Right.Level * 1.5f), "Expected distinct left and right peaks.");

                MusicDuckLease lease = music.Duck(gain);
                yield return RenderAndSample(window, poll, meter, peaks);
                Assert.That(peaks[0] / dryLow, Is.EqualTo(0.5f).Within(0.1f), "Gain duck at the low tone.");
                Assert.That(peaks[1] / dryHigh, Is.EqualTo(0.5f).Within(0.1f), "Gain duck at the high tone.");
                music.Release(lease);

                lease = music.Duck(filter);
                yield return RenderAndSample(window, poll, meter, peaks);
                Assert.That(peaks[0] / dryLow, Is.GreaterThan(0.6f), "Low tone after low-pass filter.");
                Assert.That(peaks[1] / dryHigh, Is.LessThan(0.2f), "High tone after low-pass filter.");
                Assert.That(window.Timeline.Count, Is.EqualTo(frozenTimelineCount));
                music.Release(lease);
            }
            finally
            {
                music.Stop();
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

        private static IEnumerator RenderAndSample(DxAudioMixMonitorWindow window, MethodInfo poll, MixMonitorPlayerMeter meter, float[] peaks, bool allowUnavailable = false)
        {
            peaks[0] = 0f;
            peaks[1] = 0f;
            bool renderedSignal = false;
            for (int frame = 0; frame < 45; ++frame)
            {
                yield return null;
                int count = AudioRenderer.GetSampleCountForCaptureFrame();
                if (count <= 0)
                {
                    continue;
                }

                using NativeArray<float> output = new(count, Allocator.Temp);
                Assert.That(AudioRenderer.Render(output), Is.True);
                poll.Invoke(window, new object[] { Time.realtimeSinceStartupAsDouble });
                if (frame < 30)
                {
                    continue;
                }

                foreach (float sample in output)
                {
                    renderedSignal |= 0.01f < Math.Abs(sample);
                }
                peaks[0] = Math.Max(peaks[0], meter.Spectrum[LowBin]);
                peaks[1] = Math.Max(peaks[1], meter.Spectrum[HighBin]);
            }

            if (allowUnavailable && !renderedSignal)
            {
                Assert.Ignore("Unity entered Play Mode but offline capture produced no audible samples.");
            }
            Assert.That(renderedSignal, Is.True, "Offline render must contain the fixture signal.");
        }

        [UnityTest]
        public IEnumerator MusicSpectrumRespondsToGainAndFilterDucks()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("This probe requires a disposable batchmode project.");
            }

            yield return new EnterPlayMode();

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
