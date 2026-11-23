namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using static DxAudioValidation;

    /// <summary>The headings, rows and help lines the DxAudio authoring surfaces share.</summary>
    /// <remarks>
    /// Every surface draws with UI Toolkit, so one helper keeps the spacing and the warning shape the same. A
    /// finding is a <see cref="HelpBox" />, and a clear surface says so instead of showing nothing.
    /// </remarks>
    internal static class DxAudioInspectorUi
    {
        /// <summary>How often a live reading refreshes while audio plays, in milliseconds.</summary>
        private const long RefreshMilliseconds = 200;

        /// <summary>The rate a graph is measured at when the editor reports no output device, in hertz.</summary>
        private const int FallbackSampleRate = 48_000;

        /// <summary>Adds a spaced section under <paramref name="parent" /> and returns it.</summary>
        internal static VisualElement AddSection(VisualElement parent, string title)
        {
            VisualElement section = new VisualElement();
            section.style.marginTop = 8f;
            Label heading = new Label(title);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(heading);
            parent.Add(section);
            return section;
        }

        /// <summary>A row that holds a description and the control that acts on it.</summary>
        internal static VisualElement Row()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        /// <summary>Adds a label that takes the row's spare width and returns it.</summary>
        internal static Label AddText(VisualElement parent, string text)
        {
            Label label = new Label(text);
            label.style.flexGrow = 1f;
            parent.Add(label);
            return label;
        }

        internal static void FocusProperty(VisualElement root, string path)
        {
            PropertyField field = root.Query<PropertyField>().Where(candidate => string.Equals(candidate.bindingPath, path, StringComparison.Ordinal)).First();
            if (field != null)
            {
                field.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(field);
                field.focusable = true;
                field.Focus();
            }
        }

        /// <summary>Adds one row per stage of <paramref name="asset" />, in order, with any live gain reduction.</summary>
        /// <remarks>
        /// Order is the sound, so the rows are the authored list, top first. A chain that a running voice plays also
        /// shows how far each stage holds the signal down; that reading is diagnostics, so it may allocate. A mix bus
        /// shapes the sum rather than a voice, so the copy names the mix.
        /// </remarks>
        internal static void AddChainRows(VisualElement parent, DspChainAsset asset, bool mix = false)
        {
            if (asset == null)
            {
                parent.Add(new Label(mix ? "No chain: the mix passes through untouched." : "No chain: voices play their clip untouched."));
                return;
            }

            IReadOnlyList<DspEffectAsset> effects = asset.Effects;
            if (effects.Count == 0)
            {
                parent.Add(new Label(mix ? "The chain holds no effects, so the mix passes through untouched." : "The chain holds no effects, so it plays the clip untouched."));
                return;
            }

            float[] reductions = LiveReductions(asset, effects);
            for (int index = 0; index < effects.Count; ++index)
            {
                DspEffectAsset effect = effects[index];
                VisualElement row = Row();
                AddText(row, effect == null ? $"Stage {index + 1}: empty" : $"{index + 1}. {NameOf(effect)}: {effect.Describe()}");
                if (reductions != null && effect != null)
                {
                    AddText(row, $"{reductions[index]:0.#} dB");
                }

                parent.Add(row);
            }

            if (reductions == null)
            {
                parent.Add(new Label(mix ? "A running bus shows its live gain reduction beside each stage." : "A running voice shows its live gain reduction beside each stage."));
            }
        }

        /// <summary>Rebuilds <paramref name="rows" /> while audio plays, because a live reading moves with no edit.</summary>
        internal static void RefreshWhilePlaying(VisualElement rows, DspChainAsset asset, bool mix = false)
        {
            rows.schedule.Execute(() =>
                {
                    // The rows are rebuilt on an edit anyway. Only a running voice adds a reading with no edit.
                    if (Application.isPlaying)
                    {
                        rows.Clear();
                        AddChainRows(rows, asset, mix);
                    }
                })
                .Every(RefreshMilliseconds);
        }

        /// <summary>Adds one row per node and per edge of <paramref name="asset" />, with what the graph delays each branch.</summary>
        /// <remarks>
        /// The latency is measured on a graph built from the asset at <see cref="DspGraphAsset.ReferenceSampleRate" />,
        /// because the delay a chain reports is in samples at a rate and an inspector cannot show a live voice's figure
        /// for an asset nothing plays yet. This is authoring work, so it may allocate.
        /// </remarks>
        internal static void AddGraphRows(VisualElement parent, DspGraphAsset asset)
        {
            if (asset == null)
            {
                parent.Add(new Label("No graph: voices run no parallel branches."));
                return;
            }

            List<DxAudioValidation.Finding> findings = new();
            DspGraph graph = DxAudioValidation.CollectGraph(asset, (_, _, finding) => findings.Add(finding));
            parent.Add(new DxAudioInspectorUi.ProblemList(findings, "No active problems."));
            if (graph == null)
            {
                return;
            }

            IReadOnlyList<DspGraphNode> nodes = asset.Nodes;
            for (int index = 0; index < nodes.Count; ++index)
            {
                DspGraphNode node = nodes[index];
                VisualElement row = Row();
                AddText(row, node == null ? $"Node {index + 1}: empty" : $"Node \"{node.Name}\": {Describe(node)}");
                if (graph.TryGetNodeLatency(index, out int latency))
                {
                    AddText(row, $"{latency} samples");
                }

                parent.Add(row);
            }

            IReadOnlyList<DspGraphEdge> edges = asset.Edges;
            for (int index = 0; index < edges.Count; ++index)
            {
                DspGraphEdge edge = edges[index];
                parent.Add(new Label(edge == null ? $"Edge {index + 1}: empty" : $"\"{edge.From}\" -> \"{edge.To}\""));
            }

            parent.Add(new Label($"The graph delays the signal {graph.LatencySamples} samples at {DspGraphAsset.ReferenceSampleRate} Hz."));
        }

        /// <summary>What a node runs, as one line: its chain, or that it is a junction.</summary>
        private static string Describe(DspGraphNode node)
        {
            DspChainAsset chain = node.Chain;
            if (chain == null)
            {
                return "junction, passes its input on";
            }

            string name = string.IsNullOrEmpty(chain.name) ? "unnamed chain" : chain.name;
            return $"{name}, {chain.Count} stages";
        }

        /// <summary>The asset's name, or its type when an asset made in code was never given one.</summary>
        private static string NameOf(DspEffectAsset effect)
        {
            return string.IsNullOrEmpty(effect.name) ? effect.GetType().Name : effect.name;
        }

        /// <summary>The most restrictive live reduction per authored stage, across every running host or mix bus that plays the chain.</summary>
        /// <returns>
        /// <see langword="null" /> when nothing runs the chain, so no stage has a reading. An empty slot keeps its zero,
        /// because the built chain skips it and its stage number belongs to another row.
        /// </returns>
        private static float[] LiveReductions(DspChainAsset asset, IReadOnlyList<DspEffectAsset> effects)
        {
            if (!Application.isPlaying)
            {
                return null;
            }

            int stages = 0;
            for (int index = 0; index < effects.Count; ++index)
            {
                if (effects[index] != null)
                {
                    ++stages;
                }
            }

            AudioFilterHost[] hosts = Hosts();
            float[] reductions = null;
            foreach (AudioFilterHost host in hosts)
            {
                // A stopped voice keeps its chain and its last reading, so it must not speak for a sounding one.
                if (!host.IsSounding)
                {
                    continue;
                }

                DspChain chain = null;
                if (host.Chain == asset)
                {
                    chain = host.LiveChain;
                }
                else if (host.BusChain == asset)
                {
                    chain = host.LiveBusChain;
                }

                Accumulate(chain, stages, effects, ref reductions);
            }

            AudioMixBus[] buses = MixBuses();
            foreach (AudioMixBus bus in buses)
            {
                // A mix bus has no voice that starts or stops: it runs as long as the listener's object is enabled.
                if (bus.isActiveAndEnabled && bus.Chain == asset)
                {
                    Accumulate(bus.LiveChain, stages, effects, ref reductions);
                }
            }

            return reductions;
        }

        /// <summary>Keeps the most restrictive reading of <paramref name="chain" /> per authored stage.</summary>
        private static void Accumulate(DspChain chain, int stages, IReadOnlyList<DspEffectAsset> effects, ref float[] reductions)
        {
            if (chain == null || chain.Count != stages)
            {
                return;
            }

            reductions ??= new float[effects.Count];
            int stage = 0;
            for (int authored = 0; authored < effects.Count; ++authored)
            {
                if (effects[authored] == null)
                {
                    continue;
                }

                float reduction = chain[stage].GainReductionDecibels;
                if (reduction < reductions[authored])
                {
                    reductions[authored] = reduction;
                }

                ++stage;
            }
        }

        /// <summary>Every host the running editor holds.</summary>
        /// <remarks>
        /// The scene-wide finders changed shape twice across the supported versions, and the oldest and newest
        /// spellings each warn on the other's editor. This one call is the same on 2021.3 and on 6000.x.
        /// </remarks>
        private static AudioFilterHost[] Hosts()
        {
            return Resources.FindObjectsOfTypeAll<AudioFilterHost>();
        }

        /// <summary>Every mix bus the running editor holds, found the same way as a host.</summary>
        private static AudioMixBus[] MixBuses()
        {
            return Resources.FindObjectsOfTypeAll<AudioMixBus>();
        }

        internal sealed class ProblemList : VisualElement
        {
            private readonly IReadOnlyList<Finding> _findings;
            private readonly string _clear;
            private readonly Func<int, Action> _fix;
            private bool _showSilenced;

            internal ProblemList(IReadOnlyList<Finding> findings, string clear = null, Func<int, Action> fix = null)
            {
                _findings = findings;
                _clear = clear;
                _fix = fix;
                RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    DxAudioProblemSettings.Changed += Draw;
                    Draw();
                });
                RegisterCallback<DetachFromPanelEvent>(_ => DxAudioProblemSettings.Changed -= Draw);
                Draw();
            }

            internal static Image Badge(Severity severity)
            {
                string icon =
                    severity == Severity.Error ? "console.erroricon.sml"
                    : severity == Severity.Warn ? "console.warnicon.sml"
                    : "console.infoicon.sml";
                Image badge = new()
                {
                    image = EditorGUIUtility.IconContent(icon).image,
                    tooltip = severity.ToString(),
                    pickingMode = PickingMode.Ignore,
                };
                badge.style.width = 14f;
                badge.style.height = 14f;
                return badge;
            }

            private void Draw()
            {
                Clear();
                DxAudioProblemSettings settings = DxAudioProblemSettings.instance;
                int visible = 0;
                int hidden = 0;
                for (int index = 0; index < _findings.Count; ++index)
                {
                    Finding finding = _findings[index];
                    Severity severity = settings.SeverityFor(finding.Rule);
                    if (severity == Severity.Off)
                    {
                        continue;
                    }

                    bool shared = settings.IsSharedSilence(finding);
                    bool personal = DxAudioProblemSettings.IsPersonalSilence(finding.Rule);
                    if (shared || personal)
                    {
                        ++hidden;
                        if (!_showSilenced)
                        {
                            continue;
                        }
                    }

                    ++visible;
                    DrawFinding(settings, finding, severity, shared, personal, _fix?.Invoke(index));
                }

                if (visible == 0 && hidden == 0 && _clear != null)
                {
                    Add(new HelpBox(_clear, HelpBoxMessageType.Info));
                }

                if (0 < hidden)
                {
                    VisualElement row = DxAudioInspectorUi.Row();
                    DxAudioInspectorUi.AddText(row, $"{hidden} silenced");
                    row.Add(
                        new Button(() =>
                        {
                            _showSilenced = !_showSilenced;
                            Draw();
                        })
                        {
                            text = _showSilenced ? "Hide" : "Show",
                        }
                    );
                    Add(row);
                }
            }

            private void DrawFinding(DxAudioProblemSettings settings, Finding finding, Severity severity, bool shared, bool personal, Action fix)
            {
                HelpBoxMessageType icon =
                    severity == Severity.Error ? HelpBoxMessageType.Error
                    : severity == Severity.Warn ? HelpBoxMessageType.Warning
                    : HelpBoxMessageType.Info;
                string silenced =
                    shared && personal ? " (shared and personal silence)"
                    : shared ? " (shared silence)"
                    : personal ? " (personal silence)"
                    : string.Empty;
                Add(new HelpBox(finding.Label + silenced, icon));
                if (fix != null)
                {
                    Button button = new(fix) { text = "Fix", tooltip = "Go to the source of this problem." };
                    button.style.alignSelf = Align.FlexStart;
                    Add(button);
                }

                Foldout options = new() { text = shared || personal ? "Manage Silence" : "Silence", value = false };
                if (DxAudioProblemSettings.TryAssetId(finding, out string guid, out long localId))
                {
                    if (shared)
                    {
                        options.Add(
                            new Button(() =>
                            {
                                settings.RemoveSilence(finding.Rule, guid, localId);
                                settings.Persist();
                            })
                            {
                                text = "Restore Shared Finding",
                            }
                        );
                    }
                    else
                    {
                        TextField reason = new("Reason");
                        Button save = new(() =>
                        {
                            if (settings.TrySilence(finding, reason.value))
                            {
                                settings.Persist();
                            }
                        })
                        {
                            text = "Silence for This Asset",
                        };
                        save.SetEnabled(false);
                        reason.RegisterValueChangedCallback(evt => save.SetEnabled(!string.IsNullOrWhiteSpace(evt.newValue)));
                        options.Add(reason);
                        options.Add(save);
                    }
                }

                options.Add(
                    new Button(() =>
                    {
                        DxAudioProblemSettings.SetPersonalSilence(finding.Rule, !personal);
                    })
                    {
                        text = personal ? "Show This Rule for Me" : "Hide This Rule for Me",
                    }
                );
                options.Add(new Button(() => SettingsService.OpenProjectSettings(DxAudioProblemSettings.SettingsPath)) { text = "Change Rule Severity" });
                Add(options);
            }
        }
    }
}
