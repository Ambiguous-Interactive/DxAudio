namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>One chain asset prepared for every block shape one voice or one mix bus can be handed.</summary>
    /// <remarks>
    /// Narrower shapes are prepared too, because a filter block can carry fewer channels than the device: a chain
    /// prepared only for the output layout would pass a mono block through and the sound would play unshaped, which is
    /// the one failure that is invisible by ear until the mix is compared.
    ///
    /// The clear is a generation, not a flag, because a swap races the audio thread: a block that reads a request must
    /// not spend a newer one written while it ran, or the next play would start on the delay line the last one left.
    /// </remarks>
    internal sealed class DspChainSlot
    {
        /// <summary>The asset every chain in this slot was built from.</summary>
        public DspChainAsset Asset { get; }

        /// <summary>The play that last asked for a clear. Written on the main thread only.</summary>
        public int ClearGeneration;

        /// <summary>The generation this slot has already cleared for. Written on the audio thread only.</summary>
        public int ClearedGeneration;

        private DspChain[] _chains = Array.Empty<DspChain>();

        public DspChainSlot(DspChainAsset asset)
        {
            Asset = asset;
        }

        /// <summary>Asks for this slot to be cleared before it next processes a block.</summary>
        public void RequestClear()
        {
            ClearGeneration++;
        }

        /// <summary>Whether a clear is owed to this slot, recording it as taken when one is.</summary>
        public bool ClearBeforeNextBlock()
        {
            int requested = ClearGeneration;
            if (ClearedGeneration == requested)
            {
                return false;
            }

            ClearedGeneration = requested;
            return true;
        }

        /// <summary>Builds one chain for every block shape up to <paramref name="shapes" /> channels.</summary>
        public void Prepare(int sampleRate, int blockSize, int shapes)
        {
            _chains = new DspChain[shapes];
            for (int index = 0; index < shapes; index++)
            {
                DspChain chain = Asset.CreateChain();
                _ = chain.TryPrepare(sampleRate, blockSize, index + 1);
                _chains[index] = chain;
            }
        }

        /// <summary>The chain prepared for <paramref name="channels" />, or null when that shape is wider than the layout.</summary>
        public DspChain For(int channels)
        {
            if (channels <= 0 || _chains.Length < channels)
            {
                return null;
            }

            return _chains[channels - 1];
        }

        /// <summary>Clears every block shape this slot prepared, so a reused voice starts silent at any width.</summary>
        /// <remarks>
        /// A voice can run one block at one width and the next play's first block at another. Resetting only the width
        /// in front of the block would leave every other prepared shape holding the last play's delay line.
        /// </remarks>
        public void Reset()
        {
            foreach (DspChain chain in _chains)
            {
                chain.Reset();
            }
        }
    }
}
