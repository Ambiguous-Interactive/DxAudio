namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Draws each playable take once before refilling.</summary>
    internal sealed class SfxShuffleBag
    {
        private int[] _tickets = Array.Empty<int>();
        private int _count;
        private int _remaining;
        private int _revision;

        private static int DrawIndex(int count, float draw)
        {
            double scaled = (double)AudioMath.Clamp(draw, 0f, 1f) * count;
            return (int)Math.Min(scaled, count - 1);
        }

        internal void Reserve(int capacity)
        {
            if (_tickets.Length < capacity)
            {
                Array.Resize(ref _tickets, capacity);
            }
        }

        internal void Restart()
        {
            _remaining = 0;
        }

        internal int Next(int count, int revision, int previous, float unitRandom)
        {
            if (_remaining == 0 || count != _count || revision != _revision)
            {
                Reserve(count);
                _count = count;
                _revision = revision;
                _remaining = count;
                for (int index = 0; index < count; ++index)
                {
                    _tickets[index] = index;
                }

                if (1 < count && 0 <= previous && previous < count)
                {
                    // Put the last take outside the first draw's range. It joins the remaining tickets on the second draw.
                    _tickets[previous] = count - 1;
                    _tickets[count - 1] = previous;
                    return Remove(DrawIndex(count - 1, unitRandom));
                }
            }

            return Remove(DrawIndex(_remaining, unitRandom));
        }

        private int Remove(int index)
        {
            int chosen = _tickets[index];
            _tickets[index] = _tickets[--_remaining];
            return chosen;
        }
    }
}
