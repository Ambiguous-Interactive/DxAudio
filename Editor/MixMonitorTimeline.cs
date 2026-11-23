namespace WallstopStudios.DxAudio.Editor
{
    using System;

    internal sealed class MixMonitorTimeline<TClip>
    {
        internal const double HistorySeconds = 10d;

        internal int Count { get; private set; }
        internal int VoiceCount { get; private set; }

        private readonly Frame[] _frames;
        private (SfxVoiceState state, TClip clip, float clipSeconds)[] _voices = Array.Empty<(SfxVoiceState, TClip, float)>();
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
                _voices = new (SfxVoiceState, TClip, float)[_frames.Length * voiceCount];
                VoiceCount = voiceCount;
            }
            else if (0 < Count)
            {
                Array.Clear(_voices, 0, _voices.Length);
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
                Array.Clear(_voices, _oldest * VoiceCount, VoiceCount);
                _oldest = (_oldest + 1) % _frames.Length;
                --Count;
            }

            _latest = Slot(Count);
            ++Count;
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

        internal void SetVoice(int voice, in SfxVoiceState state, TClip clip = default, float clipSeconds = 0f)
        {
            _voices[_latest * VoiceCount + voice] = (state, clip, clipSeconds);
        }

        internal double Time(int frame)
        {
            return _frames[Slot(frame)].time;
        }

        internal bool StartsSegment(int frame)
        {
            return _frames[Slot(frame)].startsSegment;
        }

        internal SfxDropCounts Drops(int frame)
        {
            return _frames[Slot(frame)].drops;
        }

        internal ref readonly SfxVoiceState Voice(int frame, int voice)
        {
            return ref _voices[Slot(frame) * VoiceCount + voice].state;
        }

        internal (TClip clip, float seconds) Clip(int frame, int voice)
        {
            ref readonly (SfxVoiceState state, TClip clip, float clipSeconds) sample = ref _voices[Slot(frame) * VoiceCount + voice];
            return (sample.clip, sample.clipSeconds);
        }

        private int Slot(int frame)
        {
            return (_oldest + frame) % _frames.Length;
        }

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
