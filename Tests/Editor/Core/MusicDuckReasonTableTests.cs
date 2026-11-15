namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;

    public sealed class MusicDuckReasonTableTests
    {
        private static readonly MusicAttenuation Half = new(0.5f, 2_000f, MusicAttenuation.OpenHighPassCutoffHz);

        private enum Reason
        {
            Menu = 1,
            Dialogue = 2,
        }

        [Test]
        public void EachKeyIsOneReasonThatALaterLookupCanRelease()
        {
            MusicDuckReasonTable<Reason> table = new(key =>
                key == Reason.Dialogue ? MusicDuckHolders.Many : MusicDuckHolders.One
            );
            MusicDuckStack stack = new();

            _ = stack.Take(table.Of(Reason.Menu), Half);

            Assert.That(table.Of(Reason.Menu), Is.SameAs(table.Of(Reason.Menu)));
            Assert.That(table.Of(Reason.Menu), Is.Not.SameAs(table.Of(Reason.Dialogue)));
            Assert.That(stack.Release(table.Of(Reason.Menu)), Is.True, "a second lookup ends the first one's hold");
            Assert.That(stack.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void AReasonSaysItsHoldersAndItsKey()
        {
            int asked = 0;
            MusicDuckReasonTable<string> table = new(
                _ =>
                {
                    asked++;
                    return MusicDuckHolders.Many;
                },
                StringComparer.OrdinalIgnoreCase
            );

            IMusicDuckReason dialogue = table.Of("dialogue");

            Assert.That(dialogue.Holders, Is.EqualTo(MusicDuckHolders.Many));
            Assert.That(dialogue.ToString(), Is.EqualTo("dialogue"));
            Assert.That(table.Of("DIALOGUE"), Is.SameAs(dialogue), "the comparer decides which keys are equal");
            Assert.That(asked, Is.EqualTo(1), "the holders are asked once per key");
            Assert.That(table.Of(null) == null);
        }
    }
}
