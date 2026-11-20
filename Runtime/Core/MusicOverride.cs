namespace WallstopStudios.DxAudio
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Which music is wanted now: the current context's, unless something has temporarily taken over.
    /// </summary>
    /// <remarks>
    /// A boss fight, an elevator or a cutscene plays its own music and then hands back. Holding that as a
    /// lease means only the holder can end it, and ending it returns to the context's music.
    ///
    /// The last override taken wins, and only its lease releases it, so an older holder releasing late cannot
    /// end a newer holder's music. Setting a new context clears any override, because a context change is a
    /// new scene of play and an override belongs to the one it was taken in.
    /// </remarks>
    /// <typeparam name="TTrack">What a selection is. <c>MusicPlayer</c> uses <c>MusicTrack</c>.</typeparam>
    public sealed class MusicOverride<TTrack> : IMusicOverrideIssuer
        where TTrack : class
    {
        /// <summary>The music wanted now: the override when there is one, otherwise the context's.</summary>
        /// <remarks>A destroyed override track no longer counts, so the context's music is wanted.</remarks>
        public TTrack Current => UnityObjects.IsGone(_overrideTrack) ? _contextTrack : _overrideTrack;

        public TTrack ContextTrack => _contextTrack;

        public bool IsOverridden => _activeId != MusicOverrideLease.NoneId;
        private readonly IEqualityComparer<TTrack> _sameMusic;
        private readonly Action<TTrack> _onSelectionChanged;
        private readonly Func<TTrack, bool> _isPlayable;

        private TTrack _contextTrack;
        private TTrack _overrideTrack;
        private TTrack _announced;
        private long _nextId = MusicOverrideLease.NoneId + 1;
        private long _activeId = MusicOverrideLease.NoneId;

        /// <param name="onSelectionChanged">Called with the newly wanted music whenever it changes.</param>
        /// <param name="sameMusic">
        /// Whether two selections are the same music. A change between two that are equal is not a change.
        /// Defaults to <see cref="EqualityComparer{T}.Default"/>.
        /// </param>
        /// <param name="isPlayable">
        /// Whether a track has something to play. <see cref="Take"/> refuses one it rejects, rather than hold an
        /// override that plays silence while it reports that it holds the music. Defaults to every track.
        /// </param>
        public MusicOverride(Action<TTrack> onSelectionChanged = null, IEqualityComparer<TTrack> sameMusic = null, Func<TTrack, bool> isPlayable = null)
        {
            _onSelectionChanged = onSelectionChanged;
            _sameMusic = sameMusic ?? EqualityComparer<TTrack>.Default;
            _isPlayable = isPlayable;
        }

        /// <summary>Sets the context's music and clears any override.</summary>
        /// <returns>Whether the wanted music changed.</returns>
        public bool SetContextTrack(TTrack track)
        {
            _contextTrack = track;
            _overrideTrack = null;
            _activeId = MusicOverrideLease.NoneId;
            return Publish();
        }

        /// <summary>Plays <paramref name="track"/> over the context until the lease is released.</summary>
        /// <returns>The lease, or an invalid one when <paramref name="track"/> is null, destroyed or not playable.</returns>
        public MusicOverrideLease Take(TTrack track)
        {
            if (UnityObjects.IsGone(track) || (_isPlayable != null && !_isPlayable(track)))
            {
                return MusicOverrideLease.None;
            }

            _activeId = _nextId++;
            _overrideTrack = track;
            _ = Publish();
            return new MusicOverrideLease(_activeId, this);
        }

        /// <summary>Ends the override <paramref name="lease"/> names, if it is still the active one.</summary>
        /// <returns>Whether the wanted music changed.</returns>
        public bool Release(in MusicOverrideLease lease)
        {
            if (!lease.IsValid || !lease.IssuedBy(this) || lease.Id != _activeId)
            {
                return false;
            }

            _activeId = MusicOverrideLease.NoneId;
            _overrideTrack = null;
            return Publish();
        }

        /// <summary>Announces <see cref="Current"/> when it is other music than was last announced.</summary>
        /// <remarks>
        /// It compares with the last announcement, not with <see cref="Current"/> before the change, because
        /// <see cref="Current"/> also changes with no call here: when the override track is destroyed. Its
        /// release must still hand the music back to the context.
        /// </remarks>
        private bool Publish()
        {
            TTrack previous = _announced;
            TTrack current = Current;
            bool unchanged = previous == null ? current == null : current != null && _sameMusic.Equals(previous, current);
            if (unchanged)
            {
                return false;
            }

            _announced = current;
            _onSelectionChanged?.Invoke(current);
            return true;
        }
    }

    internal interface IMusicOverrideIssuer
    {
        bool Release(in MusicOverrideLease lease);
    }

    /// <summary>A claim on the music, handed back by <see cref="MusicOverride{TTrack}.Take"/>.</summary>
    /// <remarks>
    /// Disposable, unlike <see cref="MusicDuckLease"/>, because an override is often scoped: a
    /// <c>using</c> around a cutscene ends it however the cutscene ends.
    /// </remarks>
    public readonly struct MusicOverrideLease : IDisposable, IEquatable<MusicOverrideLease>
    {
        internal const long NoneId = 0;

        public static readonly MusicOverrideLease None = new();

        public bool IsValid => _id != NoneId && _issuer != null;

        internal long Id => _id;

        private readonly long _id;
        private readonly IMusicOverrideIssuer _issuer;

        internal MusicOverrideLease(long id, IMusicOverrideIssuer issuer)
        {
            _id = id;
            _issuer = issuer;
        }

        public void Dispose()
        {
            _ = _issuer?.Release(this);
        }

        public bool Equals(MusicOverrideLease other)
        {
            return _id == other._id && ReferenceEquals(_issuer, other._issuer);
        }

        public override bool Equals(object obj)
        {
            return obj is MusicOverrideLease other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int issuerHash = _issuer == null ? 0 : _issuer.GetHashCode();
                return (_id.GetHashCode() * 397) ^ issuerHash;
            }
        }

        public override string ToString()
        {
            return IsValid ? $"music override #{_id}" : "no override";
        }

        internal bool IssuedBy(IMusicOverrideIssuer issuer)
        {
            return ReferenceEquals(_issuer, issuer);
        }
    }
}
