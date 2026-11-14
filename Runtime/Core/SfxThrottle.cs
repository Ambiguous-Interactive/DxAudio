namespace WallstopStudios.DxAudio
{
    using System.Collections.Generic;

    /// <summary>Rate-limits each key on its own, so a burst of one event plays once instead of stacking.</summary>
    /// <remarks>
    /// One frame can carry several of the same beat: ten props settling, a batched interaction. Playing every
    /// one sums into a phasing, clipping mess rather than sounding ten times louder. Per key rather than
    /// global, because a pickup must not silence a footstep at the same moment.
    ///
    /// Time is a parameter, so the decision is a pure function of its inputs. A key is any value with
    /// equality: <c>SfxPlayer</c> uses the cue asset itself. The key is a type parameter, so a game that keys
    /// cues by a struct does not box one per sound played.
    /// </remarks>
    /// <typeparam name="TKey">What a rate limit belongs to, such as a cue asset or a game's cue id.</typeparam>
    public sealed class SfxThrottle<TKey>
    {
        private readonly Dictionary<TKey, float> _lastPlayedByKey;

        public SfxThrottle()
            : this(null) { }

        /// <param name="comparer">How keys compare, or <see langword="null"/> for the type's default.</param>
        public SfxThrottle(IEqualityComparer<TKey> comparer)
        {
            _lastPlayedByKey = new Dictionary<TKey, float>(comparer);
        }

        /// <summary>
        /// Starts <paramref name="key"/>'s window when it is outside its last one, and says whether it was.
        /// </summary>
        public bool TryBegin(TKey key, float now, float minimumIntervalSeconds)
        {
            if (!IsReady(key, now, minimumIntervalSeconds))
            {
                return false;
            }

            _lastPlayedByKey[key] = now;
            return true;
        }

        /// <summary>Whether <paramref name="key"/> is outside its rate-limit window, without spending it.</summary>
        /// <remarks>
        /// Lets a caller tell "this cue was rate-limited" from "this cue could not get a voice" before it
        /// commits to either. Only the second is a dropped cue worth reporting.
        /// </remarks>
        public bool IsReady(TKey key, float now, float minimumIntervalSeconds)
        {
            if (key == null)
            {
                return false;
            }

            return minimumIntervalSeconds <= 0f
                || !_lastPlayedByKey.TryGetValue(key, out float lastPlayed)
                || minimumIntervalSeconds <= now - lastPlayed;
        }

        public void Forget(TKey key)
        {
            if (key != null)
            {
                _ = _lastPlayedByKey.Remove(key);
            }
        }

        public void Clear()
        {
            _lastPlayedByKey.Clear();
        }
    }
}
