namespace WallstopStudios.DxAudio
{
    using System;
    using System.Collections.Generic;

    /// <summary>Owns the random streams and take history for one player or audition session.</summary>
    internal sealed class SfxPlaybackResolver
    {
        private readonly SfxSelection<SfxCue> _selection = new();
        private readonly Dictionary<SfxCue, SfxShuffleBag> _bags = new();
        private ISfxRandom _takeAndPitch;
        private ISfxRandom _level;
        private ISfxRandom _brightness;

        private static float Draw(ISfxRandom source)
        {
            double draw = source.NextDouble();
            return double.IsNaN(draw) ? 0.5f : (float)draw;
        }

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
            _brightness = new LegacyRandom(seed ^ 0x3BE9C9);
        }

        internal void Warm(SfxCue cue)
        {
            if (cue != null && cue.Variation.UseShuffle)
            {
                BagFor(cue).Reserve(cue.Variants.Count);
            }
        }

        internal void ClearHistory()
        {
            _selection.Clear();
            foreach (SfxShuffleBag bag in _bags.Values)
            {
                bag.Restart();
            }
        }

        internal void ClearShuffleBags()
        {
            _bags.Clear();
        }

        internal bool TryResolve(SfxCue cue, out SfxPlayback play)
        {
            if (cue == null)
            {
                play = default;
                return false;
            }

            return TryResolve(cue, cue.TakeRevision, cue.Variation, cue.PitchJitter, cue.Volume, null, out play);
        }

        internal bool TryResolve(SfxCue cue, int takeRevision, SfxVariation variation, float legacyJitter, float cueVolume, float[] takeGains, out SfxPlayback play)
        {
            int count = cue == null ? 0 : cue.PlayableVariantCount;
            if (count == 0)
            {
                play = default;
                return false;
            }

            bool followed = _selection.TryFollow(cue, cue.FollowsTakeOf, count, out int index);
            if (followed)
            {
                RestartBag(cue);
            }
            else if (variation.UseShuffle)
            {
                int previous = _selection.TryGetLast(cue, out int last) ? last : -1;
                index = BagFor(cue).Next(count, takeRevision, previous, Draw(_takeAndPitch));
                _selection.Remember(cue, index);
            }
            else
            {
                RestartBag(cue);
                if (!_selection.TryNext(cue, count, Draw(_takeAndPitch), out index))
                {
                    play = default;
                    return false;
                }
            }

            if (!cue.TryGetPlayableVariant(index, out SfxVariant variant))
            {
                play = default;
                return false;
            }

            float pitch = variation.Pitch(legacyJitter, Draw(_takeAndPitch));
            float baseVolume = cueVolume * (takeGains == null ? variant.Gain : takeGains[index]);
            float volume = variation.Volume(baseVolume, Draw(_level), out float decibels);
            float cutoff = variation.UseLowPass ? variation.LowPassHertz(Draw(_brightness)) : 0f;
            play = new SfxPlayback(variant, cue.PlaybackStartSecondsFor(variant), pitch, baseVolume, volume, decibels, cutoff, followed);
            return true;
        }

        private SfxShuffleBag BagFor(SfxCue cue)
        {
            if (!_bags.TryGetValue(cue, out SfxShuffleBag bag))
            {
                bag = new SfxShuffleBag();
                _bags.Add(cue, bag);
            }

            return bag;
        }

        private void RestartBag(SfxCue cue)
        {
            if (0 < _bags.Count && _bags.TryGetValue(cue, out SfxShuffleBag bag))
            {
                bag.Restart();
            }
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
