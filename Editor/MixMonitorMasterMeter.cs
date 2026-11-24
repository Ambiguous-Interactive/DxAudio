namespace WallstopStudios.DxAudio.Editor
{
    using UnityEngine;

    internal sealed class MixMonitorMasterMeter
    {
        // A listener block can span more than one editor poll.
        private const double SignalTimeoutSeconds = 0.25d;

        internal Channel Left => _left;
        internal Channel Right => _right;
        internal double? ShortTermLufs => HasSignal ? _loudness.ShortTerm : null;
        internal double? IntegratedLufs => _loudness.Integrated;
        internal float? TruePeak { get; private set; }
        internal bool TruePeakOverrun { get; private set; }
        internal bool IsAttached => _tap != null;
        internal bool HasSignal { get; private set; }

        private readonly MasterLoudness _loudness = new();
        private AudioMixBus _bus;
        private StereoPeakTap _tap;
        private Channel _left;
        private Channel _right;
        private Channel _truePeak;
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

            ReadCapturedAudio(_tap, now);
        }

        internal void ReadCapturedAudio(StereoPeakTap tap, double now)
        {
            bool received = tap.TryRead(out float left, out float right);
            if (received)
            {
                _lastSignalTime = now;
            }

            bool hasSignal = received || (HasSignal && now < _lastSignalTime + SignalTimeoutSeconds);
            if (HasSignal && !hasSignal)
            {
                _loudness.Reset();
            }

            HasSignal = hasSignal;
            _left.Sample(received ? left : null, HasSignal, now);
            _right.Sample(received ? right : null, HasSignal, now);

            bool receivedTruePeak = tap.TryReadTruePeak(out float peak, out bool overrun, _loudness);
            if (overrun)
            {
                TruePeakOverrun = true;
                TruePeak = null;
                _truePeak = default;
                return;
            }
            if (receivedTruePeak)
            {
                TruePeakOverrun = false;
            }

            _truePeak.Sample(receivedTruePeak ? peak : null, receivedTruePeak, now);
            TruePeak = receivedTruePeak || 0f < _truePeak.Held ? _truePeak.Held : null;
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
            _loudness.Reset();
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
