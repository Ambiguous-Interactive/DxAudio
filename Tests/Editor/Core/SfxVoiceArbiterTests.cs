namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;

    public sealed class SfxVoiceArbiterTests
    {
        private static readonly SfxVoiceState Free = SfxVoiceState.Idle;

        public static IEnumerable<TestCaseData> Cases()
        {
            yield return new TestCaseData(new SfxVoiceState[0], SfxPriority.High, 0, -1, false).SetName("An empty pool drops the cue");
            yield return new TestCaseData(new[] { Busy(SfxPriority.Low, 0f), Free, Free }, SfxPriority.Low, 0, 1, false).SetName("An idle voice wins over a lower busy one");
            yield return new TestCaseData(new[] { Free, Free, Free }, SfxPriority.Medium, 2, 2, false).SetName("The search starts at the round robin");
            yield return new TestCaseData(new[] { Free, Free, Free }, SfxPriority.Medium, 5, 2, false).SetName("The round robin wraps");
            yield return new TestCaseData(new[] { Free, Free, Free }, SfxPriority.Medium, -1, 2, false).SetName("A negative round robin wraps");
            yield return new TestCaseData(new[] { Busy(SfxPriority.Medium, 0f), Busy(SfxPriority.Medium, 1f) }, SfxPriority.Medium, 0, -1, false).SetName(
                "An equal is never stolen from"
            );
            yield return new TestCaseData(new[] { Busy(SfxPriority.Medium, 0f), Busy(SfxPriority.Low, 5f) }, SfxPriority.High, 0, 1, true).SetName(
                "The least important goes first"
            );
            yield return new TestCaseData(new[] { Busy(SfxPriority.Low, 3f), Busy(SfxPriority.Low, 1f), Busy(SfxPriority.Low, 2f) }, SfxPriority.Medium, 0, 1, true).SetName(
                "Among equals the oldest goes first"
            );
            yield return new TestCaseData(new[] { Held(SfxPriority.Low), Busy(SfxPriority.High, 0f) }, SfxPriority.High, 0, -1, false).SetName("A held loop is never stolen");
            yield return new TestCaseData(new[] { Held(SfxPriority.Low), Free }, SfxPriority.Low, 0, 1, false).SetName("A held voice is never free");
            yield return new TestCaseData(new[] { Paused(SfxPriority.High) }, SfxPriority.Low, 0, -1, false).SetName("A lower cue never takes a paused voice");
            yield return new TestCaseData(new[] { Paused(SfxPriority.Medium) }, SfxPriority.Medium, 0, 0, true).SetName("An equal cue may reuse a paused voice");
            yield return new TestCaseData(new[] { Paused(SfxPriority.Low) }, SfxPriority.High, 0, 0, true).SetName("A higher cue may take a paused voice");
            yield return new TestCaseData(new[] { default(SfxVoiceState), Busy(SfxPriority.Low, 0f) }, SfxPriority.High, 0, 1, true).SetName(
                "An undescribed voice is never chosen"
            );
        }

        [TestCaseSource(nameof(Cases))]
        public void ChoosesTheExpectedVoice(SfxVoiceState[] voices, SfxPriority incoming, int roundRobin, int expectedIndex, bool expectedInterrupts)
        {
            bool chosen = SfxVoiceArbiter.TryChoose(voices, incoming, roundRobin, out int index, out bool interrupts);

            Assert.That(chosen, Is.EqualTo(0 <= expectedIndex), "whether a voice was chosen");
            if (chosen)
            {
                Assert.That(index, Is.EqualTo(expectedIndex));
                Assert.That(interrupts, Is.EqualTo(expectedInterrupts));
            }
        }

        private static SfxVoiceState Busy(SfxPriority priority, float startedAt)
        {
            return new SfxVoiceState(isUsable: true, isPlaying: true, isHeld: false, priority, startedAt);
        }

        private static SfxVoiceState Held(SfxPriority priority)
        {
            return new SfxVoiceState(isUsable: true, isPlaying: true, isHeld: true, priority, startedAtUnscaledTime: 0f);
        }

        private static SfxVoiceState Paused(SfxPriority priority)
        {
            return new SfxVoiceState(isUsable: true, isPlaying: false, isHeld: false, priority, startedAtUnscaledTime: 0f, isPaused: true);
        }
    }
}
