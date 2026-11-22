namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class DspChorusTests
    {
        [Test]
        public void AChorusInterpolatesAndKeepsTheDrySignalImmediate()
        {
            DspChorus chorus = new() { DepthSeconds = 0f, Mix = 0.5f };
            Assert.That(chorus.TryPrepare(1_100, 32, 1), Is.True);
            float[] impulse = new float[32];
            impulse[0] = 1f;

            chorus.Process(impulse, 0, impulse.Length, 1);

            Assert.That(impulse[0], Is.EqualTo(0.5f));
            Assert.That(impulse[16], Is.EqualTo(0.25f));
            Assert.That(impulse[17], Is.EqualTo(0.25f));
            Assert.That(impulse[18], Is.Zero);
            Assert.That(chorus.LatencySamples, Is.Zero);
        }

        [Test]
        public void OppositeModulationWidensStereoWithoutCrossingChannels()
        {
            DspChorus chorus = new()
            {
                RateHertz = 5f,
                DepthSeconds = 0.008f,
                Mix = 1f,
            };
            Assert.That(chorus.TryPrepare(1_000, 64, 2), Is.True);
            float[] stereo = new float[102];
            for (int frame = 0; frame < 51; ++frame)
            {
                stereo[2 * frame] = frame;
                stereo[2 * frame + 1] = -frame;
            }

            chorus.Process(stereo, 0, stereo.Length, 2);

            Assert.That(stereo[100], Is.EqualTo((27f * 2f + 43f) / 3f).Within(0.0001f));
            Assert.That(stereo[101], Is.EqualTo(-(27f + 43f * 2f) / 3f).Within(0.0001f));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(8)]
        public void BlockSplitsAndResetPreserveTheSameWetSignal(int channels)
        {
            const int Frames = 451;
            DspChorus chorus = new()
            {
                RateHertz = 5f,
                DepthSeconds = DspChorus.MaximumDepthSeconds,
                Mix = 0.6f,
            };
            Assert.That(chorus.TryPrepare(1_000, Frames, channels), Is.True);
            float[] input = new float[Frames * channels];
            for (int frame = 0; frame < Frames; ++frame)
            {
                input[frame * channels] = (frame % 13 - 6) / 8f;
            }

            float[] expected = (float[])input.Clone();
            chorus.Process(expected, 0, expected.Length, channels);

            chorus.Reset();
            float[] silence = new float[30 * channels];
            chorus.Process(silence, 0, silence.Length, channels);
            Assert.That(silence, Is.All.Zero);

            chorus.Reset();
            chorus.Process(input, 0, 37 * channels, channels);
            chorus.Process(input, 37 * channels, 114 * channels, channels);
            chorus.Process(input, 151 * channels, 300 * channels, channels);
            Assert.That(input, Is.EqualTo(expected));
        }

        [Test]
        public void ZeroMixLeavesDryAudioUnchangedAfterPreparation()
        {
            DspChorus chorus = new();
            Assert.That(chorus.TryPrepare(1_000, 32, 2), Is.True);
            chorus.Mix = 0f;
            float[] before = { 1f, -1f };
            chorus.Process(before, 0, before.Length, 2);
            Assert.That(before, Is.EqualTo(new[] { 0.65f, -0.65f }));

            Assert.That(chorus.TryPrepare(1_000, 32, 2), Is.True);
            float[] after = { 1f, -1f, 0.125f, -0.125f };
            chorus.Process(after, 0, after.Length, 2);
            Assert.That(after, Is.EqualTo(new[] { 1f, -1f, 0.125f, -0.125f }));
        }

        [Test]
        public void ARefusedBlockLeavesAudioAndHistoryUntouched()
        {
            DspChorus chorus = new() { DepthSeconds = 0f, Mix = 1f };
            Assert.That(chorus.TryPrepare(1_000, 32, 2), Is.True);
            float[] refused = { 1f, 2f, 3f, 4f };
            chorus.Process(refused, 0, refused.Length, 1);
            chorus.Process(refused, 0, 3, 2);
            chorus.Process(refused, int.MaxValue, 2, 2);
            Assert.That(refused, Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));

            float[] impulse = new float[32];
            impulse[0] = 0.75f;
            chorus.Process(impulse, 0, impulse.Length, 2);
            Assert.That(impulse[28], Is.Zero);
            Assert.That(impulse[29], Is.Zero);
            Assert.That(impulse[30], Is.EqualTo(0.75f));
            Assert.That(impulse[31], Is.Zero);
        }
    }
}
