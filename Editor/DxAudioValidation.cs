namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;

    /// <summary>Checks DxAudio assets for content a player cannot reach, and for a clip that ignores its track.</summary>
    /// <remarks>
    /// An empty take, a key two cues share, or a track no context names is silent at run time rather than
    /// wrong-sounding, so it is easiest to find before play. A music clip whose import type does not follow its
    /// track still plays, but holds more or less memory than the track asks for. These checks only read assets: they
    /// never change one, and nothing in the runtime assemblies depends on them.
    /// </remarks>
    public static class DxAudioValidation
    {
        /// <summary>Every problem that would keep a listed cue from answering its key.</summary>
        public static List<string> Validate(SfxLibrary library)
        {
            List<string> findings = new();
            if (library == null)
            {
                findings.Add("There is no SFX library to check.");
                return findings;
            }

            Dictionary<string, SfxCue> firstByKey = new(StringComparer.Ordinal);
            IReadOnlyList<SfxCue> cues = library.Cues;
            for (int index = 0; index < cues.Count; ++index)
            {
                SfxCue cue = cues[index];
                if (cue == null)
                {
                    findings.Add($"Slot {index + 1} is empty. Remove it or assign a cue.");
                    continue;
                }

                string name = CueName(cue, index);
                if (!cue.IsPlayable)
                {
                    findings.Add($"{name} has no playable take; no take has a clip.");
                }

                if (cue.FollowsTakeOf == cue)
                {
                    findings.Add($"{name} follows its own take number, so it never follows anything.");
                }

                if (cue.IsLooping && HasAuthoredStartOffset(cue))
                {
                    findings.Add($"{name} loops, so its take start offsets are ignored; a loop restarts at zero.");
                }

                if (string.IsNullOrWhiteSpace(cue.Key))
                {
                    findings.Add($"{name} has no key, so nothing can look it up in this library.");
                    continue;
                }

                // The runtime index skips a cue with no take, so it cannot be the one a shared key serves.
                if (!cue.IsPlayable)
                {
                    continue;
                }

                if (firstByKey.TryGetValue(cue.Key, out SfxCue first))
                {
                    findings.Add($"{name} shares the key '{cue.Key}' with '{first.name}'; only the first is reachable.");
                }
                else
                {
                    firstByKey[cue.Key] = cue;
                }
            }

            return findings;
        }

        /// <summary>Every problem that would keep a listed track from answering its context.</summary>
        public static List<string> Validate(MusicLibrary library)
        {
            List<string> findings = new();
            if (library == null)
            {
                findings.Add("There is no music library to check.");
                return findings;
            }

            Dictionary<string, MusicTrack> firstByContext = new(StringComparer.Ordinal);
            IReadOnlyList<MusicTrack> tracks = library.Tracks;
            for (int index = 0; index < tracks.Count; ++index)
            {
                MusicTrack track = tracks[index];
                if (track == null)
                {
                    findings.Add($"Slot {index + 1} is empty. Remove it or assign a track.");
                    continue;
                }

                string name = TrackName(track, index);
                if (!track.IsPlayable)
                {
                    findings.Add($"{name} has no clip, so it cannot play.");
                }
                else if (!MusicClipImport.Matches(track))
                {
                    MusicClipImport.TryGetAppliedLoadType(track.Clip, out MusicClipLoadType applied);
                    findings.Add(
                        $"{name} asks for a {track.ResolvedClipLoadType} clip, but the clip is imported as "
                            + $"{applied}; the track inspector applies the choice to the clip, which every user of "
                            + "the clip shares."
                    );
                }

                if (string.IsNullOrWhiteSpace(track.Context))
                {
                    findings.Add($"{name} names no context, so no lookup reaches it.");
                    continue;
                }

                // The runtime index skips a track with no clip, so it cannot be the one a shared context serves.
                if (!track.IsPlayable)
                {
                    continue;
                }

                if (firstByContext.TryGetValue(track.Context, out MusicTrack first))
                {
                    findings.Add($"{name} shares the context '{track.Context}' with '{first.name}'; only the first is reachable.");
                }
                else
                {
                    firstByContext[track.Context] = track;
                }
            }

            return findings;
        }

        /// <summary>Every problem with one cue, for the cue inspector.</summary>
        public static List<string> Validate(SfxCue cue)
        {
            List<string> findings = new();
            if (cue == null)
            {
                findings.Add("There is no cue to check.");
                return findings;
            }

            if (!cue.IsPlayable)
            {
                findings.Add("This cue has no playable take; no take has a clip.");
            }

            if (cue.FollowsTakeOf == cue)
            {
                findings.Add("This cue follows its own take number, so it never follows anything.");
            }

            if (cue.IsLooping && HasAuthoredStartOffset(cue))
            {
                findings.Add("This cue loops, so its take start offsets are ignored; a loop restarts at zero.");
            }

            return findings;
        }

        private static string CueName(SfxCue cue, int index)
        {
            return string.IsNullOrWhiteSpace(cue.name) ? $"Slot {index + 1}" : $"'{cue.name}'";
        }

        private static string TrackName(MusicTrack track, int index)
        {
            return string.IsNullOrWhiteSpace(track.name) ? $"Slot {index + 1}" : $"'{track.name}'";
        }

        private static bool HasAuthoredStartOffset(SfxCue cue)
        {
            IReadOnlyList<SfxVariant> variants = cue.Variants;
            for (int index = 0; index < variants.Count; ++index)
            {
                SfxVariant variant = variants[index];
                if (variant != null && 0f < variant.StartTimeSeconds)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
