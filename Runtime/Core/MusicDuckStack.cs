namespace WallstopStudios.DxAudio
{
    using System.Collections.Generic;
    using System.Text;

    /// <summary>Every live reason the music is held back, composed into one attenuation.</summary>
    /// <remarks>
    /// Each system ducks and releases on its own and never needs to know what else is active: the music sits
    /// at the most restrictive of all live holds, and releasing one leaves the others in force. A menu opened
    /// during a dialogue stays muffled after the dialogue ends, and comes back up only when both are gone.
    ///
    /// Pure, so the rules are tested against tables of holds rather than by listening.
    /// <c>MusicPlayer</c> reads <see cref="Current"/> and fades toward it.
    /// </remarks>
    public sealed class MusicDuckStack
    {
        private readonly Dictionary<IMusicDuckReason, ReasonHold> _active = new();
        private readonly List<IMusicDuckReason> _unheld = new();

        private long _nextId = MusicDuckLease.NoneId + 1;

        /// <summary>What every live hold composes to. <see cref="MusicAttenuation.Unattenuated"/> with none.</summary>
        public MusicAttenuation Current { get; private set; } = MusicAttenuation.Unattenuated;

        /// <summary>How many reasons hold the music down.</summary>
        public int ActiveCount => _active.Count;

        public bool IsActive(IMusicDuckReason reason)
        {
            return reason != null && _active.ContainsKey(reason);
        }

        public int HolderCount(IMusicDuckReason reason)
        {
            return reason != null && _active.TryGetValue(reason, out ReasonHold hold) ? hold.holders.Count : 0;
        }

        /// <summary>Holds the music at <paramref name="attenuation"/> for <paramref name="reason"/>.</summary>
        /// <param name="reason">Why. Its <see cref="IMusicDuckReason.Holders"/> decides what a repeat means.</param>
        /// <param name="attenuation">How far down. A repeat of a held reason replaces it.</param>
        /// <param name="holder">
        /// Who holds it, for a <see cref="MusicDuckHolders.Many"/> reason. A destroyed Unity object here is
        /// reclaimed by <see cref="SweepAbandoned"/>, and <see cref="Release(IMusicDuckReason, object)"/>
        /// can release by it.
        /// </param>
        /// <returns>The lease that releases this hold. Invalid when <paramref name="reason"/> is null.</returns>
        public MusicDuckLease Take(IMusicDuckReason reason, MusicAttenuation attenuation, object holder = null)
        {
            if (reason == null)
            {
                return MusicDuckLease.None;
            }

            bool many = reason.Holders == MusicDuckHolders.Many;
            if (!_active.TryGetValue(reason, out ReasonHold held))
            {
                held = new ReasonHold();
                _active[reason] = held;
            }
            else if (!many && 0 < held.holders.Count)
            {
                // The same state asserted again is the same hold: its id and every lease naming it survive.
                held.attenuation = attenuation;
                _ = Recompose();
                return new MusicDuckLease(held.holders[0].id, reason, this);
            }

            long id = _nextId++;
            held.attenuation = attenuation;
            held.holders.Add(new Hold(id, many ? holder : null));
            _ = Recompose();
            return new MusicDuckLease(id, reason, this);
        }

        /// <summary>Gives up exactly the hold <paramref name="lease"/> names.</summary>
        /// <returns>Whether <see cref="Current"/> changed.</returns>
        public bool Release(in MusicDuckLease lease)
        {
            if (!lease.IsValid || !lease.IssuedBy(this) || !_active.TryGetValue(lease.reason, out ReasonHold held))
            {
                return false;
            }

            List<Hold> holders = held.holders;
            for (int index = 0; index < holders.Count; ++index)
            {
                if (holders[index].id != lease.Id)
                {
                    continue;
                }

                holders.RemoveAt(index);
                DropIfUnheld(lease.reason, held);
                return Recompose();
            }

            return false;
        }

        /// <summary>
        /// Releases <paramref name="reason"/>: the whole of a <see cref="MusicDuckHolders.One"/> reason, or
        /// <paramref name="holder"/>'s hold on a <see cref="MusicDuckHolders.Many"/> one.
        /// </summary>
        /// <returns>Whether <see cref="Current"/> changed.</returns>
        public bool Release(IMusicDuckReason reason, object holder = null)
        {
            if (reason == null || !_active.TryGetValue(reason, out ReasonHold held))
            {
                return false;
            }

            if (reason.Holders != MusicDuckHolders.Many)
            {
                _ = _active.Remove(reason);
                return Recompose();
            }

            List<Hold> holders = held.holders;
            for (int index = 0; index < holders.Count; ++index)
            {
                // ReferenceEquals, not ==: a holder announcing its own destruction already equals null.
                if (!ReferenceEquals(holders[index].holder, holder))
                {
                    continue;
                }

                holders.RemoveAt(index);
                DropIfUnheld(reason, held);
                return Recompose();
            }

            return false;
        }

        /// <summary>Releases every hold whose holder was destroyed without releasing it.</summary>
        /// <param name="reclaimed">How many holds were released.</param>
        /// <returns>Whether <see cref="Current"/> changed.</returns>
        public bool SweepAbandoned(out int reclaimed)
        {
            int swept = 0;
            foreach (KeyValuePair<IMusicDuckReason, ReasonHold> entry in _active)
            {
                List<Hold> holders = entry.Value.holders;
                for (int index = holders.Count - 1; 0 <= index; --index)
                {
                    if (holders[index].IsAbandoned)
                    {
                        holders.RemoveAt(index);
                        ++swept;
                    }
                }
            }

            reclaimed = swept;
            if (swept == 0)
            {
                return false;
            }

            _unheld.Clear();
            foreach (KeyValuePair<IMusicDuckReason, ReasonHold> entry in _active)
            {
                if (entry.Value.holders.Count == 0)
                {
                    _unheld.Add(entry.Key);
                }
            }

            foreach (IMusicDuckReason reason in _unheld)
            {
                _ = _active.Remove(reason);
            }

            _unheld.Clear();
            return Recompose();
        }

        /// <summary>Releases everything. Returns whether <see cref="Current"/> changed.</summary>
        public bool Clear()
        {
            _active.Clear();
            return Recompose();
        }

        /// <summary>Every live hold, for a diagnostic.</summary>
        public string DescribeHolds()
        {
            if (_active.Count == 0)
            {
                return "Nothing is holding the music back.";
            }

            StringBuilder description = new();
            foreach (KeyValuePair<IMusicDuckReason, ReasonHold> entry in _active)
            {
                foreach (Hold hold in entry.Value.holders)
                {
                    if (0 < description.Length)
                    {
                        _ = description.Append(", ");
                    }

                    _ = description.Append(entry.Key).Append(" #").Append(hold.id);
                    if (hold.holder != null)
                    {
                        string holderName = hold.IsAbandoned ? "a destroyed object" : hold.holder.ToString();
                        _ = description.Append(" held by ").Append(holderName);
                    }
                }
            }

            return $"{Current} held by: {description}.";
        }

        private void DropIfUnheld(IMusicDuckReason reason, ReasonHold held)
        {
            if (held.holders.Count == 0)
            {
                _ = _active.Remove(reason);
            }
        }

        private bool Recompose()
        {
            MusicAttenuation composed = MusicAttenuation.Unattenuated;
            foreach (KeyValuePair<IMusicDuckReason, ReasonHold> entry in _active)
            {
                composed = MusicAttenuation.MostRestrictive(composed, entry.Value.attenuation);
            }

            if (composed.Equals(Current))
            {
                return false;
            }

            Current = composed;
            return true;
        }

        private sealed class ReasonHold
        {
            internal readonly List<Hold> holders = new();
            internal MusicAttenuation attenuation;
        }

        private readonly struct Hold
        {
            public readonly long id;
            public readonly object holder;

            public Hold(long id, object holder)
            {
                this.id = id;
                this.holder = holder;
            }

            /// <summary>Whether the holder was a Unity object that has since been destroyed.</summary>
            internal bool IsAbandoned
            {
                get
                {
#if UNITY_5_3_OR_NEWER
                    return holder is UnityEngine.Object unityObject && unityObject == null;
#else
                    return false;
#endif
                }
            }
        }
    }
}
