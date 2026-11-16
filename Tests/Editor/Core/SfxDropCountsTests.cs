namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class SfxDropCountsTests
    {
        private static readonly SfxDropCounts Counts = new(notPlayable: 1, throttled: 2, noVoice: 3, loopLimit: 4, wrongPlayMethod: 5);

        [TestCase(SfxDropReason.NotPlayable, 1)]
        [TestCase(SfxDropReason.Throttled, 2)]
        [TestCase(SfxDropReason.NoVoice, 3)]
        [TestCase(SfxDropReason.LoopLimit, 4)]
        [TestCase(SfxDropReason.WrongPlayMethod, 5)]
        [TestCase((SfxDropReason)99, 0)]
        public void EachReasonReadsItsOwnCount(SfxDropReason reason, int expected)
        {
            Assert.That(Counts[reason], Is.EqualTo(expected));
        }

        [Test]
        public void TheTotalAddsEveryReason()
        {
            Assert.That(Counts.Total, Is.EqualTo(15));
            Assert.That(SfxDropCounts.None.Total, Is.Zero);
        }
    }
}
