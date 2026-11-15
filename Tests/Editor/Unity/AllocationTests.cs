namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Keeps the play path honest: the package claims no managed allocation per play.
    /// </summary>
    public sealed class AllocationTests
    {
        private readonly List<UnityEngine.Object> _made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object made in _made)
            {
                if (made != null)
                {
                    UnityEngine.Object.DestroyImmediate(made);
                }
            }

            _made.Clear();
        }

        [Test]
        public void PlayingACueOnAFullPoolDoesNotAllocate()
        {
            SfxCue cue = Cue();
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { cue }, voiceCount: 4));

            for (int i = 0; i < 64; i++)
            {
                player.Play(cue);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                player.Play(cue);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L), $"Play allocated {allocated} bytes over 1,000 warmed plays.");
        }

        [Test]
        public void PlayingByKeyDoesNotAllocate()
        {
            SfxCue cue = Cue(key: "hit");
            SfxPlayer player = SfxPlayer(SfxLibrary.Create(new[] { cue }, voiceCount: 4));

            for (int i = 0; i < 64; i++)
            {
                player.Play("hit");
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                player.Play("hit");
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L), $"Keyed Play allocated {allocated} bytes over 1,000 warmed plays.");
        }

        private SfxPlayer SfxPlayer(SfxLibrary library)
        {
            Made(library);
            SfxPlayer player = Made(new GameObject("sfx")).AddComponent<SfxPlayer>();
            player.Library = library;
            return player;
        }

        private SfxCue Cue(string key = null)
        {
            return Made(
                SfxCue.Create(
                    new[] { new SfxVariant(Clip()), new SfxVariant(Clip()), new SfxVariant(Clip()) },
                    key: key,
                    loop: false,
                    minimumIntervalSeconds: 0f
                )
            );
        }

        private AudioClip Clip()
        {
            return Made(AudioClip.Create("test clip", 4_000, 1, 8_000, stream: false));
        }

        private T Made<T>(T made)
            where T : UnityEngine.Object
        {
            _made.Add(made);
            return made;
        }
    }
}
