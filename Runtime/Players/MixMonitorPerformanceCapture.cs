#if UNITY_EDITOR
namespace WallstopStudios.DxAudio
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using Unity.Profiling;

    /// <summary>Measures one editor capture. The audio thread only reads the attachment and adds elapsed ticks.</summary>
    internal sealed class MixMonitorPerformanceCapture
    {
        private static volatile MixMonitorPerformanceCapture _active;

        internal long PlayCalls { get; private set; }
        internal long PlayBytes { get; private set; }

        private readonly SfxPlayer _player;
        private readonly bool _canMeasurePlay;
        private ProfilerRecorder _recorder;
        private bool _measuringPlay;
        private long _dspTicks;

        private MixMonitorPerformanceCapture(SfxPlayer player)
        {
            _player = player;
            if (player != null)
            {
                _recorder = new ProfilerRecorder(
                    ProfilerCategory.Internal,
                    "GC.Alloc",
                    1,
                    ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread
                );
                _canMeasurePlay = _recorder.Valid;
            }
        }

        internal static MixMonitorPerformanceCapture Attach(SfxPlayer player)
        {
            if (_active != null)
            {
                return null;
            }

            MixMonitorPerformanceCapture capture = new(player);
            _active = capture;
            return capture;
        }

        internal static PlayScope MeasurePlay(SfxPlayer player)
        {
            MixMonitorPerformanceCapture capture = _active;
            if (capture == null || !ReferenceEquals(capture._player, player) || capture._measuringPlay || !capture._canMeasurePlay)
            {
                return default;
            }

            capture._measuringPlay = true;
            capture._recorder.Start();
            return new PlayScope(capture);
        }

        internal static DspScope MeasureDsp()
        {
            MixMonitorPerformanceCapture capture = _active;
            return capture == null ? default : new DspScope(capture, Stopwatch.GetTimestamp());
        }

        internal double ReadDspMilliseconds()
        {
            return Interlocked.Exchange(ref _dspTicks, 0L) * (1000d / Stopwatch.Frequency);
        }

        internal void Detach()
        {
            if (_active != this)
            {
                return;
            }

            _active = null;
            if (!_measuringPlay)
            {
                _recorder.Dispose();
            }
        }

        private void FinishPlay()
        {
            _recorder.Stop();
            if (0 < _recorder.Count)
            {
                PlayBytes += _recorder.GetSample(0).Value;
            }

            ++PlayCalls;
            _measuringPlay = false;
            if (_active == this)
            {
                _recorder.Reset();
            }
            else
            {
                // A drop subscriber can close or pause the monitor before Play returns.
                _recorder.Dispose();
            }
        }

        internal readonly struct PlayScope : IDisposable
        {
            private readonly MixMonitorPerformanceCapture _capture;

            internal PlayScope(MixMonitorPerformanceCapture capture)
            {
                _capture = capture;
            }

            public void Dispose()
            {
                _capture?.FinishPlay();
            }
        }

        internal readonly struct DspScope : IDisposable
        {
            private readonly MixMonitorPerformanceCapture _capture;
            private readonly long _start;

            internal DspScope(MixMonitorPerformanceCapture capture, long start)
            {
                _capture = capture;
                _start = start;
            }

            public void Dispose()
            {
                if (_capture != null)
                {
                    Interlocked.Add(ref _capture._dspTicks, Stopwatch.GetTimestamp() - _start);
                }
            }
        }
    }
}
#endif
