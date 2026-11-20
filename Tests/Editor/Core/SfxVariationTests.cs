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

        [TestCase(0f, 500f)]
        [TestCase(0.5f, 2_000f)]
        [TestCase(1f, 8_000f)]
        public void LowPassDrawsAreUniformAcrossOctaves(float draw, float expected)
        {
            SfxVariation variation = new()
            {
                UseLowPass = true,
                MinimumLowPassHertz = 500f,
                MaximumLowPassHertz = 8_000f,
            };
            Assert.That(variation.LowPassHertz(draw), Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void LowPassBypassesInvalidValuesAndUsesSafeOrderedBounds()
        {
            SfxVariation variation = default;
            Assert.That(variation.LowPassHertz(0.5f), Is.Zero);
            variation.UseLowPass = true;
            variation.MinimumLowPassHertz = 30_000f;
            variation.MaximumLowPassHertz = 50f;
            Assert.That(variation.HasInvalidBounds, Is.True);
            Assert.That(variation.LowPassHertz(0f), Is.EqualTo(500f));
            Assert.That(variation.LowPassHertz(1f), Is.EqualTo(20_000f));
            variation.MinimumLowPassHertz = float.NaN;
            Assert.That(variation.LowPassHertz(0.5f), Is.Zero);
        }

        [Test]
        public void LowPassResetsEachChannelShapeWhenAVoiceStartsAgain()
        {
            SfxLowPass stage = new(48_000, 64, 2);
            stage.Begin(2_000f);
            float[] mono = { 1f, 0f, 0f, 0f };
            float[] stereo = { 0f, 1f, 0f, 0f };
            stage.Process(mono, 1);
            stage.Process(stereo, 2);
            Assert.That(mono[0], Is.InRange(0.01f, 0.02f));
            Assert.That(stereo[1], Is.EqualTo(mono[0]));

            stage.Begin(2_000f);
            float[] silence = new float[4];
            stage.Process(silence, 1);
            Assert.That(silence, Is.All.Zero);
            stage.Process(silence, 2);
            Assert.That(silence, Is.All.Zero);

            stage.Begin(0f);
            float[] dry = { 1f, 0f };
            stage.Process(dry, 1);
            Assert.That(dry, Is.EqualTo(new[] { 1f, 0f }));
        }
    }
}
