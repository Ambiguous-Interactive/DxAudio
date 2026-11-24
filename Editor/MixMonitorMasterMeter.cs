namespace WallstopStudios.DxAudio.Editor
{
    using UnityEngine;

    internal sealed class MixMonitorMasterMeter
    {
        internal const float MinimumFaderDecibels = -60f;

        // A listener block can span more than one editor poll.
        private const double SignalTimeoutSeconds = 0.25d;

        internal float FaderDecibels { get; private set; }
        internal bool Muted { get; private set; }
        internal MixMonitorPeakChannel Left => _left;
        internal MixMonitorPeakChannel Right => _right;
        internal double? ShortTermLufs => HasSignal ? _loudness.ShortTerm : null;
        internal double? IntegratedLufs => _loudness.Integrated;
        internal float? TruePeak { get; private set; }
        internal bool TruePeakOverrun { get; private set; }
        internal bool IsAttached => _tap != null;
        internal bool HasSignal { get; private set; }

        private readonly MasterLoudness _loudness = new();
        private AudioMixBus _bus;
        private StereoPeakTap _tap;
        private MixMonitorPeakChannel _left;
        private MixMonitorPeakChannel _right;
        private MixMonitorPeakChannel _truePeak;
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

        internal void SetAudition(float decibels, bool muted)
        {
            if (_tap == null || !AudioMath.IsFinite(decibels))
            {
                return;
            }

            FaderDecibels = Mathf.Clamp(decibels, MinimumFaderDecibels, 0f);
            Muted = muted;
            _tap.AuditionGain = muted ? 0f : AudioMath.DecibelsToLinear(FaderDecibels);
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
            FaderDecibels = 0f;
            Muted = false;
            HasSignal = false;
            _left = default;
            _right = default;
            _truePeak = default;
            _loudness.Reset();
            TruePeak = null;
            TruePeakOverrun = false;
        }
    }
}
