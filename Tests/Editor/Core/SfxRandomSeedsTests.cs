namespace WallstopStudios.DxAudio.Tests.Editor
{
    using NUnit.Framework;

    public sealed class SfxRandomSeedsTests
    {
        [Test]
        public void Fnv1a64MatchesThePublishedVectors()
        {
            Assert.That(SfxRandomSeeds.Fnv1a64(string.Empty), Is.EqualTo(0xCBF29CE484222325UL), "the empty path hashes to the offset basis");
            Assert.That(SfxRandomSeeds.Fnv1a64("a"), Is.EqualTo(0xAF63DC4C8601EC8CUL));
            Assert.That(SfxRandomSeeds.Fnv1a64("foobar"), Is.EqualTo(0x85944171F73967E8UL));
            Assert.That(SfxRandomSeeds.Fnv1a64(null), Is.EqualTo(0xCBF29CE484222325UL), "a null path names the empty path");
        }

        [Test]
        public void DerivingIsDeterministicAndAnyKeyChangeMovesTheSeed()
        {
            ulong derived = SfxRandomSeeds.Derive(7UL, 100UL);
            Assert.That(SfxRandomSeeds.Derive(7UL, 100UL), Is.EqualTo(derived), "the same inputs agree");
            Assert.That(SfxRandomSeeds.Derive(8UL, 100UL), Is.Not.EqualTo(derived), "the root seed matters");
            Assert.That(SfxRandomSeeds.Derive(7UL, 101UL), Is.Not.EqualTo(derived), "the key matters");
        }

        [Test]
        public void DerivingFoldsKeysInThePublishedOrder()
        {
            // Vectors computed from the published formula, spec 02: the state starts at mix64 of the root, and
            // each key folds once as mix64(state + Gamma * (k + 1)). They pin the fold order against an
            // independent computation.
            Assert.That(SfxRandomSeeds.Derive(7UL, 100UL), Is.EqualTo(0x7B49F1065D5FADA7UL), "the one-key fold");
            Assert.That(SfxRandomSeeds.Derive(7UL, 100UL, 1UL), Is.EqualTo(0xF70A9BF473A46594UL), "the two-key fold");
            Assert.That(SfxRandomSeeds.NodeSeed(7UL, "graph/bed", 2UL), Is.EqualTo(0x7627EA94936FD162UL), "a node seed");
            Assert.That(SfxRandomSeeds.NodeSeed(0UL, "impacts/hit", 0UL), Is.EqualTo(0x5D704D09B56EA1B5UL), "a second node seed");
        }

        [TestCase(0UL, "impacts/hit", 0UL, TestName = "The first instance of a path")]
        [TestCase(0UL, "impacts/hit", 1UL, TestName = "A second instance of the same path")]
        [TestCase(1UL, "impacts/hit", 0UL, TestName = "Another graph seed")]
        [TestCase(0UL, "impacts/hit-loud", 0UL, TestName = "Another path")]
        public void NodeSeedChangesWithEveryInput(ulong graphSeed, string nodePath, ulong instanceIndex)
        {
            ulong seed = SfxRandomSeeds.NodeSeed(graphSeed, nodePath, instanceIndex);

            Assert.That(SfxRandomSeeds.NodeSeed(graphSeed ^ 1UL, nodePath, instanceIndex), Is.Not.EqualTo(seed), "the graph seed matters");
            Assert.That(SfxRandomSeeds.NodeSeed(graphSeed, nodePath + "-edited", instanceIndex), Is.Not.EqualTo(seed), "the path matters");
            Assert.That(SfxRandomSeeds.NodeSeed(graphSeed, nodePath, instanceIndex + 1UL), Is.Not.EqualTo(seed), "the instance index matters");
        }

        [Test]
        public void Mix64IsDeterministicAndSpreadsSmallChanges()
        {
            ulong mixed = SfxRandomSeeds.Mix64(0UL);
            ulong known = SfxRandomSeeds.Mix64(0x9E3779B97F4A7C15UL);
            Assert.That(SfxRandomSeeds.Mix64(1UL), Is.Not.EqualTo(mixed), "neighbouring inputs land apart");
            Assert.That(SfxRandomSeeds.Mix64(0x9E3779B97F4A7C15UL), Is.EqualTo(known), "the same input agrees");
        }
    }
}
