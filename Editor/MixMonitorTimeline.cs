namespace WallstopStudios.DxAudio.Editor
{
    using System;

    internal sealed class MixMonitorTimeline<TClip, TTrack>
        where TTrack : class
    {
        internal const double HistorySeconds = 10d;

        internal int Count { get; private set; }
        internal int VoiceCount { get; private set; }
        internal int DeckCount { get; }

        private readonly Frame[] _frames;
        private readonly (TTrack track, float fade)[] _decks;
        private (SfxVoiceState state, TClip clip, float clipSeconds)[] _voices = Array.Empty<(SfxVoiceState, TClip, float)>();
        private SfxDropCounts _previous;
        private int _oldest;
        private int _latest;
        private bool _interrupted = true;

        internal MixMonitorTimeline(int capacity, int deckCount = 0)
        {
            _frames = new Frame[capacity];
            DeckCount = deckCount;
            _decks = new (TTrack, float)[capacity * deckCount];
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

            if (0 < Count)
            {
                Array.Clear(_decks, 0, _decks.Length);
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

        internal void Sample(double time, in SfxDropCounts counts, float? duckGain = null)
        {
            while (0 < Count && (Count == _frames.Length || _frames[_oldest].time < time - HistorySeconds))
            {
                Array.Clear(_voices, _oldest * VoiceCount, VoiceCount);
                Array.Clear(_decks, _oldest * DeckCount, DeckCount);
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
            _frames[_latest] = new Frame(time, drops, _interrupted, duckGain);
            _previous = counts;
            _interrupted = false;
        }

        internal void SetVoice(int voice, in SfxVoiceState state, TClip clip = default, float clipSeconds = 0f)
        {
            _voices[_latest * VoiceCount + voice] = (state, clip, clipSeconds);
        }

        internal void SetDeck(int deck, TTrack track, float fade)
        {
            _decks[_latest * DeckCount + deck] = (track, fade);
        }

        internal (TTrack track, float fade) Deck(int frame, int deck)
        {
            return _decks[Slot(frame) * DeckCount + deck];
        }

        internal float? DuckGain(int frame)
        {
            return _frames[Slot(frame)].duckGain;
        }

        internal double Time(int frame)
        {
            return _frames[Slot(frame)].time;
        }

        internal bool StartsSegment(int frame)
        {
            return _frames[Slot(frame)].startsSegment;
        }

        internal bool CanJoin(int frame, double maxGapSeconds)
        {
            return 0 < frame && !StartsSegment(frame) && Time(frame) - Time(frame - 1) <= maxGapSeconds;
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
            internal readonly float? duckGain;

            internal Frame(double time, in SfxDropCounts drops, bool startsSegment, float? duckGain)
            {
                this.time = time;
                this.drops = drops;
                this.startsSegment = startsSegment;
                this.duckGain = duckGain;
            }
        }
    }
}
