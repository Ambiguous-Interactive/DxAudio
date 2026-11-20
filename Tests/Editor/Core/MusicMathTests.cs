namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    public sealed class MusicMathTests
    {
        private const float Tolerance = 0.0001f;

        public static IEnumerable<MusicEase> Eases()
        {
            MusicEase[] values = (MusicEase[])Enum.GetValues(typeof(MusicEase));
            foreach (MusicEase value in values)
            {
                // The unset sentinel is not a fade curve; its own case asserts it renders as linear.
                if (value != default)
                {
                    yield return value;
                }
            }
        }

        [Test]
        public void AnUnsetEaseRendersAsLinear()
        {
            Assert.That(MusicEasing.Evaluate(default, 0.25f), Is.EqualTo(0.25f).Within(Tolerance));
            Assert.That(MusicEasing.Evaluate(default, 0.75f), Is.EqualTo(0.75f).Within(Tolerance));
        }

        [TestCaseSource(nameof(Eases))]
        public void EveryEaseRunsFromZeroToOneWithoutGoingBack(MusicEase ease)
        {
            Assert.That(MusicEasing.Evaluate(ease, 0f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(MusicEasing.Evaluate(ease, 1f), Is.EqualTo(1f).Within(Tolerance));
            Assert.That(MusicEasing.Evaluate(ease, -3f), Is.EqualTo(0f).Within(Tolerance), "clamped below");
            Assert.That(MusicEasing.Evaluate(ease, 4f), Is.EqualTo(1f).Within(Tolerance), "clamped above");
            float previous = 0f;
            for (int step = 1; step <= 100; ++step)
            {
                float value = MusicEasing.Evaluate(ease, step / 100f);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous - Tolerance), $"{ease} went back at step {step}");
                previous = value;
            }
        }

        [Test]
        public void ARampAdvancesRetargetsAndFinishes()
        {
            MusicRamp ramp = new(0f, 1f, 2f, MusicEase.Linear);
            Assert.That(ramp.Advance(1f), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(ramp.IsFinished, Is.False);

            MusicRamp back = ramp.Retarget(0f, 1f, MusicEase.Linear);
            Assert.That(back.Value, Is.EqualTo(0.5f).Within(Tolerance), "a retarget starts where the ramp is");
            Assert.That(back.Advance(5f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(back.IsFinished, Is.True);

            MusicRamp instant = new(0.2f, 0.8f, 0f, MusicEase.InOutSine);
            Assert.That(instant.Value, Is.EqualTo(0.8f).Within(Tolerance), "a zero-second ramp is already there");
            Assert.That(MusicRamp.At(0.3f).Advance(1f), Is.EqualTo(0.3f).Within(Tolerance));
            Assert.That(new MusicRamp(0f, 1f, 1f, MusicEase.Linear).Advance(-1f), Is.Zero, "time never runs backwards");
        }

        [Test]
        public void AttenuationClampsComposesAndInterpolates()
        {
            MusicAttenuation wild = new(3f, 50_000f, 90_000f);
            Assert.That(wild.volumeScale, Is.EqualTo(1f));
            Assert.That(wild.lowPassCutoffHz, Is.EqualTo(MusicAttenuation.OpenLowPassCutoffHz));
            Assert.That(wild.highPassCutoffHz, Is.LessThanOrEqualTo(wild.lowPassCutoffHz), "crossed filters would cut everything");

            MusicAttenuation crossed = new(0.5f, 500f, 2_000f);
            Assert.That(crossed.highPassCutoffHz, Is.EqualTo(500f));

            MusicAttenuation a = new(0.5f, 1_000f, 100f);
            MusicAttenuation b = new(0.8f, 4_000f, 400f);
            Assert.That(MusicAttenuation.MostRestrictive(a, b), Is.EqualTo(new MusicAttenuation(0.5f, 1_000f, 400f)));

            Assert.That(MusicAttenuation.Lerp(a, b, 0f), Is.EqualTo(a));
            Assert.That(MusicAttenuation.Lerp(a, b, 1f), Is.EqualTo(b));
            MusicAttenuation middle = MusicAttenuation.Lerp(a, b, 0.5f);
            Assert.That(middle.lowPassCutoffHz, Is.EqualTo(2_000f).Within(1f), "cutoffs move in log frequency");
            Assert.That(middle.volumeScale, Is.EqualTo(0.65f).Within(Tolerance));
        }

        [TestCase(22_000f, 10f, 10f, TestName = "An ordered band is kept")]
        [TestCase(900f, 4_000f, 900f, TestName = "A crossing high-pass yields to the low-pass")]
        [TestCase(500f, 500f, 500f, TestName = "An equal band is kept")]
        [TestCase(1_000f, -50f, 10f, TestName = "A high-pass below range is lifted to open")]
        [TestCase(80_000f, 90_000f, 22_000f, TestName = "Two overshooting cutoffs are held at the ceiling")]
        public void TheHighPassStaysInsideTheBand(float lowPassCutoffHz, float highPassCutoffHz, float expected)
        {
            MusicAttenuation attenuation = new(1f, lowPassCutoffHz, highPassCutoffHz);

            Assert.That(attenuation.highPassCutoffHz, Is.EqualTo(expected));
        }

        [Test]
        public void AttenuationEqualityIsQuantizedAndAgreesWithItsHash()
        {
            MusicAttenuation a = new(0.5f, 1_000f, 100f);
            MusicAttenuation nearlyA = new(0.50001f, 1_000.1f, 100.1f);
            MusicAttenuation notA = new(0.51f, 1_000f, 100f);

            Assert.That(nearlyA, Is.EqualTo(a));
            Assert.That(nearlyA.GetHashCode(), Is.EqualTo(a.GetHashCode()));
            Assert.That(notA, Is.Not.EqualTo(a));
            Assert.That(MusicAttenuation.Unattenuated.IsUnattenuated, Is.True);
        }

        [TestCase(true, true, false, MusicHandOffKind.KeepSounding)]
        [TestCase(true, true, true, MusicHandOffKind.KeepSounding)]
        [TestCase(false, true, true, MusicHandOffKind.ReclaimOutgoing)]
        [TestCase(true, false, true, MusicHandOffKind.ReclaimOutgoing)]
        [TestCase(false, true, false, MusicHandOffKind.Crossfade)]
        [TestCase(false, false, false, MusicHandOffKind.StartFromSilence)]
        [TestCase(true, false, false, MusicHandOffKind.StartFromSilence)]
        public void HandOffFollowsItsTable(bool same, bool audible, bool fadingOut, MusicHandOffKind expected)
        {
            Assert.That(MusicHandOff.Decide(same, audible, fadingOut), Is.EqualTo(expected));
        }

        [Test]
        public void AStopScheduleFinishesOnceAtItsDeadline()
        {
            MusicStopSchedule schedule = new();
            Assert.That(schedule.TryFinish(100f), Is.False, "nothing is scheduled");
            schedule.Arm(now: 1f, fadeSeconds: 2f);
            Assert.That(schedule.TryFinish(2.9f), Is.False);
            Assert.That(schedule.TryFinish(3f), Is.True);
            Assert.That(schedule.TryFinish(4f), Is.False, "a passed deadline is spent");
            schedule.Arm(now: 0f, fadeSeconds: -5f);
            Assert.That(schedule.TryFinish(0f), Is.True, "a negative fade is an immediate stop");
        }

        [Test]
        public void ACancelledStopNeverFinishes()
        {
            MusicStopSchedule schedule = new();
            schedule.Arm(now: 0f, fadeSeconds: 1f);

            schedule.Cancel();

            Assert.That(schedule.IsScheduled, Is.False);
            Assert.That(schedule.TryFinish(5f), Is.False);
        }

        [TestCase(-21f, -6f, -21f, 0f, 1f, TestName = "A clip already on target keeps unity gain")]
        [TestCase(-15f, -6f, -21f, 0f, 0.5012f, TestName = "A loud clip comes down to target")]
        [TestCase(-30f, -12f, -21f, 0f, 1f, TestName = "A quiet clip is never boosted")]
        [TestCase(-15f, 4f, -21f, 0f, 0.4467f, TestName = "The peak ceiling wins when it is lower")]
        [TestCase(-21f, -6f, -21f, 6f, 0.5012f, TestName = "The bus gain is taken off")]
        public void LoudnessGainMeetsTheTarget(float lufs, float peak, float target, float busDb, float expected)
        {
            Assert.That(AudioLoudness.GainFor(lufs, peak, target, busDb), Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void LoudnessReportsPeakLimitingAndHeardLevels()
        {
            Assert.That(AudioLoudness.IsPeakLimited(-15f, 4f, -21f, 0f), Is.True);
            Assert.That(AudioLoudness.IsPeakLimited(-15f, -6f, -21f, 0f), Is.False);
            Assert.That(AudioLoudness.TryGetHeardLufs(-15f, 0.5012f, 0f, out float heard), Is.True);
            Assert.That(heard, Is.EqualTo(-21f).Within(0.01f));
            Assert.That(AudioLoudness.TryGetHeardLufs(-15f, 0f, 0f, out _), Is.False, "zero gain is heard as nothing");
        }

        [TestCase(1f, 0f)]
        [TestCase(0.5f, -6.0206f)]
        [TestCase(0f, AudioMath.SilentDecibels)]
        [TestCase(-1f, AudioMath.SilentDecibels)]
        [TestCase(0.00001f, AudioMath.SilentDecibels)]
        public void DecibelsRoundTrip(float linear, float decibels)
        {
            Assert.That(AudioMath.LinearToDecibels(linear), Is.EqualTo(decibels).Within(0.001f));
            float back = AudioMath.DecibelsToLinear(decibels);
            float expectedBack = decibels <= AudioMath.SilentDecibels ? 0f : linear;
            Assert.That(back, Is.EqualTo(expectedBack).Within(0.0001f));
        }

        [TestCase(5, 3, 2)]
        [TestCase(-1, 3, 2)]
        [TestCase(-3, 3, 0)]
        [TestCase(0, 3, 0)]
        public void PositiveModNeverGoesNegative(int value, int divisor, int expected)
        {
            Assert.That(AudioMath.PositiveMod(value, divisor), Is.EqualTo(expected));
        }
    }
}
