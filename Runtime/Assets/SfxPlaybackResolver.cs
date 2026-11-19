namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Owns the random streams and take history for one player or audition session.</summary>
    internal sealed class SfxPlaybackResolver
    {
        private readonly SfxSelection<SfxCue> _selection = new();
        private ISfxRandom _takeAndPitch;
        private ISfxRandom _level;

        internal void Reset(int seed)
        {
            SetSeed(seed);
            ClearHistory();
        }

        internal void SetSeed(int seed, Func<int, ISfxRandom> factory = null)
        {
            int levelSeed = seed ^ 0x51F0A7;
            ISfxRandom takeAndPitch = factory?.Invoke(seed);
            ISfxRandom level = factory?.Invoke(levelSeed);
            if (takeAndPitch == null || level == null || ReferenceEquals(takeAndPitch, level))
            {
                takeAndPitch = new LegacyRandom(seed);
                level = new LegacyRandom(levelSeed);
            }

            _takeAndPitch = takeAndPitch;
            _level = level;
        }

        internal void ClearHistory()
        {
            _selection.Clear();
        }

        internal bool TryResolve(SfxCue cue, out SfxPlayback play)
        {
            int count = cue == null ? 0 : cue.PlayableVariantCount;
            if (count == 0)
            {
                play = default;
                return false;
            }

            bool followed = _selection.TryFollow(cue, cue.FollowsTakeOf, count, out int index);
            if (!followed && !_selection.TryNext(cue, count, Draw(_takeAndPitch), out index))
            {
                play = default;
                return false;
            }

            if (!cue.TryGetPlayableVariant(index, out SfxVariant variant))
            {
                play = default;
                return false;
            }

            float pitch = cue.PitchFor(Draw(_takeAndPitch));
            float volume = cue.Variation.Volume(cue.Volume, Draw(_level), out float decibels);
            play = new SfxPlayback(variant, cue.PlaybackStartSecondsFor(variant), pitch, cue.Volume, volume, decibels, followed);
            return true;
        }

        private static float Draw(ISfxRandom source)
        {
            double draw = source.NextDouble();
            return double.IsNaN(draw) ? 0.5f : (float)draw;
        }

        private sealed class LegacyRandom : ISfxRandom
        {
            private readonly Random _random;

            internal LegacyRandom(int seed)
            {
                _random = new Random(seed);
            }

            public double NextDouble() => _random.NextDouble();
        }
    }
}
