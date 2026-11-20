namespace WallstopStudios.DxAudio
{
    using System.Collections.Generic;

    /// <summary>Picks which variant a multi-variant cue plays next, never the same one twice in a row.</summary>
    /// <remarks>
    /// Uniform random is the obvious choice and the wrong one: over a handful of variants it repeats often
    /// enough to be heard as a stutter, the machine-gun effect that makes repeated pickups sound synthetic.
    /// Drawing only from the variants that are not the previous one costs nothing and removes it.
    ///
    /// Memory is per key, for the same reason <see cref="SfxThrottle{TKey}"/> is: "not twice in a row" is a
    /// statement about one set of takes. With one shared memory, a pickup landing between two footsteps
    /// overwrites what the footstep last played, and the footstep is free to repeat.
    ///
    /// The random value is a parameter rather than drawn here, so the choice is a pure function of its
    /// inputs and testable without stubbing a generator. The key is a type parameter, so a struct key does
    /// not box per draw.
    /// </remarks>
    /// <typeparam name="TKey">What a set of takes belongs to, such as a cue asset or a game's cue id.</typeparam>
    public sealed class SfxSelection<TKey>
    {
        private const float LargestUnitRandom = 0.9999f;

        private readonly IEqualityComparer<TKey> _comparer;

        // A missing key means "nothing has played yet" for that set of takes, rather than a -1 sentinel.
        private readonly Dictionary<TKey, int> _lastIndexByKey;

        public SfxSelection()
            : this(null) { }

        /// <param name="comparer">How keys compare, or <see langword="null"/> for the type's default.</param>
        public SfxSelection(IEqualityComparer<TKey> comparer)
        {
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            _lastIndexByKey = new Dictionary<TKey, int>(_comparer);
        }

        private static int ClampIndex(int value, int highest)
        {
            if (value < 0)
            {
                return 0;
            }

            return highest < value ? highest : value;
        }

        /// <summary>Picks the next of <paramref name="variantCount"/> variants for <paramref name="key"/>.</summary>
        /// <param name="key">What the variants belong to. Only its own history is consulted.</param>
        /// <param name="variantCount">How many variants there are to choose from.</param>
        /// <param name="unitRandom">A value in [0, 1). Values outside are clamped into range.</param>
        /// <param name="index">The variant to play.</param>
        /// <returns><see langword="false"/> when there is nothing to choose.</returns>
        public bool TryNext(TKey key, int variantCount, float unitRandom, out int index)
        {
            if (key == null || variantCount <= 0)
            {
                index = default;
                return false;
            }

            if (variantCount == 1)
            {
                _lastIndexByKey[key] = 0;
                index = 0;
                return true;
            }

            float clamped = AudioMath.Clamp(unitRandom, 0f, LargestUnitRandom);
            int chosen;

            // The range check covers a variant list that shrank since this key's last pick.
            if (_lastIndexByKey.TryGetValue(key, out int last) && last < variantCount)
            {
                // Draw from the variants that are not the previous one, then step past it.
                int drawn = (int)(clamped * (variantCount - 1));
                chosen = ClampIndex(drawn, variantCount - 2);
                if (last <= chosen)
                {
                    chosen++;
                }
            }
            else
            {
                int drawn = (int)(clamped * variantCount);
                chosen = ClampIndex(drawn, variantCount - 1);
            }

            _lastIndexByKey[key] = chosen;
            index = chosen;
            return true;
        }

        /// <summary>
        /// Plays the same take number <paramref name="leader"/> last played, so two halves of one
        /// performance match: a question and its answer, a wind-up and its release.
        /// </summary>
        /// <returns>
        /// <see langword="false"/> when the leader has played nothing yet, or played a take this key does
        /// not have. The caller then draws with <see cref="TryNext"/> as usual.
        /// </returns>
        /// <remarks>
        /// A key equal to <paramref name="leader"/> never follows it. Only equality is checked: a game whose
        /// key splits one cue into several sets of takes, such as by what struck it, must refuse a cue that
        /// follows itself before it calls this.
        /// </remarks>
        public bool TryFollow(TKey key, TKey leader, int variantCount, out int index)
        {
            if (key == null || leader == null || _comparer.Equals(key, leader) || !_lastIndexByKey.TryGetValue(leader, out int last) || variantCount <= last)
            {
                index = default;
                return false;
            }

            _lastIndexByKey[key] = last;
            index = last;
            return true;
        }

        public void Forget(TKey key)
        {
            if (key != null)
            {
                _ = _lastIndexByKey.Remove(key);
            }
        }

        public void Clear()
        {
            _lastIndexByKey.Clear();
        }

        internal bool TryGetLast(TKey key, out int index)
        {
            return _lastIndexByKey.TryGetValue(key, out index);
        }

        internal void Remember(TKey key, int index)
        {
            _lastIndexByKey[key] = index;
        }
    }
}
