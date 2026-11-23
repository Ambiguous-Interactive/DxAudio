namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;
    using WallstopStudios.DxAudio.Editor;

    public sealed class MixMonitorTimelineTests
    {
        [Test]
        public void OldSamplesExpireAndVoiceRowsStayWithTheirFrames()
        {
            MixMonitorTimeline<int> timeline = new(2);
            timeline.Reset(2);
            timeline.Sample(1d, SfxDropCounts.None);
            timeline.Sample(2d, SfxDropCounts.None);
            timeline.SetVoice(1, new SfxVoiceState(true, false, true, SfxPriority.Medium, 0f));
            timeline.Sample(3d, SfxDropCounts.None);
            timeline.SetVoice(0, new SfxVoiceState(true, false, false, SfxPriority.High, 1f, isPaused: true));

            Assert.That(timeline.Count, Is.EqualTo(2));
            Assert.That(timeline.Time(0), Is.EqualTo(2d));
            Assert.That(timeline.Time(1), Is.EqualTo(3d));
            Assert.That(timeline.Voice(0, 1).isHeld, Is.True);
            Assert.That(timeline.Voice(1, 0).isPaused, Is.True);
            Assert.That(timeline.Voice(1, 1).isUsable, Is.False);
        }

        [Test]
        public void ClipDetailsStayWithTheSampleAfterAVoiceIsReused()
        {
            MixMonitorTimeline<int> timeline = new(2);
            timeline.Reset(1);
            timeline.Sample(1d, SfxDropCounts.None);
            timeline.Sample(2d, SfxDropCounts.None);
            SfxVoiceState playing = new(true, true, false, SfxPriority.Medium, 0f);
            timeline.SetVoice(0, playing, clip: 17, clipSeconds: 0.25f);
            timeline.Sample(3d, SfxDropCounts.None);
            timeline.SetVoice(0, playing, clip: 31, clipSeconds: 0.125f);

            Assert.That(timeline.Clip(0, 0), Is.EqualTo((17, 0.25f)));
            Assert.That(timeline.Clip(1, 0), Is.EqualTo((31, 0.125f)));

            timeline.Sample(4d, SfxDropCounts.None);
            Assert.That(timeline.Clip(1, 0), Is.EqualTo((0, 0f)));

            timeline.Reset(1);
            timeline.Sample(5d, SfxDropCounts.None);
            Assert.That(timeline.Clip(0, 0), Is.EqualTo((0, 0f)));
        }

        [Test]
        public void DropMarkersCountNewDropsAndKeepTheirCountsAfterWraparound()
        {
            MixMonitorTimeline<int> timeline = new(2);
            timeline.Reset(0);
            timeline.Sample(1d, new SfxDropCounts(10, 0, 6, 0, 0));
            Assert.That(timeline.Drops(0).Total, Is.Zero);

            timeline.Sample(2d, new SfxDropCounts(13, 0, 7, 0, 0));
            timeline.Sample(3d, new SfxDropCounts(13, 0, 7, 0, 0));

            Assert.That(timeline.Drops(0).notPlayable, Is.EqualTo(3));
            Assert.That(timeline.Drops(0).noVoice, Is.EqualTo(1));
            Assert.That(timeline.Drops(1).Total, Is.Zero);
        }

        [Test]
        public void ResumeLeavesAGapAndStartsANewDropBaseline()
        {
            MixMonitorTimeline<int> timeline = new(4);
            timeline.Reset(0);
            timeline.Sample(1d, SfxDropCounts.None);
            timeline.Interrupt();
            timeline.Sample(10d, new SfxDropCounts(3, 0, 0, 0, 0));
            timeline.Sample(11d, new SfxDropCounts(4, 0, 0, 0, 0));

            Assert.That(timeline.StartsSegment(1), Is.True);
            Assert.That(timeline.Drops(1).Total, Is.Zero);
            Assert.That(timeline.StartsSegment(2), Is.False);
            Assert.That(timeline.Drops(2).notPlayable, Is.EqualTo(1));
        }

        [Test]
        public void SamplesOlderThanTheVisibleTimeRangeExpire()
        {
            MixMonitorTimeline<int> timeline = new(4);
            timeline.Reset(0);
            timeline.Sample(1d, SfxDropCounts.None);
            timeline.Sample(2d, SfxDropCounts.None);
            timeline.Sample(12d, SfxDropCounts.None);

            Assert.That(timeline.Count, Is.EqualTo(2));
            Assert.That(timeline.Time(0), Is.EqualTo(2d));
        }

        [Test]
        public void ResetClearsTheHistoryAndBaselinesTheNextPlayer()
        {
            MixMonitorTimeline<int> timeline = new(2);
            timeline.Reset(2);
            timeline.Sample(1d, new SfxDropCounts(3, 0, 0, 0, 0));
            timeline.SetVoice(0, new SfxVoiceState(true, true, false, SfxPriority.High, 1f));

            timeline.Reset(1);
            Assert.That(timeline.Count, Is.Zero);
            Assert.That(timeline.VoiceCount, Is.EqualTo(1));
            timeline.Sample(2d, new SfxDropCounts(8, 0, 0, 0, 0));
            Assert.That(timeline.Voice(0, 0).isPlaying, Is.False);
            Assert.That(timeline.Drops(0).Total, Is.Zero);
        }
    }
}
