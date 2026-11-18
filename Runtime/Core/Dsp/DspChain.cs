namespace WallstopStudios.DxAudio
{
    using System;
    using System.Collections.Generic;

    /// <summary>Effects in the order the signal meets them, top of the list first.</summary>
    /// <remarks>
    /// Order is the sound: distortion before a compressor is driven and then controlled, while the reverse
    /// squashes the quiet part before the drive lifts it again. So the chain is a list, not a set, and the
    /// editor shows it in this order.
    ///
    /// A chain is built and prepared before it processes audio, as the same rule the effects follow. Adding or
    /// removing an effect while the audio thread is inside <see cref="Process"/> would reorder a list mid-block.
    /// </remarks>
    public sealed class DspChain
    {
        private readonly List<DspEffect> _effects;
        private int _sampleRate;
        private int _maxBlockSize;
        private int _channels;

        public DspChain()
        {
            _effects = new List<DspEffect>();
        }

        public int Count => _effects.Count;

        public DspEffect this[int index] => _effects[index];

        /// <summary>The delay of every stage together, in samples.</summary>
        public int LatencySamples
        {
            get
            {
                int latency = 0;
                for (int i = 0; i < _effects.Count; i++)
                {
                    latency += _effects[i].LatencySamples;
                }

                return latency;
            }
        }

        /// <summary>Appends <paramref name="effect"/> to the end of the chain.</summary>
        /// <remarks>
        /// A stage added after <see cref="Prepare"/> is prepared at once with the chain's layout. Otherwise it would
        /// sit in the list doing nothing, and its latency would be missing from <see cref="LatencySamples"/>.
        /// </remarks>
        public void Add(DspEffect effect)
        {
            if (effect == null)
            {
                throw new ArgumentNullException(nameof(effect));
            }

            if (0 < _sampleRate)
            {
                effect.Prepare(_sampleRate, _maxBlockSize, _channels);
            }

            _effects.Add(effect);
        }

        /// <summary>Removes the first occurrence of <paramref name="effect"/>.</summary>
        /// <returns><see langword="false"/> when the chain does not hold it.</returns>
        public bool Remove(DspEffect effect)
        {
            return effect != null && _effects.Remove(effect);
        }

        public void Clear()
        {
            _effects.Clear();
        }

        /// <summary>Prepares every effect for the same rate, block size and channel count.</summary>
        public void Prepare(int sampleRate, int maxBlockSize, int channels)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (maxBlockSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBlockSize));
            }

            if (channels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            _sampleRate = sampleRate;
            _maxBlockSize = maxBlockSize;
            _channels = channels;
            for (int i = 0; i < _effects.Count; i++)
            {
                _effects[i].Prepare(sampleRate, maxBlockSize, channels);
            }
        }

        /// <summary>Runs the block through every stage, in order.</summary>
        public void Process(float[] buffer, int offset, int count, int channels)
        {
            for (int i = 0; i < _effects.Count; i++)
            {
                _effects[i].Process(buffer, offset, count, channels);
            }
        }

        /// <summary>Resets every effect, so a pooled voice starts clean.</summary>
        public void Reset()
        {
            for (int i = 0; i < _effects.Count; i++)
            {
                _effects[i].Reset();
            }
        }
    }
}
