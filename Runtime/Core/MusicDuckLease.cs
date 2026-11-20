namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>One live hold on the music, and the only thing that can give up that exact hold.</summary>
    /// <remarks>
    /// Identity is a monotonic id rather than the reason, because two holders of one
    /// <see cref="MusicDuckHolders.Many"/> reason are genuinely alive at once. Ids are never reused, so
    /// releasing a lease twice, or one <see cref="MusicDuckStack.SweepAbandoned"/> already reclaimed, does
    /// nothing rather than releasing whoever holds the music now. <see langword="default"/> is not a valid
    /// lease, so a field nobody assigned cannot release anything.
    ///
    /// Deliberately not <see cref="IDisposable"/>. A duck usually outlives any block: it lasts as long as a
    /// player reads a speech bubble. And the endings that strand one are the ones where no <c>finally</c>
    /// runs, such as a destroyed holder. <see cref="MusicDuckStack.SweepAbandoned"/> covers those.
    /// </remarks>
    public readonly struct MusicDuckLease : IEquatable<MusicDuckLease>
    {
        internal const long NoneId = 0;

        public static readonly MusicDuckLease None = new();

        public bool IsValid => _id != NoneId && _issuer != null;

        internal long Id => _id;

        public readonly IMusicDuckReason reason;

        private readonly long _id;
        private readonly MusicDuckStack _issuer;

        internal MusicDuckLease(long id, IMusicDuckReason reason, MusicDuckStack issuer)
        {
            _id = id;
            this.reason = reason;
            _issuer = issuer;
        }

        public bool Equals(MusicDuckLease other)
        {
            return _id == other._id && ReferenceEquals(_issuer, other._issuer);
        }

        public override bool Equals(object obj)
        {
            return obj is MusicDuckLease other && Equals(other);
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
            return IsValid ? $"{reason} duck #{_id}" : "no duck";
        }

        /// <remarks>
        /// Every stack numbers its holds from one, so identity is the pair (issuer, id), never the id alone.
        /// Checking only the id would let one stack release another's hold.
        /// </remarks>
        internal bool IssuedBy(MusicDuckStack stack)
        {
            return ReferenceEquals(_issuer, stack);
        }
    }
}
