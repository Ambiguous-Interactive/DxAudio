namespace WallstopStudios.DxAudio
{
    using System.Text;

    /// <summary>
    /// Seed derivation for the synthesis random streams: every node derives its own seed from the graph seed, its
    /// path and its instance index, so editing one module never changes another's output.
    /// </summary>
    /// <remarks>
    /// The mixer is the splitmix64 finalizer (Steele, Lea and Flood; also used by the xoshiro family), the path
    /// key is FNV-1a 64 (Fowler, Noll, Vo; Eastlake and Jones), both reimplemented from the published
    /// specifications. Derivation runs when a graph is prepared, never on the audio path.
    /// </remarks>
    public static class SfxRandomSeeds
    {
        /// <summary>The golden-ratio step used by splitmix64 and by the key mixing.</summary>
        public const ulong Gamma = 0x9E3779B97F4A7C15UL;

        private const ulong FnvOffsetBasis = 0xCBF29CE484222325UL;
        private const ulong FnvPrime = 0x100000001B3UL;

        /// <summary>
        /// The splitmix64 finalizer: a bijection over 64 bits that turns a small input change into a spread output.
        /// </summary>
        public static ulong Mix64(ulong value)
        {
            value += Gamma;
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }

        /// <summary>
        /// FNV-1a 64 over the UTF-8 bytes of <paramref name="text"/>. A null or empty path hashes to the offset
        /// basis, so a missing key still derives deterministically.
        /// </summary>
        public static ulong Fnv1a64(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return FnvOffsetBasis;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ulong hash = FnvOffsetBasis;
            foreach (byte value in bytes)
            {
                hash ^= value;
                hash *= FnvPrime;
            }

            return hash;
        }

        /// <summary>
        /// The one key fold from the published formula: mix64(state + Gamma * (k + 1)). Derivation folds keys with
        /// this step; hashing folds fields with it.
        /// </summary>
        public static ulong Fold(ulong state, ulong key)
        {
            return Mix64(state + (Gamma * (key + 1UL)));
        }

        /// <summary>Derives a seed from a root and one key, folding the key with the golden-ratio step.</summary>
        public static ulong Derive(ulong rootSeed, ulong key)
        {
            return Fold(Mix64(rootSeed), key);
        }

        /// <summary>
        /// Derives a seed from a root and two keys, folding each key in the published order: the state starts at
        /// mix64 of the root, and every key folds once without re-mixing the state in between.
        /// </summary>
        public static ulong Derive(ulong rootSeed, ulong firstKey, ulong secondKey)
        {
            return Fold(Fold(Mix64(rootSeed), firstKey), secondKey);
        }

        /// <summary>
        /// The node seed: derived from the graph seed, the node path and the instance index. The same graph seed
        /// and tree give the same streams on every platform; a renamed node re-seeds itself and nothing else.
        /// </summary>
        public static ulong NodeSeed(ulong graphSeed, string nodePath, ulong instanceIndex)
        {
            return Derive(graphSeed, Fnv1a64(nodePath), instanceIndex);
        }
    }
}
