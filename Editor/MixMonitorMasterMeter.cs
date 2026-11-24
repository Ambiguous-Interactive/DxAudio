namespace WallstopStudios.DxAudio.Editor
{
    using UnityEngine;

    internal sealed class MixMonitorMasterMeter
    {
        // A listener block can span more than one editor poll.
        private const double SignalTimeoutSeconds = 0.25d;

        internal Channel Left => _left;
        internal Channel Right => _right;
        internal float? TruePeak { get; private set; }
        internal bool TruePeakOverrun { get; private set; }
        internal bool IsAttached => _tap != null;
        internal bool HasSignal { get; private set; }

        private AudioMixBus _bus;
        private StereoPeakTap _tap;
        private Channel _left;
        private Channel _right;
        private Channel _truePeak;
        private double _lastTruePeakTime;
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

            bool received = _tap.TryRead(out float left, out float right);
            if (received)
            {
                _lastSignalTime = now;
            }

            HasSignal = received || (HasSignal && now < _lastSignalTime + SignalTimeoutSeconds);
            _left.Sample(received ? left : null, HasSignal, now);
            _right.Sample(received ? right : null, HasSignal, now);

            bool receivedTruePeak = _tap.TryReadTruePeak(out float peak, out bool overrun);
            if (overrun)
            {
                TruePeakOverrun = true;
                TruePeak = null;
                _truePeak = default;
                return;
            }
            if (receivedTruePeak)
            {
                _lastTruePeakTime = now;
                TruePeakOverrun = false;
            }

            bool hasTruePeak = receivedTruePeak || (TruePeak.HasValue && now < _lastTruePeakTime + SignalTimeoutSeconds);
            _truePeak.Sample(receivedTruePeak ? peak : null, hasTruePeak, now);
            TruePeak = hasTruePeak ? _truePeak.Held : null;
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
            _truePeak = default;
            TruePeak = null;
            TruePeakOverrun = false;
        }

        internal struct Channel
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
}
