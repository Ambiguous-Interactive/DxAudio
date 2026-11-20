namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class SfxVariationTests
    {
        [TestCase(0f, 0.5f, 0.03154787f)]
        [TestCase(0.5f, 1f, 0.12559432f)]
        [TestCase(1f, 2f, 0.5f)]
        public void PlaybackDrawsAreUniformInCentsAndDecibels(float draw, float pitch, float volume)
        {
            SfxVariation variation = new(true, -1200f, 1200f, -24f, 0f);
            Assert.That(variation.Pitch(0.08f, draw), Is.EqualTo(pitch).Within(0.000001f));
            Assert.That(variation.Volume(0.5f, draw, out _), Is.EqualTo(volume).Within(0.000001f));
        }

        [Test]
        public void DefaultVariationPreservesLegacyPitchAndVolume()
        {
            SfxVariation variation = default;
            Assert.That(variation.Pitch(0.08f, 0f), Is.EqualTo(0.92f));
            Assert.That(variation.Pitch(0.08f, 1f), Is.EqualTo(1.08f));
            Assert.That(variation.Volume(0.5f, 0.7f, out _), Is.EqualTo(0.5f));
        }

        [Test]
        public void MalformedRangesResolveToSafeValues()
        {
            SfxVariation variation = new(true, 1600f, -1600f, float.NaN, 2f);
            Assert.That(variation.HasInvalidBounds, Is.True);
            Assert.That(variation.Pitch(0f, 0f), Is.EqualTo(0.5f));
            Assert.That(variation.Pitch(0f, 1f), Is.EqualTo(2f));
            Assert.That(variation.Volume(1f, 0.5f, out _), Is.EqualTo(1f));
        }
    }
}
