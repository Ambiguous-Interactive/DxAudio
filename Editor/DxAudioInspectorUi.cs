namespace WallstopStudios.DxAudio.Editor
{
    using System.Collections.Generic;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

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

        /// <summary>Adds one warning per finding, or one clear line when there are none.</summary>
        internal static void AddFindings(VisualElement parent, IReadOnlyList<string> findings, string clear)
        {
            if (findings.Count == 0)
            {
                parent.Add(new HelpBox(clear, HelpBoxMessageType.Info));
                return;
            }

            for (int index = 0; index < findings.Count; ++index)
            {
                parent.Add(new HelpBox(findings[index], HelpBoxMessageType.Warning));
            }
        }

        /// <summary>Adds one row per stage of <paramref name="asset" />, in order, with any live gain reduction.</summary>
        /// <remarks>
        /// Order is the sound, so the rows are the authored list, top first. A chain that a running voice plays also
        /// shows how far each stage holds the signal down; that reading is diagnostics, so it may allocate.
        /// </remarks>
        internal static void AddChainRows(VisualElement parent, DspChainAsset asset)
        {
            if (asset == null)
            {
                parent.Add(new Label("No chain: voices play their clip untouched."));
                return;
            }

            IReadOnlyList<DspEffectAsset> effects = asset.Effects;
            if (effects.Count == 0)
            {
                parent.Add(new Label("The chain holds no effects, so it plays the clip untouched."));
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
                parent.Add(new Label("A running voice shows its live gain reduction beside each stage."));
            }
        }

        /// <summary>Rebuilds <paramref name="rows" /> while audio plays, because a live reading moves with no edit.</summary>
        internal static void RefreshWhilePlaying(VisualElement rows, DspChainAsset asset)
        {
            rows.schedule.Execute(() =>
                {
                    // The rows are rebuilt on an edit anyway. Only a running voice adds a reading with no edit.
                    if (Application.isPlaying)
                    {
                        rows.Clear();
                        AddChainRows(rows, asset);
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

            AddFindings(parent, asset.FindProblems(), "The graph is ready to play.");
            if (!asset.TryCreateGraph(out DspGraph graph))
            {
                return;
            }

            int sampleRate = DspGraphAsset.ReferenceSampleRate;
            bool prepared = graph.TryPrepare(
                sampleRate,
                DspGraphAsset.ReferenceBlockFrames,
                DspGraphAsset.ReferenceChannels,
                asset.IndexOfNode(asset.InputNode),
                asset.IndexOfNode(asset.OutputNode)
            );

            IReadOnlyList<DspGraphNode> nodes = asset.Nodes;
            for (int index = 0; index < nodes.Count; ++index)
            {
                DspGraphNode node = nodes[index];
                VisualElement row = Row();
                AddText(row, node == null ? $"Node {index + 1}: empty" : $"Node \"{node.Name}\": {Describe(node)}");
                if (prepared && graph.TryGetNodeLatency(index, out int latency))
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

            if (prepared)
            {
                parent.Add(new Label($"The graph delays the signal {graph.LatencySamples} samples at {sampleRate} Hz."));
            }
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
            for (int index = 0; index < hosts.Length; ++index)
            {
                AudioFilterHost host = hosts[index];
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
            for (int index = 0; index < buses.Length; ++index)
            {
                AudioMixBus bus = buses[index];
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
    }
}
