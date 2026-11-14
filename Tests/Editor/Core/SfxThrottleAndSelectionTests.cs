namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class SfxThrottleAndSelectionTests
    {
        [TestCase(0f, 0.04f, false, TestName = "Inside the window is refused")]
        [TestCase(0f, 0.05f, true, TestName = "The window's end is allowed")]
        [TestCase(0f, 1f, true, TestName = "Past the window is allowed")]
        public void ThrottleRefusesOnlyInsideTheWindow(float first, float second, bool expected)
        {
            SfxThrottle throttle = new();
            Assert.That(throttle.TryBegin("step", first, 0.05f), Is.True);

            Assert.That(throttle.TryBegin("step", second, 0.05f), Is.EqualTo(expected));
        }

        [Test]
        public void ThrottleKeepsKeysApartAndZeroIntervalsOpen()
        {
            SfxThrottle throttle = new();
            Assert.That(throttle.TryBegin("step", 0f, 1f), Is.True);
            Assert.That(throttle.TryBegin("pickup", 0f, 1f), Is.True, "another key has its own window");
            Assert.That(throttle.TryBegin("tick", 0f, 0f), Is.True);
            Assert.That(throttle.TryBegin("tick", 0f, 0f), Is.True, "a zero interval lets every request through");
            Assert.That(throttle.TryBegin(null, 0f, 0f), Is.False, "a null key names nothing");
        }

        [Test]
        public void IsReadyDoesNotSpendTheWindow()
        {
            SfxThrottle throttle = new();
            Assert.That(throttle.IsReady("step", 0f, 1f), Is.True);
            Assert.That(throttle.IsReady("step", 0f, 1f), Is.True);
            Assert.That(throttle.TryBegin("step", 0f, 1f), Is.True);
            Assert.That(throttle.IsReady("step", 0.5f, 1f), Is.False);
            throttle.Forget("step");
            Assert.That(throttle.IsReady("step", 0.5f, 1f), Is.True, "a forgotten key starts over");
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        public void SelectionNeverRepeatsAndReachesEveryVariant(int variantCount)
        {
            SfxSelection selection = new();
            bool[] seen = new bool[variantCount];
            int previous = -1;
            const int Draws = 200;
            for (int draw = 0; draw < Draws; draw++)
            {
                float unitRandom = (draw * 0.37f) % 1f;
                Assert.That(selection.TryNext("cue", variantCount, unitRandom, out int index), Is.True);
                Assert.That(index, Is.InRange(0, variantCount - 1));
                Assert.That(index, Is.Not.EqualTo(previous), $"draw {draw} repeated variant {index}");
                seen[index] = true;
                previous = index;
            }

            Assert.That(seen, Is.All.True, "every variant is reachable");
        }

        [TestCase(-5f)]
        [TestCase(0f)]
        [TestCase(0.99999f)]
        [TestCase(7f)]
        public void SelectionClampsOutOfRangeDraws(float unitRandom)
        {
            SfxSelection selection = new();
            Assert.That(selection.TryNext("cue", 4, unitRandom, out int first), Is.True);
            Assert.That(selection.TryNext("cue", 4, unitRandom, out int second), Is.True);

            Assert.That(first, Is.InRange(0, 3));
            Assert.That(second, Is.InRange(0, 3).And.Not.EqualTo(first));
        }

        [Test]
        public void SelectionHandlesDegenerateCounts()
        {
            SfxSelection selection = new();
            Assert.That(selection.TryNext("cue", 0, 0.5f, out _), Is.False, "nothing to choose");
            Assert.That(selection.TryNext(null, 3, 0.5f, out _), Is.False, "a null key names nothing");
            Assert.That(selection.TryNext("one", 1, 0.9f, out int only), Is.True);
            Assert.That(only, Is.Zero);
            Assert.That(selection.TryNext("one", 1, 0.9f, out only), Is.True, "a lone variant may repeat");
            Assert.That(only, Is.Zero);
        }

        [Test]
        public void SelectionMemoryIsPerKeyAndSurvivesAShrink()
        {
            SfxSelection selection = new();
            Assert.That(selection.TryNext("step", 4, 0.99f, out int step), Is.True);
            Assert.That(step, Is.EqualTo(3));
            Assert.That(selection.TryNext("pickup", 4, 0.99f, out int pickup), Is.True);
            Assert.That(pickup, Is.EqualTo(3), "another key does not inherit the step's history");
            Assert.That(
                selection.TryNext("step", 2, 0.99f, out int shrunk),
                Is.True,
                "a last pick past the new count is ignored"
            );
            Assert.That(shrunk, Is.InRange(0, 1));
        }

        [Test]
        public void FollowingPlaysTheLeadersTake()
        {
            SfxSelection selection = new();
            Assert.That(selection.TryFollow("answer", "question", 3, out _), Is.False, "the leader has played nothing");
            Assert.That(selection.TryNext("question", 3, 0.99f, out int asked), Is.True);

            Assert.That(selection.TryFollow("answer", "question", 3, out int answered), Is.True);
            Assert.That(answered, Is.EqualTo(asked));
            Assert.That(
                selection.TryFollow("answer", "question", asked, out _),
                Is.False,
                "the follower lacks that take"
            );
            Assert.That(selection.TryFollow("question", "question", 3, out _), Is.False, "a cue cannot follow itself");
        }
    }
}
