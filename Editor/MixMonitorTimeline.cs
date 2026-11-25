namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;

    internal sealed class MixMonitorTimeline<TClip, TTrack>
        where TTrack : class
    {
        internal const double HistorySeconds = 10d;
        private const double MaximumAudioClipPitch = 3d;

        internal int Count { get; private set; }
        internal int VoiceCount { get; private set; }
        internal int DeckCount { get; }
        internal int DuckCapacity { get; }

        private readonly Frame[] _frames;
        private readonly (TTrack track, float fade)[] _decks;
        private readonly (IMusicDuckReason reason, long id)[] _duckHolds;
        private readonly int[] _omittedDuckHolds;
        private readonly bool[] _assignedDuckInputs;
        private (SfxVoiceState state, TClip clip, float clipSeconds, float[] peaks, int playbackId, int stolenPlaybackId)[] _voices = Array.Empty<(
            SfxVoiceState,
            TClip,
            float,
            float[],
            int,
            int
        )>();
        private SfxDropCounts _previous;
        private int _oldest;
        private int _latest;
        private double _captureStart;
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
                _voices = new (SfxVoiceState, TClip, float, float[], int, int)[_frames.Length * voiceCount];
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

        internal int Sample(double time, in SfxDropCounts counts, float? duckGain = null)
        {
            if (Count == 0)
            {
                _captureStart = time;
            }

            int evicted = 0;
            while (0 < Count && (Count == _frames.Length || _frames[_oldest].time < time - HistorySeconds))
            {
                Array.Clear(_voices, _oldest * VoiceCount, VoiceCount);
                Array.Clear(_decks, _oldest * DeckCount, DeckCount);
                Array.Clear(_duckHolds, _oldest * DuckCapacity, DuckCapacity);
                _omittedDuckHolds[_oldest] = 0;
                _oldest = (_oldest + 1) % _frames.Length;
                --Count;
                ++evicted;
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
            return evicted;
        }

        internal void SetVoice(int voice, in SfxVoiceState state, TClip clip = default, float clipSeconds = 0f, float[] peaks = null, int playbackId = 0, int stolenPlaybackId = 0)
        {
            _voices[_latest * VoiceCount + voice] = (state, clip, clipSeconds, peaks, playbackId, stolenPlaybackId);
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

        internal double ElapsedSeconds(int frame)
        {
            return Time(frame) - _captureStart;
        }

        internal int FrameAt(double time)
        {
            int low = 0;
            int high = Count - 1;
            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                if (Time(middle) <= time)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
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

        internal int OccupiedVoiceCount(int frame)
        {
            int occupied = 0;
            for (int voice = 0; voice < VoiceCount; ++voice)
            {
                ref readonly SfxVoiceState state = ref Voice(frame, voice);
                if (state.isUsable && !state.IsFree)
                {
                    ++occupied;
                }
            }

            return occupied;
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

        internal bool VoiceWasStolen(int frame, int voice, double maxGapSeconds)
        {
            int nextFrame = frame + 1;
            if (Count <= nextFrame || !CanJoin(nextFrame, maxGapSeconds))
            {
                return false;
            }

            int victim = _voices[Slot(frame) * VoiceCount + voice].playbackId;
            return victim != 0 && victim == _voices[Slot(nextFrame) * VoiceCount + voice].stolenPlaybackId;
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
            if (nextSeconds < seconds && first.isHeld && second.isHeld)
            {
                nextSeconds += duration;
            }

            // Allow the full capture gap for audio buffer timing, even if the editor ticks sooner.
            double maxAdvance = MaximumAudioClipPitch * maxGapSeconds;
            return seconds <= nextSeconds && nextSeconds - seconds <= maxAdvance ? nextSeconds : seconds;
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
