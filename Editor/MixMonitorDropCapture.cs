namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;

    internal sealed class MixMonitorDropCapture
    {
        internal static IReadOnlyList<SfxDropReason> Reasons { get; } =
            Array.AsReadOnly(Array.FindAll((SfxDropReason[])Enum.GetValues(typeof(SfxDropReason)), reason => (int)reason != 0));

        internal bool Paused { get; set; }
        internal SfxDropCounts Counts { get; private set; }
        internal int VisibleCount => Counts.Total - SilencedCount;

        internal int SilencedCount
        {
            get
            {
                int count = 0;
                foreach (SfxDropReason reason in _silenced)
                {
                    count += Counts[reason];
                }

                return count;
            }
        }

        private readonly HashSet<SfxDropReason> _silenced = new() { SfxDropReason.Throttled };

        internal void Sample(in SfxDropCounts counts)
        {
            if (!Paused)
            {
                Counts = counts;
            }
        }

        internal void Reset()
        {
            Paused = false;
            Counts = SfxDropCounts.None;
        }

        internal bool IsSilenced(SfxDropReason reason)
        {
            return _silenced.Contains(reason);
        }

        internal void SetSilenced(SfxDropReason reason, bool silenced)
        {
            if (silenced)
            {
                _silenced.Add(reason);
            }
            else
            {
                _silenced.Remove(reason);
            }
        }
    }
}
