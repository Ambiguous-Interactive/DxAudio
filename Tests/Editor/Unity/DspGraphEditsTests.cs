namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DxAudio.Editor;

    public sealed class DspGraphEditsTests
    {
        private static DspGraphAsset Graph(MadeObjects made)
        {
            return made.Track(DspGraphAsset.Create(new[] { new DspGraphNode("In", null), new DspGraphNode("Out", null) }, new[] { new DspGraphEdge("In", "Out") }, "In", "Out"));
        }

        [Test]
        public void RenamingAnEndpointUpdatesItsReferencesInOneUndo()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made);
            DspGraphEdits edits = new(graph);

            Assert.That(edits.TryRename(0, "Source", out string reason), Is.True, reason);
            Assert.That(graph.InputNode, Is.EqualTo("Source"));
            Assert.That(graph.Edges[0].From, Is.EqualTo("Source"));

            Undo.PerformUndo();
            Assert.That(graph.InputNode, Is.EqualTo("In"));
            Assert.That(graph.Edges[0].From, Is.EqualTo("In"));
        }

        [Test]
        public void ARejectedCycleLeavesTheOriginalEdgeIntact()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made);
            DspGraphEdits edits = new(graph);

            Assert.That(edits.CanConnect(1, 0, out string previewReason), Is.False);
            Assert.That(previewReason, Does.Contain("cycle"));
            Assert.That(edits.TryConnect(1, 0, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo(previewReason));
            Assert.That(graph.EdgeCount, Is.EqualTo(1));
        }

        [Test]
        public void InsertingAChainReplacesTheWireAndPreservesASignalPath()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made);
            DspGraphEdits edits = new(graph);

            Assert.That(edits.TryInsert(0, "Insert", null, Vector2.zero, out string reason), Is.True, reason);
            Assert.That(graph.FindProblems(), Is.Empty);
            Assert.That(graph.Edges[0].To, Is.EqualTo("Insert"));
            Assert.That(graph.Edges[1].From, Is.EqualTo("Insert"));
            Assert.That(graph.Nodes[2].HasEditorPosition, Is.True);

            Undo.PerformUndo();
            Assert.That(graph.NodeCount, Is.EqualTo(2));
            Assert.That(graph.EdgeCount, Is.EqualTo(1));
            Assert.That(graph.Edges[0].To, Is.EqualTo("Out"));

            Undo.PerformRedo();
            Assert.That(graph.Nodes[2].Name, Is.EqualTo("Insert"));
            Assert.That(graph.EdgeCount, Is.EqualTo(2));
        }

        [Test]
        public void ReconnectingAWireRestoresItsOriginalEndpointInOneUndo()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made);
            DspGraphEdits edits = new(graph);
            Assert.That(edits.TryAdd("Branch", null, Vector2.zero, out string addError), Is.True, addError);

            Assert.That(edits.TrySetEdge(0, 0, 2, out string edgeError), Is.True, edgeError);
            Assert.That(graph.Edges[0].To, Is.EqualTo("Branch"));

            Undo.PerformUndo();
            Assert.That(graph.NodeCount, Is.EqualTo(3));
            Assert.That(graph.Edges[0].To, Is.EqualTo("Out"));

            Undo.PerformRedo();
            Assert.That(graph.Edges[0].To, Is.EqualTo("Branch"));
        }

        [Test]
        public void AmbiguousEdgesMustBeRepairedBeforeADuplicateCanBeRenamed()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = made.Track(
                DspGraphAsset.Create(
                    new[] { new DspGraphNode("In", null), new DspGraphNode("In", null), new DspGraphNode("Out", null) },
                    new[] { new DspGraphEdge("In", "Out") },
                    "In",
                    "Out"
                )
            );
            DspGraphEdits edits = new(graph);

            Assert.That(edits.TryRename(1, "Branch", out _), Is.False);
            Assert.That(graph.Nodes[1].Name, Is.EqualTo("In"));
        }

        [Test]
        public void AddingANodeCannotSilentlyAdoptADanglingConnection()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = made.Track(DspGraphAsset.Create(new[] { new DspGraphNode("In", null) }, new[] { new DspGraphEdge("In", "Missing") }, "In", "In"));
            DspGraphEdits edits = new(graph);

            Assert.That(edits.TryAdd("Missing", null, Vector2.zero, out _), Is.False);
            Assert.That(graph.NodeCount, Is.EqualTo(1));
            Assert.That(graph.Edges[0].To, Is.EqualTo("Missing"));
        }

        [Test]
        public void ClearingAmbiguousEndpointsAllowsExplicitRepair()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = made.Track(
                DspGraphAsset.Create(new[] { new DspGraphNode("Duplicate", null), new DspGraphNode("Duplicate", null) }, null, "Duplicate", "Duplicate")
            );
            DspGraphEdits edits = new(graph);

            Assert.That(edits.TrySetEndpoint(true, -1, out _), Is.True);
            Assert.That(edits.TrySetEndpoint(false, -1, out _), Is.True);
            Assert.That(edits.TryRename(1, "Renamed", out _), Is.True);
            Assert.That(graph.InputNode, Is.Empty);
            Assert.That(graph.OutputNode, Is.Empty);
        }

        [Test]
        public void ALayoutChangeSurvivesReloadWithoutChangingTheSignal()
        {
            using MadeObjects made = new();
            DspGraphAsset graph = Graph(made);
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/DxAudioGraphLayoutTest.asset");
            AssetDatabase.CreateAsset(graph, path);
            try
            {
                Vector2 position = new(20f, 35f);
                DspGraphEdits edits = new(graph);
                Assert.That(graph.Nodes[0].HasEditorPosition, Is.False);
                Assert.That(edits.TryMove(new Dictionary<int, Vector2> { [0] = position }, out _), Is.True);
                AssetDatabase.SaveAssetIfDirty(graph);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                DspGraphAsset loaded = AssetDatabase.LoadAssetAtPath<DspGraphAsset>(path);
                Assert.That(loaded.Nodes[0].HasEditorPosition, Is.True);
                Assert.That(loaded.Nodes[0].EditorPosition, Is.EqualTo(position));
                Assert.That(loaded.TryCreateGraph(out DspGraph dsp), Is.True);
                Assert.That(dsp.TryPrepare(48_000, 16, 1, 0, 1), Is.True);
                float[] signal = { 0.5f, -0.25f, 0f };
                dsp.Process(signal, 0, signal.Length, 1);
                Assert.That(signal, Is.EqualTo(new[] { 0.5f, -0.25f, 0f }));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
