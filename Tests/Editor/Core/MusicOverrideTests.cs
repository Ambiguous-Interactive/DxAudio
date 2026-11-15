namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    public sealed class MusicOverrideTests
    {
        [Test]
        public void AnOverridePlaysOverTheContextAndHandsBack()
        {
            List<string> heard = new();
            MusicOverride<string> selection = new(heard.Add);

            Assert.That(selection.SetContextTrack("town"), Is.True);
            MusicOverrideLease boss = selection.Take("boss");
            Assert.That(selection.Current, Is.EqualTo("boss"));
            Assert.That(selection.IsOverridden, Is.True);
            Assert.That(selection.Release(boss), Is.True);

            Assert.That(selection.Current, Is.EqualTo("town"));
            Assert.That(heard, Is.EqualTo(new[] { "town", "boss", "town" }));
        }

        [Test]
        public void OnlyTheNewestOverridesLeaseReleasesIt()
        {
            MusicOverride<string> selection = new();
            _ = selection.SetContextTrack("town");
            MusicOverrideLease older = selection.Take("elevator");
            MusicOverrideLease newer = selection.Take("boss");

            Assert.That(selection.Release(older), Is.False, "an older holder releasing late keeps the newer music");
            Assert.That(selection.Current, Is.EqualTo("boss"));
            newer.Dispose();
            Assert.That(selection.Current, Is.EqualTo("town"), "disposing releases");
            Assert.That(selection.Release(newer), Is.False, "a lease releases once");
        }

        [Test]
        public void AContextChangeClearsTheOverride()
        {
            MusicOverride<string> selection = new();
            _ = selection.SetContextTrack("town");
            MusicOverrideLease boss = selection.Take("boss");
            _ = selection.SetContextTrack("cave");

            Assert.That(selection.IsOverridden, Is.False);
            Assert.That(selection.Current, Is.EqualTo("cave"));
            Assert.That(selection.Release(boss), Is.False, "the override belonged to the old context");
        }

        [Test]
        public void ATrackThePlayableCheckRejectsIsNeverTaken()
        {
            List<string> heard = new();
            MusicOverride<string> selection = new(heard.Add, isPlayable: track => track.Length > 0);
            _ = selection.SetContextTrack("town");

            Assert.That(selection.Take("").IsValid, Is.False, "a silent track is refused, not held");
            Assert.That(selection.IsOverridden, Is.False);
            Assert.That(selection.Current, Is.EqualTo("town"));
            Assert.That(selection.Take("boss").IsValid, Is.True);
            Assert.That(heard, Is.EqualTo(new[] { "town", "boss" }));
        }

        [Test]
        public void TheSameMusicIsNotAChange()
        {
            int changes = 0;
            MusicOverride<string> selection = new(_ => changes++, StringComparer.OrdinalIgnoreCase);
            _ = selection.SetContextTrack("Town");

            Assert.That(selection.SetContextTrack("TOWN"), Is.False);
            Assert.That(selection.Take(null).IsValid, Is.False);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(selection.SetContextTrack(null), Is.True, "silence is a change");
            Assert.That(selection.SetContextTrack(null), Is.False);
        }
    }
}
