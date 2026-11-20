namespace WallstopStudios.DxAudio
{
    using System;
    using System.Collections.Generic;

    /// <summary>Hands one <see cref="IMusicDuckReason"/> per key, the same object on every call.</summary>
    /// <remarks>
    /// <see cref="MusicDuckStack"/> keys a hold by its reason's reference. A game that names its reasons with
    /// an enum or with strings must hand it the same object for the same name, or one release cannot end the
    /// hold. Authored reasons are <c>MusicDuck</c> assets; this table is for reasons that only code raises.
    /// </remarks>
    /// <typeparam name="TKey">What names a reason: an enum value, a string, or any key with equality.</typeparam>
    public sealed class MusicDuckReasonTable<TKey>
    {
        private readonly Func<TKey, MusicDuckHolders> _holdersOf;
        private readonly Dictionary<TKey, Reason> _reasons;

        /// <param name="holdersOf">How many things may hold the reason a key names. Asked once per key.</param>
        /// <param name="comparer">Which keys are equal. Defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
        public MusicDuckReasonTable(Func<TKey, MusicDuckHolders> holdersOf, IEqualityComparer<TKey> comparer = null)
        {
            _holdersOf = holdersOf ?? throw new ArgumentNullException(nameof(holdersOf));
            _reasons = new Dictionary<TKey, Reason>(comparer ?? EqualityComparer<TKey>.Default);
        }

        /// <summary>The reason <paramref name="key"/> names: the same object for equal keys.</summary>
        /// <returns><see langword="null"/> for a null key, which the stack refuses.</returns>
        public IMusicDuckReason Of(TKey key)
        {
            if (key == null)
            {
                return null;
            }

            if (!_reasons.TryGetValue(key, out Reason reason))
            {
                reason = new Reason(key, _holdersOf(key));
                _reasons.Add(key, reason);
            }

            return reason;
        }

        private sealed class Reason : IMusicDuckReason
        {
            public MusicDuckHolders Holders { get; }
            private readonly TKey _key;

            internal Reason(TKey key, MusicDuckHolders holders)
            {
                _key = key;
                Holders = holders;
            }

            public override string ToString()
            {
                return _key.ToString();
            }
        }
    }
}
