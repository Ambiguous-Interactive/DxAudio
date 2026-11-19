namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DxAudio.Editor;

    public sealed class AssetDetailsTests
    {
        [Test]
        public void LibrarySearchMatchesKeysAndNamesAndKeepsMembershipOrder()
        {
            using MadeObjects made = new();
            SfxCue first = made.Track(SfxCue.Create(null, key: "impact.iron"));
            first.name = "Hit";
            SfxCue second = made.Track(SfxCue.Create(null, key: "footstep"));
            second.name = "Iron Boot";
            SfxLibrary library = made.Track(SfxLibrary.Create(new[] { first, null, second }));

            List<DxAudioAssetDetails.Member> members = new();
            DxAudioAssetDetails.CollectMembers(library, " iron ", members);
            Assert.That(members.Count, Is.EqualTo(2));
            Assert.That(members[0].Asset, Is.SameAs(first));
            Assert.That(members[1].Asset, Is.SameAs(second));
            Assert.That(members[1].Slot, Is.EqualTo(3));
            DxAudioAssetDetails.CollectMembers(library, string.Empty, members);
            Assert.That(members.Count, Is.EqualTo(3));
            Assert.That(members[1].Asset == null, Is.True);
        }

        [Test]
        public void LibrarySearchMatchesMusicContexts()
        {
            using MadeObjects made = new();
            MusicTrack track = made.Track(MusicTrack.Create(null, context: "Battle"));
            MusicLibrary library = made.Track(MusicLibrary.Create(new[] { track }));
            List<DxAudioAssetDetails.Member> members = new();

            DxAudioAssetDetails.CollectMembers(library, "battle", members);

            Assert.That(members.Count, Is.EqualTo(1));
            Assert.That(members[0].Asset, Is.SameAs(track));
        }

        [Test]
        public void TrackAuditionUsesTheAuthoredOffsetAndTrim()
        {
            using MadeObjects made = new();
            AudioClip clip = made.Track(AudioClip.Create("Track", 48_000, 1, 48_000, false));
            MusicTrack track = made.Track(MusicTrack.Create(clip, startTimeSeconds: 0.25f, volumeScale: 0.4f));
            try
            {
                AudioSource source = SfxAudition.Play(track);
                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.volume, Is.EqualTo(0.4f));
                Assert.That(source.loop, Is.False);
                Assert.That(source.time, Is.EqualTo(track.StartTimeSeconds).Within(0.001f));
            }
            finally
            {
                SfxAudition.DestroyPreview();
            }
        }
    }
}
