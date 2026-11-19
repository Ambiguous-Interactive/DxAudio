namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Keeps a graph asset honest: it builds what its nodes and edges say, and it keeps them in a file.</summary>
    /// <remarks>
    /// A graph asset is a recipe. These tests build one in code, read it back the way a domain reload leaves it, and
    /// prove the refusals that a cue would otherwise meet as a silent pass-through.
    /// </remarks>
    public sealed class DspGraphAssetTests
    {
        [Test]
        public void AGraphAssetBuildsANodePerAuthoredNodeAndAFreshChainForEach()
        {
            using MadeObjects made = new();
            DspChainAsset shared = made.Track(DspChainAsset.Create(new DspEffectAsset[] { made.Track(DspDistortionAsset.Create()) }));
            DspGraphAsset asset = made.Track(
                DspGraphAsset.Create(
                    new[] { new DspGraphNode("in", null), new DspGraphNode("left", shared), new DspGraphNode("right", shared), new DspGraphNode("out", null) },
                    new[] { new DspGraphEdge("in", "left"), new DspGraphEdge("in", "right"), new DspGraphEdge("left", "out"), new DspGraphEdge("right", "out") },
                    "in",
                    "out"
                )
            );

            Assert.That(asset.TryCreateGraph(out DspGraph graph), Is.True);
            Assert.That(graph.NodeCount, Is.EqualTo(4));
            Assert.That(graph.EdgeCount, Is.EqualTo(4));
            Assert.That(asset.IndexOfNode("right"), Is.EqualTo(2));
            Assert.That(asset.IndexOfNode("gone"), Is.EqualTo(-1));
            Assert.That(
                graph.TryPrepare(48_000, 64, 1, asset.IndexOfNode(asset.InputNode), asset.IndexOfNode(asset.OutputNode)),
                Is.True,
                "two nodes may name one chain asset, because each node builds its own chain"
            );
        }

        [Test]
        public void AnEdgeToANameNoNodeHoldsIsRefused()
        {
            using MadeObjects made = new();
            DspGraphAsset asset = made.Track(
                DspGraphAsset.Create(new[] { new DspGraphNode("in", null), new DspGraphNode("out", null) }, new[] { new DspGraphEdge("in", "sidechain") }, "in", "out")
            );

            Assert.That(asset.TryCreateGraph(out DspGraph graph), Is.False);
            Assert.That(graph == null, Is.True);
            Assert.That(string.Join(" ", asset.FindProblems()), Does.Contain("sidechain"), "the finding names the edge a person has to fix");
        }

        [Test]
        public void TwoNodesMayNotShareAName()
        {
            using MadeObjects made = new();
            DspGraphAsset asset = made.Track(
                DspGraphAsset.Create(new[] { new DspGraphNode("in", null), new DspGraphNode("in", null) }, new[] { new DspGraphEdge("in", "in") }, "in", "in")
            );

            Assert.That(asset.TryCreateGraph(out DspGraph graph), Is.False);
            Assert.That(graph == null, Is.True);
            Assert.That(string.Join(" ", asset.FindProblems()), Does.Contain("More than one node"));
        }

        [Test]
        public void ANodeOffThePathIsReportedAndRefused()
        {
            using MadeObjects made = new();
            DspGraphAsset asset = made.Track(
                DspGraphAsset.Create(
                    new[] { new DspGraphNode("in", null), new DspGraphNode("out", null), new DspGraphNode("orphan", null) },
                    new[] { new DspGraphEdge("in", "out") },
                    "in",
                    "out"
                )
            );

            Assert.That(asset.TryCreateGraph(out DspGraph graph), Is.True, "the shape builds; the prepare step is what refuses an orphan");
            Assert.That(graph.TryPrepare(48_000, 64, 1, 0, 1), Is.False);
            Assert.That(string.Join(" ", asset.FindProblems()), Does.Contain("orphan"), "the finding names the node that would never run");
        }

        [Test]
        public void AGraphOverTheNodeLimitIsReported()
        {
            using MadeObjects made = new();
            DspGraphNode[] nodes = new DspGraphNode[DspGraph.MaximumNodes + 1];
            for (int index = 0; index < nodes.Length; ++index)
            {
                nodes[index] = new DspGraphNode($"n{index}", null);
            }

            DspGraphAsset asset = made.Track(DspGraphAsset.Create(nodes, new DspGraphEdge[0], "n0", $"n{DspGraph.MaximumNodes}"));

            Assert.That(string.Join(" ", asset.FindProblems()), Does.Contain("at most"));
            Assert.That(asset.TryCreateGraph(out DspGraph graph), Is.False, "a graph over the node limit cannot build");
            Assert.That(graph == null, Is.True);
        }

        [Test]
        public void AGraphOverTheMemoryBudgetIsReported()
        {
            using MadeObjects made = new();
            // Six branches of unequal length: every edge into the output waits for the longest one, so their lines
            // together ask for more than the 2 MiB a prepared graph may reserve.
            int[] stages = { 16, 12, 8, 4, 2, 1 };
            DspGraphNode[] nodes = new DspGraphNode[stages.Length + 2];
            DspGraphEdge[] edges = new DspGraphEdge[stages.Length * 2];
            nodes[0] = new DspGraphNode("in", null);
            for (int branch = 0; branch < stages.Length; ++branch)
            {
                DspEffectAsset[] effects = new DspEffectAsset[stages[branch]];
                for (int index = 0; index < effects.Length; ++index)
                {
                    effects[index] = made.Track(DspPitchShifterAsset.Create(semitones: 0f, windowSeconds: 0.2f));
                }

                string name = $"branch{branch}";
                nodes[branch + 1] = new DspGraphNode(name, made.Track(DspChainAsset.Create(effects)));
                edges[branch * 2] = new DspGraphEdge("in", name);
                edges[(branch * 2) + 1] = new DspGraphEdge(name, "out");
            }

            nodes[stages.Length + 1] = new DspGraphNode("out", null);
            DspGraphAsset asset = made.Track(DspGraphAsset.Create(nodes, edges, "in", "out"));

            Assert.That(string.Join(" ", asset.FindProblems()), Does.Contain("2 MiB"), "a graph no device layout this size can run is not ready");
        }

        [Test]
        public void ANullKeyAndANullContextStillBindToTheFirstFactory()
        {
            using MadeObjects made = new();
            // The call shape that a second Create overload would have made ambiguous. It compiling is the assertion.
            SfxCue cue = made.Track(SfxCue.Create(new[] { new SfxVariant(Clip(made)) }, null));
            MusicTrack track = made.Track(MusicTrack.Create(Clip(made), null));

            Assert.That(cue.Key == null, Is.True);
            Assert.That(track.Context == null, Is.True);
        }

        [Test]
        public void AReadyGraphHasNoProblems()
        {
            using MadeObjects made = new();
            DspGraphAsset asset = made.Track(
                DspGraphAsset.Create(new[] { new DspGraphNode("in", null), new DspGraphNode("out", null) }, new[] { new DspGraphEdge("in", "out") }, "in", "out")
            );

            Assert.That(asset.FindProblems(), Is.Empty);
        }

        [Test]
        public void AGraphAssetKeepsItsNodesEdgesAndChainReferencesInTheFile()
        {
            string folder = AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder("Assets", "DxAudioGraphTest"));
            try
            {
                DspChainAsset branch = ScriptableObject.CreateInstance<DspChainAsset>();
                string branchPath = folder + "/Branch.asset";
                AssetDatabase.CreateAsset(branch, branchPath);
                DspGraphAsset authored = DspGraphAsset.Create(
                    new[] { new DspGraphNode("in", null), new DspGraphNode("out", branch) },
                    new[] { new DspGraphEdge("in", "out") },
                    "in",
                    "out"
                );
                string path = folder + "/Authored.asset";
                AssetDatabase.CreateAsset(authored, path);
                AssetDatabase.SaveAssets();
                // Dropping the object and reading the file back is what a domain reload does to an authored asset.
                Resources.UnloadAsset(branch);
                Resources.UnloadAsset(authored);

                DspGraphAsset loaded = AssetDatabase.LoadAssetAtPath<DspGraphAsset>(path);
                Assert.That(loaded != null);
                Assert.That(loaded, Is.Not.SameAs(authored), "the file, not the object still in memory, is what a reload reads");
                Assert.That(loaded.NodeCount, Is.EqualTo(2));
                Assert.That(loaded.EdgeCount, Is.EqualTo(1));
                Assert.That(loaded.InputNode, Is.EqualTo("in"));
                Assert.That(loaded.OutputNode, Is.EqualTo("out"));
                Assert.That(loaded.Nodes[1].Chain != null, Is.True, "a node's chain reference survives the file");
                Assert.That(loaded.Nodes[1].Chain == AssetDatabase.LoadAssetAtPath<DspChainAsset>(branchPath));
                Assert.That(loaded.TryCreateGraph(out DspGraph graph), Is.True);
                Assert.That(graph.TryPrepare(48_000, 64, 1, loaded.IndexOfNode("in"), loaded.IndexOfNode("out")), Is.True);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static AudioClip Clip(MadeObjects made)
        {
            return made.Track(AudioClip.Create("test clip", 400, 1, 8_000, stream: false));
        }
    }
}
