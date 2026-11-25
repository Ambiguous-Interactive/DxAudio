namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    [Serializable]
    internal sealed class MixMonitorCaptureExport
    {
        public int version = 1;
        public int cursor;
        public int voiceSlots;
        public Drop[] dropTotals;
        public string[] silencedReasons;
        public Frame[] frames;

        internal MixMonitorCaptureExport(MixMonitorTimeline<AudioClip, MusicTrack> timeline, MixMonitorDropCapture capture, int cursor)
        {
            this.cursor = cursor;
            voiceSlots = timeline.VoiceCount;
            dropTotals = ExportDrops(capture.Counts);
            List<string> silenced = new();
            foreach (SfxDropReason reason in MixMonitorDropCapture.Reasons)
            {
                if (capture.IsSilenced(reason))
                {
                    silenced.Add(reason.ToString());
                }
            }

            silencedReasons = silenced.ToArray();
            frames = new Frame[timeline.Count];
            for (int frame = 0; frame < frames.Length; ++frame)
            {
                frames[frame] = new Frame(timeline, frame);
            }
        }

        private static Drop[] ExportDrops(in SfxDropCounts counts)
        {
            List<Drop> drops = new();
            foreach (SfxDropReason reason in MixMonitorDropCapture.Reasons)
            {
                int count = counts[reason];
                if (0 < count)
                {
                    drops.Add(new Drop { reason = reason.ToString(), count = count });
                }
            }

            return drops.ToArray();
        }

        [Serializable]
        public struct Frame
        {
            public double seconds;
            public bool startsSegment;
            public Drop[] drops;
            public Voice[] voices;
            public Deck[] decks;
            public bool hasDuckGain;
            public float duckGain;
            public Hold[] duckHolds;
            public int omittedDuckHolds;

            internal Frame(MixMonitorTimeline<AudioClip, MusicTrack> timeline, int frame)
            {
                seconds = timeline.ElapsedSeconds(frame);
                startsSegment = frame == 0 || timeline.StartsSegment(frame);
                drops = ExportDrops(timeline.Drops(frame));

                List<Voice> activeVoices = new();
                for (int slot = 0; slot < timeline.VoiceCount; ++slot)
                {
                    ref readonly SfxVoiceState state = ref timeline.Voice(frame, slot);
                    if (!state.isUsable || state.IsFree)
                    {
                        continue;
                    }

                    (AudioClip clip, float clipSeconds) = timeline.Clip(frame, slot);
                    activeVoices.Add(
                        new Voice
                        {
                            slot = slot + 1,
                            clip = clip == null ? string.Empty : clip.name,
                            clipSeconds = clipSeconds,
                            priority = state.priority.ToString(),
                            playing = state.isPlaying,
                            held = state.isHeld,
                            paused = state.isPaused,
                        }
                    );
                }
                voices = activeVoices.ToArray();

                List<Deck> activeDecks = new();
                for (int slot = 0; slot < timeline.DeckCount; ++slot)
                {
                    (MusicTrack track, float fade) = timeline.Deck(frame, slot);
                    if (track != null)
                    {
                        activeDecks.Add(
                            new Deck
                            {
                                slot = slot + 1,
                                track = track.name,
                                fade = fade,
                            }
                        );
                    }
                }
                decks = activeDecks.ToArray();

                float? gain = timeline.DuckGain(frame);
                hasDuckGain = gain.HasValue;
                duckGain = gain.GetValueOrDefault();
                List<Hold> holds = new();
                for (int row = 0; row < timeline.DuckCapacity; ++row)
                {
                    (IMusicDuckReason reason, long id) = timeline.DuckHold(frame, row);
                    if (reason != null)
                    {
                        holds.Add(
                            new Hold
                            {
                                row = row + 1,
                                reason = DxAudioMixMonitorWindow.DuckName(reason),
                                id = id,
                            }
                        );
                    }
                }
                duckHolds = holds.ToArray();
                omittedDuckHolds = timeline.OmittedDuckHolds(frame);
            }
        }

        [Serializable]
        public struct Drop
        {
            public string reason;
            public int count;
        }

        [Serializable]
        public struct Voice
        {
            public int slot;
            public string clip;
            public float clipSeconds;
            public string priority;
            public bool playing;
            public bool held;
            public bool paused;
        }

        [Serializable]
        public struct Deck
        {
            public int slot;
            public string track;
            public float fade;
        }

        [Serializable]
        public struct Hold
        {
            public int row;
            public string reason;
            public long id;
        }
    }
}
