namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;
    using WallstopStudios.DxAudio.Editor;

    public sealed class MixMonitorDropCaptureTests
    {
        [Test]
        public void ThrottledDropsStartSilenced()
        {
            MixMonitorDropCapture capture = new();
            capture.Sample(new SfxDropCounts(1, 2, 3, 4, 5));

            Assert.That(capture.IsSilenced(SfxDropReason.Throttled), Is.True);
            Assert.That(capture.SilencedCount, Is.EqualTo(2));
            Assert.That(capture.VisibleCount, Is.EqualTo(13));
        }

        [Test]
        public void SilencingOneReasonKeepsOtherReasonsWithTheSameRuleVisible()
        {
            MixMonitorDropCapture capture = new();
            capture.Sample(new SfxDropCounts(1, 2, 3, 4, 5));

            capture.SetSilenced(SfxDropReason.NoVoice, true);
            Assert.That(capture.IsSilenced(SfxDropReason.LoopLimit), Is.False);
            Assert.That(capture.SilencedCount, Is.EqualTo(5));

            capture.SetSilenced(SfxDropReason.Throttled, false);
            Assert.That(capture.SilencedCount, Is.EqualTo(3));
            Assert.That(capture.VisibleCount, Is.EqualTo(12));
        }

        [Test]
        public void PauseHoldsTheSampleUntilCaptureResumes()
        {
            MixMonitorDropCapture capture = new();
            capture.Sample(new SfxDropCounts(1, 0, 0, 0, 0));

            capture.Paused = true;
            capture.Sample(new SfxDropCounts(2, 0, 0, 0, 0));
            Assert.That(capture.Counts.notPlayable, Is.EqualTo(1));

            capture.Paused = false;
            capture.Sample(new SfxDropCounts(3, 0, 0, 0, 0));
            Assert.That(capture.Counts.notPlayable, Is.EqualTo(3));
        }

        [Test]
        public void ResetClearsTheSampleAndResumesWithoutChangingSilences()
        {
            MixMonitorDropCapture capture = new();
            capture.Sample(new SfxDropCounts(1, 2, 3, 4, 5));
            capture.SetSilenced(SfxDropReason.NoVoice, true);
            capture.Paused = true;

            capture.Reset();

            Assert.That(capture.Counts.Total, Is.Zero);
            Assert.That(capture.Paused, Is.False);
            Assert.That(capture.IsSilenced(SfxDropReason.NoVoice), Is.True);
        }
    }
}
