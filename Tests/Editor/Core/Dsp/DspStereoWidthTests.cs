namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class DspStereoWidthTests
    {
        [TestCase(0f, 0.25f, 0.25f, TestName = "Zero width folds stereo to its average")]
        [TestCase(0.5f, 0.5f, 0f, TestName = "Half width halves the difference")]
        [TestCase(1f, 0.75f, -0.25f, TestName = "Unity width bypasses the block")]
        [TestCase(2f, 1.25f, -0.75f, TestName = "Double width doubles the difference")]
        public void WidthScalesOnlyTheDifference(float width, float expectedLeft, float expectedRight)
        {
            DspStereoWidth effect = new() { Width = width };
            Assert.That(effect.TryPrepare(48_000, 4, 2), Is.True, "Stereo preparation succeeds.");
            float[] buffer = { 99f, 0.75f, -0.25f, -0.5f, -0.5f, -99f };

            effect.Process(buffer, 1, 4, 2);

            Assert.That(buffer, Is.EqualTo(new[] { 99f, expectedLeft, expectedRight, -0.5f, -0.5f, -99f }));
            Assert.That(effect.LatencySamples, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(6)]
        public void NonStereoLayoutsKeepEveryChannel(int channels)
        {
            DspStereoWidth effect = new() { Width = 0f };
            float[] samples = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f };
            float[] buffer = (float[])samples.Clone();

            Assert.That(effect.TryPrepare(48_000, 12, 2), Is.True);
            effect.Process(buffer, 0, buffer.Length, channels);
            Assert.That(effect.TryPrepare(48_000, 12, channels), Is.True);
            effect.Process(buffer, 0, buffer.Length, channels);
            effect.Process(buffer, 0, buffer.Length, 2);

            Assert.That(buffer, Is.EqualTo(samples));
        }

        [Test]
        public void UnpreparedAndInvalidBlocksLeaveSamplesAlone()
        {
            DspStereoWidth effect = new() { Width = 0f };
            float[] samples = { 0.25f, 0.75f, 0.5f, -0.5f };
            float[] buffer = (float[])samples.Clone();
            effect.Process(buffer, 0, buffer.Length, 2);
            Assert.That(effect.TryPrepare(48_000, 4, 2), Is.True);
            effect.Process(null, 0, 4, 2);
            effect.Process(buffer, -1, 2, 2);
            effect.Process(buffer, 0, 3, 2);
            effect.Process(buffer, 2, 4, 2);
            effect.Process(buffer, int.MaxValue, 2, 2);
            effect.Process(buffer, 0, 0, 2);

            Assert.That(buffer, Is.EqualTo(samples));
        }

        [Test]
        public void WidthChangesApplyAtTheNextPreparation()
        {
            DspStereoWidth effect = new();
            float[] buffer = { 1f, -1f };
            Assert.That(effect.TryPrepare(48_000, 1, 2), Is.True);

            effect.Width = 0f;
            effect.Reset();
            effect.Process(buffer, 0, buffer.Length, 2);
            Assert.That(buffer, Is.EqualTo(new[] { 1f, -1f }));

            Assert.That(effect.TryPrepare(48_000, 1, 2), Is.True);
            effect.Process(buffer, 0, buffer.Length, 2);
            Assert.That(buffer, Is.EqualTo(new[] { 0f, 0f }));
        }

        [TestCase(-1f, 0f)]
        [TestCase(3f, 2f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(float.PositiveInfinity, 1f)]
        [TestCase(float.NegativeInfinity, 1f)]
        public void WidthStaysInItsFiniteRange(float width, float expected)
        {
            DspStereoWidth effect = new() { Width = width };

            Assert.That(effect.Width, Is.EqualTo(expected));
        }
    }
}
