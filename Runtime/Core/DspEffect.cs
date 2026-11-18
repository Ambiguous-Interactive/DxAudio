namespace WallstopStudios.DxAudio
{
    /// <summary>One stage of a signal chain: samples in, shaped samples out.</summary>
    /// <remarks>
    /// Buffers are interleaved, so a stereo block reads left, right, left, right. Effects shape the buffer in
    /// place, which keeps a chain free of scratch buffers and its audio path allocation-free.
    ///
    /// <see cref="Process"/> runs on the audio thread. It takes no lock, writes no log, and allocates nothing.
    /// A knob moved on the main thread arrives as a plain property read, so the worst a torn read can do is
    /// change one block's gain. <see cref="Reset"/> exists because voices are pooled: the next cue on a voice
    /// must not inherit the last cue's delay line or envelope.
    /// </remarks>
    public abstract class DspEffect
    {
        /// <summary>The name an inspector shows for this effect.</summary>
        public abstract string DisplayName { get; }

        /// <summary>How many samples of delay this effect adds, for aligning branches that run in parallel.</summary>
        public virtual int LatencySamples => 0;

        /// <summary>Reserves whatever the effect needs before it processes audio.</summary>
        /// <remarks>Allocation belongs here, never in <see cref="Process"/>.</remarks>
        public abstract void Prepare(int sampleRate, int maxBlockSize, int channels);

        /// <summary>Shapes <paramref name="count"/> interleaved samples of <paramref name="buffer"/> in place.</summary>
        /// <remarks>
        /// <paramref name="channels"/> has to match the count <see cref="Prepare"/> was given. An effect that keeps
        /// per-channel state refuses a block of another shape and passes it through, rather than index past its
        /// buffers on the audio thread.
        /// </remarks>
        public abstract void Process(float[] buffer, int offset, int count, int channels);

        /// <summary>Clears delay lines, envelopes and holds, so a reused voice starts silent.</summary>
        public virtual void Reset() { }
    }
}
