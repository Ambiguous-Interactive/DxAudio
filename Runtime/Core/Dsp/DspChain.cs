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
        private bool _prepared;

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
                foreach (DspEffect effect in _effects)
                {
                    latency += effect.LatencySamples;
                }

                return latency;
            }
        }

        /// <summary>How far the chain holds the signal down now, in decibels; zero when nothing turns it down.</summary>
        /// <remarks>
        /// The stages run in series, so their reductions add: two stages of 3 dB hold the signal 6 dB down. Makeup gain
        /// is not counted, because it is a lift a later stage applies rather than a reduction.
        /// </remarks>
        public float GainReductionDecibels
        {
            get
            {
                float held = 0f;
                for (int i = 0; i < _effects.Count; i++)
                {
                    held += _effects[i].GainReductionDecibels;
                }

                return held;
            }
        }

        /// <summary>Appends <paramref name="effect"/> to the end of the chain.</summary>
        /// <remarks>
        /// A stage added after <see cref="TryPrepare"/> is prepared at once with the chain's layout. Otherwise it would
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
                _ = effect.TryPrepare(_sampleRate, _maxBlockSize, _channels);
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
        /// <remarks>
        /// A chain that refuses a layout, or that was never prepared, passes every block through. The effects may hold
        /// buffers from an earlier prepare, so without this answer a refused chain would keep running on them.
        /// </remarks>
        /// <returns><see langword="false"/> when a part of the layout is not positive; the chain then runs nothing.</returns>
        public bool TryPrepare(int sampleRate, int maxBlockSize, int channels)
        {
            _prepared = false;
            if (sampleRate <= 0 || maxBlockSize <= 0 || channels <= 0)
            {
                return false;
            }

            _sampleRate = sampleRate;
            _maxBlockSize = maxBlockSize;
            _channels = channels;
            // Indexed on purpose. A stage may append to this chain from its own Prepare, and an enumerator would
            // throw; the count is read per pass so the appended stage is prepared too in the same call.
            for (int i = 0; i < _effects.Count; i++)
            {
                _ = _effects[i].TryPrepare(sampleRate, maxBlockSize, channels);
            }

            _prepared = true;
            return true;
        }

        /// <summary>Runs the block through every stage, in order.</summary>
        /// <remarks>A chain that is not prepared passes the block through untouched.</remarks>
        public void Process(float[] buffer, int offset, int count, int channels)
        {
            if (!_prepared)
            {
                return;
            }

            // Indexed for the same reason as TryPrepare: a stage that appends while processing must not make the
            // audio thread throw. The new stage runs in this block.
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
