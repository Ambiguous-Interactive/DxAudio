namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class StereoPeakTapTests
    {
        [Test]
        public void AReadKeepsEachChannelsLoudestSampleAcrossBlocks()
        {
            StereoPeakTap tap = new();
            tap.Observe(new[] { 0.05f, -0.2f, -1.25f, 0.3f }, 2);
            tap.Observe(new[] { 0.3f, -0.8f }, 2);

            Assert.That(tap.TryRead(out float left, out float right), Is.True);
            Assert.That(left, Is.EqualTo(1.25f));
            Assert.That(right, Is.EqualTo(0.8f));
            Assert.That(tap.TryRead(out _, out _), Is.False);

            tap.Observe(new[] { 0f, 0f }, 2);
            Assert.That(tap.TryRead(out left, out right), Is.True);
            Assert.That(left, Is.Zero);
            Assert.That(right, Is.Zero);
        }

        [TestCase(1, 0.5f, 0.5f)]
        [TestCase(4, 0.5f, 0.25f)]
        public void ReadsMonoOrTheFrontTwoChannels(int channels, float expectedLeft, float expectedRight)
        {
            StereoPeakTap tap = new();
            float[] block = channels == 1 ? new[] { 0.1f, -0.5f } : new[] { 0.5f, 0f, 10f, 12f, 0.1f, -0.25f, 0f, 0f };
            tap.Observe(block, channels);

            Assert.That(tap.TryRead(out float left, out float right), Is.True);
            Assert.That(left, Is.EqualTo(expectedLeft));
            Assert.That(right, Is.EqualTo(expectedRight));
        }

        [Test]
        public void IgnoresIncompleteFramesAndNonFiniteSamples()
        {
            StereoPeakTap tap = new();
            tap.Observe(new[] { 1f }, 2);
            tap.Observe(new[] { 1f }, 0);
            Assert.That(tap.TryRead(out _, out _), Is.False);

            tap.Observe(new[] { float.NaN, -0.25f, -0.5f, float.PositiveInfinity, 3f }, 2);
            Assert.That(tap.TryRead(out float left, out float right), Is.True);
            Assert.That(left, Is.EqualTo(0.5f));
            Assert.That(right, Is.EqualTo(0.25f));
        }
    }
}
