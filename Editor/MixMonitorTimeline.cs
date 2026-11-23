namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;

    internal sealed class MixMonitorTimeline<TClip, TTrack>
        where TTrack : class
    {
        internal const double HistorySeconds = 10d;

        internal int Count { get; private set; }
        internal int VoiceCount { get; private set; }
        internal int DeckCount { get; }
        internal int DuckCapacity { get; }

        private readonly Frame[] _frames;
        private readonly (TTrack track, float fade)[] _decks;
        private readonly (IMusicDuckReason reason, long id)[] _duckHolds;
        private readonly int[] _omittedDuckHolds;
        private readonly bool[] _assignedDuckInputs;
        private (SfxVoiceState state, TClip clip, float clipSeconds, float[] peaks, int playbackId)[] _voices = Array.Empty<(SfxVoiceState, TClip, float, float[], int)>();
        private SfxDropCounts _previous;
        private int _oldest;
        private int _latest;
        private bool _interrupted = true;

        internal MixMonitorTimeline(int capacity, int deckCount = 0, int duckCapacity = 0)
        {
            _frames = new Frame[capacity];
            DeckCount = deckCount;
            _decks = new (TTrack, float)[capacity * deckCount];
            DuckCapacity = duckCapacity;
            _duckHolds = new (IMusicDuckReason, long)[capacity * duckCapacity];
            _omittedDuckHolds = new int[capacity];
            _assignedDuckInputs = new bool[duckCapacity];
        }

        internal void Reset(int voiceCount)
        {
            if (voiceCount != VoiceCount)
            {
                _voices = new (SfxVoiceState, TClip, float, float[], int)[_frames.Length * voiceCount];
                VoiceCount = voiceCount;
            }
            else if (0 < Count)
            {
                Array.Clear(_voices, 0, _voices.Length);
            }

            if (0 < Count)
            {
                Array.Clear(_decks, 0, _decks.Length);
                Array.Clear(_duckHolds, 0, _duckHolds.Length);
                Array.Clear(_omittedDuckHolds, 0, _omittedDuckHolds.Length);
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
                Array.Clear(_duckHolds, _oldest * DuckCapacity, DuckCapacity);
                _omittedDuckHolds[_oldest] = 0;
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

        internal void SetVoice(int voice, in SfxVoiceState state, TClip clip = default, float clipSeconds = 0f, float[] peaks = null, int playbackId = 0)
        {
            _voices[_latest * VoiceCount + voice] = (state, clip, clipSeconds, peaks, playbackId);
        }

        internal void SetDeck(int deck, TTrack track, float fade)
        {
            _decks[_latest * DeckCount + deck] = (track, fade);
        }

        internal (TTrack track, float fade) Deck(int frame, int deck)
        {
            return _decks[Slot(frame) * DeckCount + deck];
        }

        internal void SetDuckHolds(IMusicDuckReason[] reasons, long[] ids, int total)
        {
            int count = Math.Min(DuckCapacity, total);
            _omittedDuckHolds[_latest] = total - count;
            if (count == 0)
            {
                return;
            }

            int offset = _latest * DuckCapacity;
            Array.Clear(_assignedDuckInputs, 0, count);
            if (1 < Count)
            {
                // Reserve the previous lanes for surviving leases before filling gaps with new ones.
                int previous = Slot(Count - 2) * DuckCapacity;
                for (int row = 0; row < DuckCapacity; ++row)
                {
                    long previousId = _duckHolds[previous + row].id;
                    if (previousId == MusicDuckLease.NoneId)
                    {
                        continue;
                    }

                    for (int input = 0; input < count; ++input)
                    {
                        if (ids[input] == previousId)
                        {
                            _duckHolds[offset + row] = (reasons[input], ids[input]);
                            _assignedDuckInputs[input] = true;
                            break;
                        }
                    }
                }
            }

            int next = offset;
            for (int input = 0; input < count; ++input)
            {
                if (_assignedDuckInputs[input])
                {
                    continue;
                }

                while (_duckHolds[next].id != MusicDuckLease.NoneId)
                {
                    ++next;
                }

                _duckHolds[next++] = (reasons[input], ids[input]);
            }
        }

        internal (IMusicDuckReason reason, long id) DuckHold(int frame, int row)
        {
            return _duckHolds[Slot(frame) * DuckCapacity + row];
        }

        internal int OmittedDuckHolds(int frame)
        {
            return _omittedDuckHolds[Slot(frame)];
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
            int offset = Slot(frame) * VoiceCount + voice;
            return (_voices[offset].clip, _voices[offset].clipSeconds);
        }

        internal float[] VoicePeaks(int frame, int voice)
        {
            return _voices[Slot(frame) * VoiceCount + voice].peaks;
        }

        internal float VoiceClipEnd(int frame, int voice, float duration, double maxGapSeconds)
        {
            int offset = Slot(frame) * VoiceCount + voice;
            float seconds = _voices[offset].clipSeconds;
            if (Count <= frame + 1 || !CanJoin(frame + 1, maxGapSeconds))
            {
                return seconds;
            }

            int next = Slot(frame + 1) * VoiceCount + voice;
            ref readonly SfxVoiceState first = ref _voices[offset].state;
            ref readonly SfxVoiceState second = ref _voices[next].state;
            if (
                !first.isPlaying
                || !second.isPlaying
                || _voices[offset].playbackId != _voices[next].playbackId
                || !EqualityComparer<TClip>.Default.Equals(_voices[offset].clip, _voices[next].clip)
            )
            {
                return seconds;
            }

            float nextSeconds = _voices[next].clipSeconds;
            if (seconds <= nextSeconds)
            {
                return nextSeconds;
            }

            return first.isHeld && second.isHeld ? nextSeconds + duration : seconds;
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
