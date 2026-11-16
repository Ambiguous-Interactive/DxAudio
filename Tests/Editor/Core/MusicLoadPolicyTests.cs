namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;

    public sealed class MusicLoadPolicyTests
    {
        private static IEnumerable<TestCaseData> AutoCases()
        {
            yield return new TestCaseData(0f, MusicClipLoadType.DecompressOnLoad).SetName("An empty clip decompresses");
            yield return new TestCaseData(20f, MusicClipLoadType.DecompressOnLoad).SetName("A short clip decompresses");
            yield return new TestCaseData(
                MusicLoadPolicy.StreamingThresholdSeconds,
                MusicClipLoadType.DecompressOnLoad
            ).SetName("The threshold itself decompresses");
            yield return new TestCaseData(
                MusicLoadPolicy.StreamingThresholdSeconds + 0.5f,
                MusicClipLoadType.Streaming
            ).SetName("Just past the threshold streams");
            yield return new TestCaseData(240f, MusicClipLoadType.Streaming).SetName("A long track streams");
            yield return new TestCaseData(-1f, MusicClipLoadType.DecompressOnLoad).SetName(
                "A negative length decompresses"
            );
            yield return new TestCaseData(float.NaN, MusicClipLoadType.DecompressOnLoad).SetName(
                "No length decompresses"
            );
        }

        [TestCaseSource(nameof(AutoCases))]
        public void AutoDecompressesUpToTheThresholdAndStreamsBeyondIt(float seconds, MusicClipLoadType expected)
        {
            Assert.That(MusicLoadPolicy.Resolve(seconds, MusicClipLoadType.Auto), Is.EqualTo(expected));
        }

        [TestCase(MusicClipLoadType.DecompressOnLoad)]
        [TestCase(MusicClipLoadType.CompressedInMemory)]
        [TestCase(MusicClipLoadType.Streaming)]
        public void AnExplicitChoiceWinsAtAnyLength(MusicClipLoadType requested)
        {
            Assert.That(
                MusicLoadPolicy.Resolve(240f, requested),
                Is.EqualTo(requested),
                "a long clip keeps the choice"
            );
            Assert.That(MusicLoadPolicy.Resolve(0f, requested), Is.EqualTo(requested), "a short clip keeps the choice");
        }
    }
}
