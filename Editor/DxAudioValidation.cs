namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>Checks DxAudio authoring assets without changing them.</summary>
    public static class DxAudioValidation
    {
        internal static readonly Rule[] Rules = (Rule[])Enum.GetValues(typeof(Rule));

        /// <summary>Every problem with a sound library and its members.</summary>
        public static List<string> Validate(SfxLibrary library) => library == null ? new List<string> { "There is no SFX library to check." } : Messages(Find(library));

        /// <summary>Every problem with a music library and its members.</summary>
        public static List<string> Validate(MusicLibrary library) => library == null ? new List<string> { "There is no music library to check." } : Messages(Find(library));

        /// <summary>Every problem with one cue, for the cue inspector.</summary>
        public static List<string> Validate(SfxCue cue) => cue == null ? new List<string> { "There is no cue to check." } : Messages(Find(cue));

        /// <summary>Throws if a project library has an active error. Use as a Unity batchmode execute method.</summary>
        public static void ValidateProjectLibraries()
        {
            List<Finding> findings = DxAudioLibraryWindow.FindProjectErrors();
            if (findings.Count == 0)
            {
                return;
            }

            List<string> errors = new(findings.Count);
            foreach (Finding finding in findings)
            {
                errors.Add($"{AssetDatabase.GetAssetPath(finding.Asset)}: {finding.Label}");
            }

            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        internal static List<Finding> FindAsset(Object asset)
        {
            switch (asset)
            {
                case SfxLibrary sfx:
                    return Find(sfx);
                case MusicLibrary music:
                    return Find(music);
                case SfxCue cue:
                    return Find(cue);
                case MusicTrack track:
                    return Find(track, track.Clip);
                case DspChainAsset chain:
                    return Find(chain);
                case DspGraphAsset graph:
                    List<Finding> findings = new();
                    CollectGraph(graph, (_, _, finding) => findings.Add(finding));
                    return findings;
                default:
                    return new List<Finding>();
            }
        }

        internal static List<Finding> Find(SfxLibrary library)
        {
            List<Finding> findings = new();
            Dictionary<string, SfxCue> firstByKey = new(StringComparer.Ordinal);
            IReadOnlyList<SfxCue> cues = library.Cues;
            for (int index = 0; index < cues.Count; ++index)
            {
                SfxCue cue = cues[index];
                if (cue == null)
                {
                    findings.Add(new Finding(Rule.EmptyLibrarySlot, library, EmptySlot(index, "cue")));
                    continue;
                }

                string name = MemberName(cue, index);
                AddCue(findings, cue, name);
                if (string.IsNullOrWhiteSpace(cue.Key))
                {
                    findings.Add(new Finding(Rule.NoLookup, cue, $"{name} has no key, so nothing can look it up in this library."));
                    continue;
                }

                // The runtime index skips an unplayable member when choosing the first match.
                if (!cue.IsPlayable)
                {
                    continue;
                }

                if (firstByKey.TryGetValue(cue.Key, out SfxCue first))
                {
                    findings.Add(new Finding(Rule.DuplicateKey, cue, $"{name} shares the key '{cue.Key}' with '{first.name}'; only the first is reachable."));
                }
                else
                {
                    firstByKey[cue.Key] = cue;
                }
            }

            return findings;
        }

        internal static List<Finding> Find(MusicLibrary library)
        {
            List<Finding> findings = new();
            Dictionary<string, MusicTrack> firstByContext = new(StringComparer.Ordinal);
            IReadOnlyList<MusicTrack> tracks = library.Tracks;
            for (int index = 0; index < tracks.Count; ++index)
            {
                MusicTrack track = tracks[index];
                if (track == null)
                {
                    findings.Add(new Finding(Rule.EmptyLibrarySlot, library, EmptySlot(index, "track")));
                    continue;
                }

                string name = MemberName(track, index);
                AddTrack(findings, track, name, track.Clip);
                if (string.IsNullOrWhiteSpace(track.Context))
                {
                    findings.Add(new Finding(Rule.NoLookup, track, $"{name} names no context, so no lookup reaches it."));
                    continue;
                }

                if (!track.IsPlayable)
                {
                    continue;
                }

                if (firstByContext.TryGetValue(track.Context, out MusicTrack first))
                {
                    findings.Add(new Finding(Rule.DuplicateContext, track, $"{name} shares the context '{track.Context}' with '{first.name}'; only the first is reachable."));
                }
                else
                {
                    firstByContext[track.Context] = track;
                }
            }

            return findings;
        }

        internal static List<Finding> Find(SfxCue cue)
        {
            List<Finding> findings = new();
            AddCue(findings, cue, "This cue");
            return findings;
        }

        internal static List<Finding> Find(MusicTrack track, AudioClip clip)
        {
            List<Finding> findings = new();
            AddTrack(findings, track, "This track", clip);
            return findings;
        }

        internal static List<Finding> Find(DspChainAsset chain)
        {
            List<Finding> findings = new();
            AddChain(findings, chain);
            return findings;
        }

        internal static DspGraph CollectGraph(DspGraphAsset graph, Action<int, int, Finding> report)
        {
            DspGraph prepared = graph.CollectProblems((node, edge, message) => report(node, edge, new Finding(Rule.InvalidGraph, graph, message)));
            for (int index = 0; index < graph.NodeCount; ++index)
            {
                DspChainAsset chain = graph.Nodes[index]?.Chain;
                if (chain == null)
                {
                    continue;
                }

                foreach (Finding finding in Find(chain))
                {
                    report(index, -1, finding);
                }
            }

            return prepared;
        }

        internal static string Id(Rule rule) => $"DXA{(int)rule}";

        internal static Severity DefaultSeverity(Rule rule)
        {
            switch (rule)
            {
                case Rule.NoPlayableTake:
                case Rule.NoTrackClip:
                case Rule.InvalidVariation:
                case Rule.MissingChainEffect:
                case Rule.InvalidGraph:
                case Rule.UnplayableDrop:
                    return Severity.Error;
                case Rule.LoopOffset:
                case Rule.ThrottledDrop:
                    return Severity.Info;
                default:
                    return Severity.Warn;
            }
        }

        internal static string Description(Rule rule) =>
            rule switch
            {
                Rule.NoPlayableTake => "Cue has no playable take",
                Rule.NoTrackClip => "Track has no clip",
                Rule.InvalidVariation => "Cue has invalid variation bounds",
                Rule.NoLookup => "Cue has no key or track has no context",
                Rule.DuplicateKey => "Duplicate cue key",
                Rule.DuplicateContext => "Duplicate music context",
                Rule.EmptyLibrarySlot => "Empty library slot",
                Rule.SelfFollow => "Cue follows its own take",
                Rule.ClipLoadType => "Track load type differs from the clip import",
                Rule.LoopOffset => "Loop ignores take start offsets",
                Rule.MissingChainEffect => "Chain slot is missing its effect",
                Rule.InvalidGraph => "Graph cannot be built",
                Rule.UnplayableDrop => "Drop: NotPlayable or WrongPlayMethod",
                Rule.CapacityDrop => "Drop: NoVoice or LoopLimit",
                Rule.ThrottledDrop => "Drop: Throttled",
                _ => string.Empty,
            };

        internal static bool TryFromDrop(SfxCue cue, SfxDropReason reason, out Finding finding)
        {
            Rule rule;
            switch (reason)
            {
                case SfxDropReason.NotPlayable:
                case SfxDropReason.WrongPlayMethod:
                    rule = Rule.UnplayableDrop;
                    break;
                case SfxDropReason.NoVoice:
                case SfxDropReason.LoopLimit:
                    rule = Rule.CapacityDrop;
                    break;
                case SfxDropReason.Throttled:
                    rule = Rule.ThrottledDrop;
                    break;
                default:
                    finding = default;
                    return false;
            }

            finding = new Finding(rule, cue, $"Cue dropped: {reason}.");
            return true;
        }

        private static void AddCue(List<Finding> findings, SfxCue cue, string name)
        {
            if (!cue.IsPlayable)
            {
                findings.Add(new Finding(Rule.NoPlayableTake, cue, $"{name} has no playable take; no take has a clip."));
            }

            if (cue.Variation.HasInvalidBounds)
            {
                findings.Add(new Finding(Rule.InvalidVariation, cue, $"{name} has invalid playback variation bounds. Check the pitch, level, and cutoff ranges."));
            }

            if (cue.FollowsTakeOf == cue)
            {
                findings.Add(new Finding(Rule.SelfFollow, cue, $"{name} follows its own take number, so it never follows anything."));
            }

            if (cue.IsLooping && HasAuthoredStartOffset(cue))
            {
                findings.Add(new Finding(Rule.LoopOffset, cue, $"{name} loops, so its take start offsets are ignored; a loop restarts at zero."));
            }

            AddGraph(findings, cue.Graph, name);
            AddChain(findings, cue.Chain);
        }

        private static void AddTrack(List<Finding> findings, MusicTrack track, string name, AudioClip clip)
        {
            if (clip == null)
            {
                findings.Add(new Finding(Rule.NoTrackClip, track, $"{name} has no clip, so it cannot play."));
            }
            else if (MusicClipImport.TryGetAppliedLoadType(clip, out MusicClipLoadType applied) && applied != track.ResolvedClipLoadType)
            {
                findings.Add(new Finding(Rule.ClipLoadType, track, $"{name} asks for a {track.ResolvedClipLoadType} clip, but the clip is imported as {applied}."));
            }

            AddGraph(findings, track.Graph, name);
            AddChain(findings, track.Chain);
        }

        private static void AddGraph(List<Finding> findings, DspGraphAsset graph, string owner)
        {
            if (graph != null)
            {
                string name = string.IsNullOrWhiteSpace(graph.name) ? "an unnamed graph" : $"the graph '{graph.name}'";
                CollectGraph(
                    graph,
                    (_, _, finding) =>
                    {
                        string status = finding.Rule == Rule.InvalidGraph ? ", which cannot play" : string.Empty;
                        findings.Add(new Finding(finding.Rule, finding.Asset, $"{owner} names {name}{status}: {finding.Message}"));
                    }
                );
            }
        }

        private static void AddChain(List<Finding> findings, DspChainAsset chain)
        {
            if (chain == null)
            {
                return;
            }

            IReadOnlyList<DspEffectAsset> effects = chain.Effects;
            for (int index = 0; index < effects.Count; ++index)
            {
                if (effects[index] == null)
                {
                    findings.Add(new Finding(Rule.MissingChainEffect, chain, $"Chain '{chain.name}' has no effect in slot {index + 1}. Assign an effect or remove the slot."));
                }
            }
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

        private static List<string> Messages(List<Finding> findings)
        {
            List<string> messages = new(findings.Count);
            for (int index = 0; index < findings.Count; ++index)
            {
                messages.Add(findings[index].Message);
            }

            return messages;
        }

        private static string EmptySlot(int index, string member) => $"Slot {index + 1} is empty. Remove it or assign a {member}.";

        private static string MemberName(Object asset, int index) => string.IsNullOrWhiteSpace(asset.name) ? $"Slot {index + 1}" : $"'{asset.name}'";

        internal enum Severity
        {
            [Obsolete("Select a severity.", true)]
            Unknown = 0,
            Error = 1,
            Warn = 2,
            Info = 3,
            Off = 4,
        }

        internal enum Rule
        {
            [Obsolete("Select a rule.", true)]
            Unknown = 0,
            NoPlayableTake = 101,
            NoTrackClip = 102,
            InvalidVariation = 103,
            NoLookup = 104,
            DuplicateKey = 105,
            DuplicateContext = 106,
            EmptyLibrarySlot = 107,
            SelfFollow = 108,
            ClipLoadType = 109,
            LoopOffset = 110,
            MissingChainEffect = 201,
            InvalidGraph = 202,
            UnplayableDrop = 301,
            CapacityDrop = 302,
            ThrottledDrop = 303,
        }

        internal readonly struct Finding
        {
            internal string Label => $"{Id(Rule)} {Message}";

            internal readonly Rule Rule;
            internal readonly Object Asset;
            internal readonly string Message;

            internal Finding(Rule rule, Object asset, string message)
            {
                Rule = rule;
                Asset = asset;
                Message = message;
            }
        }
    }
}
