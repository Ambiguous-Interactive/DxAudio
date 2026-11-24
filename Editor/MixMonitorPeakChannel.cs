namespace WallstopStudios.DxAudio.Editor
{
    internal struct MixMonitorPeakChannel
    {
        private const double HoldSeconds = 1d;

        internal float Level { get; private set; }
        internal float Held { get; private set; }

        private double _holdUntil;

        internal void Sample(float? peak, bool hasSignal, double now)
        {
            Level = peak ?? (hasSignal ? Level : 0f);
            if (_holdUntil <= now || (peak.HasValue && Held <= Level))
            {
                Held = Level;
                if (peak.HasValue)
                {
                    _holdUntil = now + HoldSeconds;
                }
            }
        }
    }
}
