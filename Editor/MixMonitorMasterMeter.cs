namespace WallstopStudios.DxAudio.Editor
{
    using UnityEngine;

    internal sealed class MixMonitorMasterMeter
    {
        // A listener block can span more than one editor poll.
        private const double SignalTimeoutSeconds = 0.25d;

        internal Channel Left => _left;
        internal Channel Right => _right;
        internal bool IsAttached => _tap != null;
        internal bool HasSignal { get; private set; }

        private AudioMixBus _bus;
        private StereoPeakTap _tap;
        private Channel _left;
        private Channel _right;
        private double _lastSignalTime;

        internal static bool CanMonitor(AudioMixBus bus)
        {
            return bus != null
                && bus.isActiveAndEnabled
                && bus.TryGetComponent(out AudioListener listener)
                && listener.isActiveAndEnabled
                && !bus.TryGetComponent<AudioSource>(out _);
        }

        internal void Sample(AudioMixBus bus, double now)
        {
            if (!CanMonitor(bus))
            {
                Clear();
                return;
            }

            if (_bus != bus)
            {
                Clear();
                if (!bus.TryAttachPeakTap(out _tap))
                {
                    return;
                }

                _bus = bus;
            }

            if (_tap.TryRead(out float left, out float right))
            {
                _lastSignalTime = now;
                HasSignal = true;
                _left.Sample(left, now);
                _right.Sample(right, now);
            }
            else if (_lastSignalTime + SignalTimeoutSeconds <= now)
            {
                HasSignal = false;
                _left.Sample(0f, now);
                _right.Sample(0f, now);
            }
        }

        internal void Clear()
        {
            if (_bus != null)
            {
                _bus.DetachPeakTap(_tap);
            }

            _bus = null;
            _tap = null;
            HasSignal = false;
            _left = default;
            _right = default;
        }

        internal struct Channel
        {
            private const double HoldSeconds = 1d;

            internal float Level { get; private set; }
            internal float Held { get; private set; }

            private double _holdUntil;

            internal void Sample(float peak, double now)
            {
                Level = peak;
                if (Held <= peak || _holdUntil <= now)
                {
                    Held = peak;
                    _holdUntil = now + HoldSeconds;
                }
            }
        }
    }
}
