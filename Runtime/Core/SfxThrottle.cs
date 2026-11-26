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
    ///
    /// The <see langword="double"/> overloads keep a short window exact when a game has run for hours. A
    /// <see langword="float"/> clock cannot represent a millisecond step at a large elapsed time, so the window
    /// shrinks. The <see langword="float"/> overloads remain for callers that already hold a float clock.
    /// </remarks>
    /// <typeparam name="TKey">What a rate limit belongs to, such as a cue asset or a game's cue id.</typeparam>
    public sealed class SfxThrottle<TKey>
    {
        private readonly Dictionary<TKey, double> _lastPlayedByKey;

        public SfxThrottle()
            : this(null) { }

        /// <param name="comparer">How keys compare, or <see langword="null"/> for the type's default.</param>
        public SfxThrottle(IEqualityComparer<TKey> comparer)
        {
            _lastPlayedByKey = new Dictionary<TKey, double>(comparer);
        }

        /// <summary>
        /// Starts <paramref name="key"/>'s window when it is outside its last one, and says whether it was.
        /// </summary>
        public bool TryBegin(TKey key, double now, double minimumIntervalSeconds)
        {
            if (!IsReady(key, now, minimumIntervalSeconds))
            {
                return false;
            }

            _lastPlayedByKey[key] = now;
            return true;
        }

        /// <summary>
        /// Starts <paramref name="key"/>'s window from a float clock. Prefer the double overload when the clock
        /// can run for hours.
        /// </summary>
        public bool TryBegin(TKey key, float now, float minimumIntervalSeconds)
        {
            return TryBegin(key, (double)now, (double)minimumIntervalSeconds);
        }

        /// <summary>Whether <paramref name="key"/> is outside its rate-limit window, without spending it.</summary>
        /// <remarks>
        /// Lets a caller tell "this cue was rate-limited" from "this cue could not get a voice" before it
        /// commits to either. Only the second is a dropped cue worth reporting.
        /// </remarks>
        public bool IsReady(TKey key, double now, double minimumIntervalSeconds)
        {
            if (key == null)
            {
                return false;
            }

            return minimumIntervalSeconds <= 0d || !_lastPlayedByKey.TryGetValue(key, out double lastPlayed) || minimumIntervalSeconds <= now - lastPlayed;
        }

        /// <summary>
        /// Whether <paramref name="key"/> is outside its rate-limit window, read from a float clock. Prefer the
        /// double overload when the clock can run for hours.
        /// </summary>
        public bool IsReady(TKey key, float now, float minimumIntervalSeconds)
        {
            return IsReady(key, (double)now, (double)minimumIntervalSeconds);
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
