namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;
    using WallstopStudios.DxAudio.Editor;

    public sealed class DspAuditionProcessorTests
    {
        [Test]
        public void DryAuditionMatchesTheLatencyOfWetPlayback()
        {
            DspAuditionProcessor processor = new(1_000, 8, 1);
            DspChain chain = new();
            chain.Add(new DspLimiter { LookaheadSeconds = 0.005f });
            Assert.That(chain.TryPrepare(1_000, 8, 1), Is.True);
            processor.Publish(new DspAuditionProcessor.Revision(1, new[] { new DspAuditionProcessor.Path(8, 1, chain, null) }));
            processor.SetDry(true);

            float[] impulse = { 0.125f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
            processor.Process(impulse, 1);

            Assert.That(impulse, Is.EqualTo(new[] { 0f, 0f, 0f, 0f, 0f, 0.125f, 0f, 0f }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CueAuditionAlignsDryAndWetSignalsAcrossBothDspStages(bool dry)
        {
            DspChain chain = new();
            chain.Add(new DspLimiter { LookaheadSeconds = 0.002f });
            Assert.That(chain.TryPrepare(1_000, 8, 1), Is.True);
            DspChain branch = new();
            branch.Add(new DspLimiter { LookaheadSeconds = 0.003f });
            DspGraph graph = new();
            Assert.That(graph.TryAddNode(branch, out int endpoint), Is.True);
            Assert.That(graph.TryPrepare(1_000, 8, 1, endpoint, endpoint), Is.True);

            DspAuditionProcessor processor = new(1_000, 8, 1);
            processor.Publish(new DspAuditionProcessor.Revision(1, new[] { new DspAuditionProcessor.Path(8, 1, chain, graph) }));
            processor.SetDry(dry);
            float[] impulse = { 0.125f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
            processor.Process(impulse, 1);

            Assert.That(impulse, Is.EqualTo(new[] { 0f, 0f, 0f, 0f, 0f, 0.125f, 0f, 0f }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CueLowPassShapesTheSignalBeforeDryAndWetPathsSplit(bool dry)
        {
            const int Rate = 48_000;
            const int Block = 64;
            DspChain chain = new();
            chain.Add(new DspLimiter { LookaheadSeconds = 0f, Ceiling = 0.1f });
            Assert.That(chain.TryPrepare(Rate, Block, 1), Is.True);
            DspFilter lowPass = new() { FrequencyHz = 1_000f };
            Assert.That(lowPass.TryPrepare(Rate, Block, 1), Is.True);
            DspAuditionProcessor processor = new(Rate, Block, 1);
            processor.Publish(new DspAuditionProcessor.Revision(1, new[] { new DspAuditionProcessor.Path(Block, 1, chain, null, lowPass) }));
            processor.SetDry(dry);
            float[] impulse = new float[Block];
            impulse[0] = 0.5f;
            float[] expected = (float[])impulse.Clone();
            DspFilter direct = new() { FrequencyHz = 1_000f };
            Assert.That(direct.TryPrepare(Rate, Block, 1), Is.True);
            direct.Process(expected, 0, Block, 1);

            processor.Process(impulse, 1);

            Assert.That(impulse, Is.EqualTo(expected));
            Assert.That(impulse[0], Is.InRange(0.001f, 0.01f));
        }

        [Test]
        public void TheMonitorAttenuatesTheProcessedOutputWithoutChangingTheLimiterInput()
        {
            DspAuditionProcessor processor = new(1_000, 8, 1);
            DspChain chain = new();
            chain.Add(new DspLimiter { LookaheadSeconds = 0f, Ceiling = 0.2f });
            Assert.That(chain.TryPrepare(1_000, 8, 1), Is.True);
            processor.Publish(new DspAuditionProcessor.Revision(1, new[] { new DspAuditionProcessor.Path(8, 1, chain, null) }));
            processor.SetMonitor(0.25f);
            float[] block = Constant(8);

            processor.Process(block, 1);

            Assert.That(processor.Peak, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(block[0], Is.EqualTo(0.05f).Within(0.0001f));

            // A one-shot may finish before the next editor poll.
            processor.Process(new float[8], 1);
            Assert.That(processor.Peak, Is.Zero);
            Assert.That(processor.PeakSinceStart, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void ADspRevisionCrossfadesFromTheOutputAlreadyPlaying()
        {
            DspAuditionProcessor processor = new(1_000, 32, 1);
            processor.Publish(Revision(number: 1, branches: 1));
            processor.Process(Constant(32), 1);
            processor.Publish(Revision(number: 2, branches: 2));
            float[] block = Constant(32);

            processor.Process(block, 1);

            Assert.That(block[0], Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(block[10], Is.EqualTo(0.375f).Within(0.0001f));
            Assert.That(block[20], Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(processor.ActiveRevision, Is.EqualTo(2));
        }

        [Test]
        public void AnInvalidDraftDiscardsAQueuedRevisionButKeepsTheSoundingRevision()
        {
            DspAuditionProcessor processor = new(1_000, 32, 1);
            processor.Publish(Revision(number: 1, branches: 1));
            processor.Process(Constant(32), 1);
            processor.Publish(Revision(number: 2, branches: 2));
            processor.Publish(null);
            float[] block = Constant(32);

            processor.Process(block, 1);

            Assert.That(block, Is.EqualTo(Constant(32)));
            Assert.That(processor.ActiveRevision, Is.EqualTo(1));
        }

        [Test]
        public void ARevisionThatChangesLatencyDipsAndResumesWithPrimedInput()
        {
            DspAuditionProcessor processor = new(1_000, 32, 1);
            processor.Publish(Revision(number: 1, branches: 1));
            processor.Process(Constant(32), 1);
            processor.Publish(DelayedRevision(number: 2, lookahead: 0.005f));
            float[] block = Constant(32);

            processor.Process(block, 1);

            Assert.That(block[4], Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(block[9], Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(block[10], Is.Zero);
            Assert.That(block[11], Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(block[15], Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(processor.ActiveRevision, Is.EqualTo(2));
        }

        [Test]
        public void AnInvalidDraftCancelsARevisionThatIsStillPriming()
        {
            DspAuditionProcessor processor = new(1_000, 32, 1);
            processor.Publish(Revision(number: 1, branches: 1));
            processor.Process(Constant(32), 1);
            processor.Publish(DelayedRevision(number: 2, lookahead: 0.04f));
            processor.Process(Constant(32), 1);
            processor.Publish(null);
            float[] block = Constant(32);

            processor.Process(block, 1);

            Assert.That(block, Is.EqualTo(Constant(32)));
            Assert.That(processor.ActiveRevision, Is.EqualTo(1));
        }

        private static DspAuditionProcessor.Revision DelayedRevision(int number, float lookahead)
        {
            DspChain chain = new();
            chain.Add(new DspLimiter { LookaheadSeconds = lookahead });
            Assert.That(chain.TryPrepare(1_000, 32, 1), Is.True);
            return new DspAuditionProcessor.Revision(number, new[] { new DspAuditionProcessor.Path(32, 1, chain, null) });
        }

        private static DspAuditionProcessor.Revision Revision(int number, int branches)
        {
            DspGraph graph = new();
            Assert.That(graph.TryAddNode(new DspChain(), out int input), Is.True);
            Assert.That(graph.TryAddNode(new DspChain(), out int output), Is.True);
            for (int index = 0; index < branches; ++index)
            {
                Assert.That(graph.TryAddNode(new DspChain(), out int branch), Is.True);
                Assert.That(graph.TryAddEdge(input, branch), Is.True);
                Assert.That(graph.TryAddEdge(branch, output), Is.True);
            }

            Assert.That(graph.TryPrepare(1_000, 32, 1, input, output), Is.True);
            return new DspAuditionProcessor.Revision(number, new[] { new DspAuditionProcessor.Path(32, 1, null, graph) });
        }

        private static float[] Constant(int length)
        {
            float[] block = new float[length];
            for (int index = 0; index < block.Length; ++index)
            {
                block[index] = 0.25f;
            }

            return block;
        }
    }
}
