namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class MusicDuckStackTests
    {
        private static readonly MusicAttenuation Half = new(0.5f, 2_000f, MusicAttenuation.OpenHighPassCutoffHz);
        private static readonly MusicAttenuation Quiet = new(0.2f, MusicAttenuation.OpenLowPassCutoffHz, 300f);

        [Test]
        public void HoldsComposeToTheMostRestrictiveAndReleaseIndependently()
        {
            MusicDuckStack stack = new();
            Reason menu = new("menu", MusicDuckHolders.One);
            Reason dialogue = new("dialogue", MusicDuckHolders.Many);

            MusicDuckLease menuLease = stack.Take(menu, Half);
            MusicDuckLease dialogueLease = stack.Take(dialogue, Quiet, holder: "speaker");

            Assert.That(stack.Current, Is.EqualTo(new MusicAttenuation(0.2f, 2_000f, 300f)));
            Assert.That(stack.Release(dialogueLease), Is.True);
            Assert.That(stack.Current, Is.EqualTo(Half), "the menu stays in force");
            Assert.That(stack.Release(menuLease), Is.True);
            Assert.That(stack.Current, Is.EqualTo(MusicAttenuation.Unattenuated));
            Assert.That(stack.ActiveCount, Is.Zero);
        }

        [Test]
        public void AOneReasonIsOneHoldHoweverOftenItIsAsserted()
        {
            MusicDuckStack stack = new();
            Reason menu = new("menu", MusicDuckHolders.One);

            MusicDuckLease first = stack.Take(menu, Half);
            MusicDuckLease second = stack.Take(menu, Quiet);

            Assert.That(second, Is.EqualTo(first), "the same state asserted again is the same hold");
            Assert.That(stack.Current, Is.EqualTo(Quiet), "the repeat replaces the attenuation");
            Assert.That(stack.Release(first), Is.True);
            Assert.That(stack.IsActive(menu), Is.False, "one release ends it");
            Assert.That(stack.Release(second), Is.False, "the hold is already gone");
        }

        [Test]
        public void AManyReasonHoldsUntilTheLastHolderReleases()
        {
            MusicDuckStack stack = new();
            Reason dialogue = new("dialogue", MusicDuckHolders.Many);

            MusicDuckLease first = stack.Take(dialogue, Half, holder: "a");
            MusicDuckLease second = stack.Take(dialogue, Half, holder: "b");

            Assert.That(stack.HolderCount(dialogue), Is.EqualTo(2));
            Assert.That(stack.IsHeld(first), Is.True);
            Assert.That(stack.IsHeld(second), Is.True, "one holder's lease does not report the other's release");
            Assert.That(stack.Release(first), Is.False, "the target does not move while b still holds");
            Assert.That(stack.IsHeld(first), Is.False);
            Assert.That(stack.IsHeld(second), Is.True);
            Assert.That(stack.IsActive(dialogue), Is.True);
            Assert.That(stack.Release(dialogue, holder: "b"), Is.True);
            Assert.That(stack.IsActive(dialogue), Is.False);
        }

        [Test]
        public void StaleForeignAndDefaultLeasesReleaseNothing()
        {
            MusicDuckStack stack = new();
            MusicDuckStack other = new();
            Reason menu = new("menu", MusicDuckHolders.One);
            MusicDuckLease lease = stack.Take(menu, Half);
            MusicDuckLease foreign = other.Take(menu, Half);

            Assert.That(default(MusicDuckLease).IsValid, Is.False);
            Assert.That(stack.Release(default), Is.False);
            Assert.That(stack.Release(foreign), Is.False, "another stack's lease, though its id matches");
            Assert.That(stack.IsActive(menu), Is.True);
            Assert.That(stack.IsHeld(lease), Is.True);
            Assert.That(stack.IsHeld(foreign), Is.False, "another stack's lease is not a hold here");
            Assert.That(stack.IsHeld(default), Is.False);
            Assert.That(stack.Release(lease), Is.True);
            Assert.That(stack.IsHeld(lease), Is.False, "a released lease is no longer held");
            _ = stack.Take(menu, Quiet);
            Assert.That(stack.Release(lease), Is.False, "a released lease never releases a later hold");
            Assert.That(stack.Take(null, Half).IsValid, Is.False);
        }

        [Test]
        public void CopyHoldsReportsEveryLiveHoldAndWhatHoldsIt()
        {
            MusicDuckStack stack = new();
            Reason menu = new("menu", MusicDuckHolders.One);
            Reason dialogue = new("dialogue", MusicDuckHolders.Many);
            _ = stack.Take(menu, Half);
            _ = stack.Take(dialogue, Half, holder: "a");
            _ = stack.Take(dialogue, Half, holder: "b");

            IMusicDuckReason[] reasons = new IMusicDuckReason[8];
            object[] holders = new object[8];
            long[] ids = new long[8];
            int total = stack.CopyHolds(reasons, holders, ids);
            int menuCount = 0;
            int dialogueCount = 0;
            bool sawA = false;
            bool sawB = false;
            for (int index = 0; index < total; ++index)
            {
                if (ReferenceEquals(reasons[index], menu))
                {
                    ++menuCount;
                }
                else if (ReferenceEquals(reasons[index], dialogue))
                {
                    ++dialogueCount;
                    sawA = sawA || ReferenceEquals(holders[index], "a");
                    sawB = sawB || ReferenceEquals(holders[index], "b");
                }
            }

            Assert.That(total, Is.EqualTo(3), "one entry per holder");
            Assert.That(menuCount, Is.EqualTo(1));
            Assert.That(dialogueCount, Is.EqualTo(2));
            Assert.That(sawA && sawB, Is.True, "each Many hold names its own holder");
            Assert.That(ids[0], Is.Not.Zero);

            int shortTotal = stack.CopyHolds(new IMusicDuckReason[1], new object[1], new long[1]);
            Assert.That(shortTotal, Is.EqualTo(3), "a short array must not hide the holds it cannot fit");
            Assert.That(stack.CopyHolds(null, holders, ids), Is.Zero);
        }

        [Test]
        public void DescribeAndClear()
        {
            MusicDuckStack stack = new();
            Assert.That(stack.DescribeHolds(), Does.Contain("Nothing"));
            _ = stack.Take(new Reason("dialogue", MusicDuckHolders.Many), Half, holder: "speaker");

            Assert.That(stack.DescribeHolds(), Does.Contain("dialogue #1 held by speaker"));
            Assert.That(stack.Clear(), Is.True);
            Assert.That(stack.Current, Is.EqualTo(MusicAttenuation.Unattenuated));
        }

        private sealed class Reason : IMusicDuckReason
        {
            public MusicDuckHolders Holders { get; }
            private readonly string _name;

            public Reason(string name, MusicDuckHolders holders)
            {
                _name = name;
                Holders = holders;
            }

            public override string ToString()
            {
                return _name;
            }
        }
    }
}
