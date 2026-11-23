namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;
    using WallstopStudios.DxAudio.Editor;

    public sealed class MixMonitorTimelineTests
    {
        [TestCase(false, false, false, 0.8f, 0.9f, 0.9f, TestName = "Waveform follows the observed playhead")]
        [TestCase(true, false, false, 0.98f, 0.03f, 1.03f, TestName = "Loop waveform wraps past the end")]
        [TestCase(false, false, false, 0.98f, 0.03f, 0.98f, TestName = "One-shot waveform does not wrap on a seek")]
        [TestCase(true, true, false, 0.8f, 0.9f, 0.8f, TestName = "Paused waveform stays at the saved playhead")]
        [TestCase(true, false, true, 0.8f, 0.9f, 0.8f, TestName = "Waveform does not interpolate across a capture interruption")]
        public void WaveformSpanFollowsOnlyContinuousPlayback(bool held, bool paused, bool interrupted, float first, float second, float expectedEnd)
        {
            MixMonitorTimeline<int, string> timeline = new(2);
            timeline.Reset(1);
            SfxVoiceState state = new(true, !paused, held, SfxPriority.Medium, 1f, isPaused: paused);
            timeline.Sample(1d, SfxDropCounts.None);
            timeline.SetVoice(0, state, clip: 17, clipSeconds: first);
            if (interrupted)
            {
                timeline.Interrupt();
            }

            timeline.Sample(1.05d, SfxDropCounts.None);
            timeline.SetVoice(0, state, clip: 17, clipSeconds: second);

            Assert.That(timeline.VoiceClipEnd(0, 0, 1f, 0.1d), Is.EqualTo(expectedEnd).Within(0.0001f));
            Assert.That(timeline.VoiceClipEnd(1, 0, 1f, 0.1d), Is.EqualTo(second));
        }

        [TestCase(17, 2, 1.05d, TestName = "A same-tick replay of the same clip uses a separate waveform span")]
        [TestCase(31, 1, 1.05d, TestName = "Voice reuse separates different clips")]
        [TestCase(17, 1, 2d, TestName = "A long editor tick separates waveform spans")]
        public void WaveformSpanEndsAtTheLastObservedPosition(int nextClip, int nextPlayback, double nextTime)
        {
            MixMonitorTimeline<int, string> timeline = new(2);
            timeline.Reset(1);
            timeline.Sample(1d, SfxDropCounts.None);
            SfxVoiceState state = new(true, true, false, SfxPriority.Medium, 1f);
            timeline.SetVoice(0, state, clip: 17, clipSeconds: 0.2f, playbackId: 1);
            timeline.Sample(nextTime, SfxDropCounts.None);
            timeline.SetVoice(0, state, clip: nextClip, clipSeconds: 0.4f, playbackId: nextPlayback);

            Assert.That(timeline.VoiceClipEnd(0, 0, 1f, 0.1d), Is.EqualTo(0.2f));
        }

        [Test]
        public void OverlappingDuckHoldsKeepTheirLanesWhenAnotherHolderReleases()
        {
            MixMonitorTimeline<int, string> timeline = new(3, duckCapacity: 2);
            MusicDuckReasonTable<string> reasons = new(_ => MusicDuckHolders.Many);
            IMusicDuckReason dialogue = reasons.Of("Dialogue");
            IMusicDuckReason pause = reasons.Of("Pause");
            timeline.Sample(1d, SfxDropCounts.None);
            timeline.SetDuckHolds(new[] { dialogue, dialogue }, new[] { 1L, 2L }, 2);

            timeline.Sample(1.0625d, SfxDropCounts.None);
            timeline.SetDuckHolds(new[] { dialogue, pause }, new[] { 2L, 3L }, 2);

            timeline.Interrupt();
            timeline.Sample(2d, SfxDropCounts.None);
            timeline.SetDuckHolds(new[] { dialogue }, new[] { 2L }, 1);

            Assert.That(timeline.DuckHold(0, 0), Is.EqualTo((dialogue, 1L)));
            Assert.That(timeline.DuckHold(0, 1), Is.EqualTo((dialogue, 2L)));
            Assert.That(timeline.DuckHold(1, 0), Is.EqualTo((pause, 3L)));
            Assert.That(timeline.DuckHold(1, 1), Is.EqualTo((dialogue, 2L)));
            Assert.That(timeline.DuckHold(2, 0).reason == null, Is.True);
            Assert.That(timeline.DuckHold(2, 1), Is.EqualTo((dialogue, 2L)));
            Assert.That(timeline.CanJoin(1, 0.125d), Is.True);
            Assert.That(timeline.CanJoin(2, 0.125d), Is.False);
        }

        [Test]
        public void DuckOverflowAndLanesExpireWithTheirSamples()
        {
            MixMonitorTimeline<int, string> timeline = new(2, duckCapacity: 1);
            MusicDuckReasonTable<string> reasons = new(_ => MusicDuckHolders.One);
            IMusicDuckReason pause = reasons.Of("Pause");
            timeline.Sample(1d, SfxDropCounts.None);
            timeline.SetDuckHolds(new[] { pause }, new[] { 1L }, 3);
            timeline.Sample(2d, SfxDropCounts.None);

            Assert.That(timeline.OmittedDuckHolds(0), Is.EqualTo(2));
            Assert.That(timeline.OmittedDuckHolds(1), Is.Zero);
            Assert.That(timeline.DuckHold(0, 0), Is.EqualTo((pause, 1L)));

            timeline.Sample(12d, SfxDropCounts.None);
            Assert.That(timeline.Count, Is.EqualTo(2));
            Assert.That(timeline.DuckHold(1, 0).reason == null, Is.True);
            Assert.That(timeline.OmittedDuckHolds(1), Is.Zero);

            timeline.SetDuckHolds(new[] { pause }, new[] { 2L }, 1);
            timeline.Reset(0);
            timeline.Sample(13d, SfxDropCounts.None);
            Assert.That(timeline.DuckHold(0, 0).reason == null, Is.True);
        }

        [Test]
        public void MusicDeckFadesAndDuckGainStayWithTheirFrames()
        {
            MixMonitorTimeline<int, string> timeline = new(2, deckCount: 2);
            timeline.Reset(0);
            timeline.Sample(1d, SfxDropCounts.None, duckGain: 1f);
            timeline.SetDeck(0, "Cave", 1f);

            timeline.Sample(2d, SfxDropCounts.None, duckGain: 0.4f);
            timeline.SetDeck(0, "Cave", 0.75f);
            timeline.SetDeck(1, "Boss", 0.25f);

            timeline.Interrupt();
            timeline.Sample(3d, SfxDropCounts.None);
            timeline.SetDeck(1, "Boss", 1f);

            Assert.That(timeline.Deck(0, 0), Is.EqualTo(("Cave", 0.75f)));
            Assert.That(timeline.Deck(0, 1), Is.EqualTo(("Boss", 0.25f)));
            Assert.That(timeline.DuckGain(0), Is.EqualTo(0.4f));
            Assert.That(timeline.Deck(1, 0), Is.EqualTo(((string)null, 0f)));
            Assert.That(timeline.Deck(1, 1), Is.EqualTo(("Boss", 1f)));
            Assert.That(timeline.DuckGain(1).HasValue, Is.False);
            Assert.That(timeline.CanJoin(1, 2d), Is.False);

            timeline.Reset(0);
            timeline.Sample(4d, SfxDropCounts.None);
            Assert.That(timeline.Deck(0, 0), Is.EqualTo(((string)null, 0f)));
            Assert.That(timeline.Deck(0, 1), Is.EqualTo(((string)null, 0f)));
            Assert.That(timeline.DuckGain(0).HasValue, Is.False);
        }

        [Test]
        public void CurvesJoinOnlyAcrossObservedIntervals()
        {
            MixMonitorTimeline<int, string> timeline = new(3);
            timeline.Sample(1d, SfxDropCounts.None);
            timeline.Sample(1.0625d, SfxDropCounts.None);
            timeline.Sample(2d, SfxDropCounts.None);

            Assert.That(timeline.CanJoin(1, 0.125d), Is.True);
            Assert.That(timeline.CanJoin(2, 0.125d), Is.False);
        }

        [Test]
        public void OldSamplesExpireAndVoiceRowsStayWithTheirFrames()
        {
            MixMonitorTimeline<int, string> timeline = new(2);
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
            MixMonitorTimeline<int, string> timeline = new(2);
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
            MixMonitorTimeline<int, string> timeline = new(2);
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
            MixMonitorTimeline<int, string> timeline = new(4);
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
            MixMonitorTimeline<int, string> timeline = new(4);
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
            MixMonitorTimeline<int, string> timeline = new(2);
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
