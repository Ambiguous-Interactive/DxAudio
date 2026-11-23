namespace WallstopStudios.DxAudio.Editor
{
    using System;

    internal sealed class MixMonitorTimeline
    {
        internal const double HistorySeconds = 10d;

        internal int Count { get; private set; }
        internal int VoiceCount { get; private set; }

        private readonly Frame[] _frames;
        private SfxVoiceState[] _voices = Array.Empty<SfxVoiceState>();
        private SfxDropCounts _previous;
        private int _oldest;
        private int _latest;
        private bool _interrupted = true;

        internal MixMonitorTimeline(int capacity)
        {
            _frames = new Frame[capacity];
        }

        internal void Reset(int voiceCount)
        {
            if (voiceCount != VoiceCount)
            {
                _voices = new SfxVoiceState[_frames.Length * voiceCount];
                VoiceCount = voiceCount;
            }

            Count = 0;
            _oldest = 0;
            _latest = 0;
            Interrupt();
        }

        internal void Interrupt()
        {
            _interrupted = true;
        }

        internal void Sample(double time, in SfxDropCounts counts)
        {
            while (0 < Count && (Count == _frames.Length || _frames[_oldest].time < time - HistorySeconds))
            {
                _oldest = (_oldest + 1) % _frames.Length;
                --Count;
            }

            _latest = Slot(Count);
            ++Count;
            Array.Clear(_voices, _latest * VoiceCount, VoiceCount);
            SfxDropCounts drops = _interrupted
                ? SfxDropCounts.None
                : new SfxDropCounts(
                    Math.Max(0, unchecked(counts.notPlayable - _previous.notPlayable)),
                    Math.Max(0, unchecked(counts.throttled - _previous.throttled)),
                    Math.Max(0, unchecked(counts.noVoice - _previous.noVoice)),
                    Math.Max(0, unchecked(counts.loopLimit - _previous.loopLimit)),
                    Math.Max(0, unchecked(counts.wrongPlayMethod - _previous.wrongPlayMethod))
                );
            _frames[_latest] = new Frame(time, drops, _interrupted);
            _previous = counts;
            _interrupted = false;
        }

        internal void SetVoice(int voice, in SfxVoiceState state)
        {
            _voices[_latest * VoiceCount + voice] = state;
        }

        internal double Time(int frame) => _frames[Slot(frame)].time;

        internal bool StartsSegment(int frame) => _frames[Slot(frame)].startsSegment;

        internal SfxDropCounts Drops(int frame) => _frames[Slot(frame)].drops;

        internal ref readonly SfxVoiceState Voice(int frame, int voice) => ref _voices[Slot(frame) * VoiceCount + voice];

        private int Slot(int frame) => (_oldest + frame) % _frames.Length;

        private readonly struct Frame
        {
            internal readonly double time;
            internal readonly SfxDropCounts drops;
            internal readonly bool startsSegment;

            internal Frame(double time, in SfxDropCounts drops, bool startsSegment)
            {
                this.time = time;
                this.drops = drops;
                this.startsSegment = startsSegment;
            }
        }
    }
}
