namespace WallstopStudios.DxAudio
{
    /// <summary>
    /// One named random stream with a capture-restore position, draw discipline, and exponential and Gaussian
    /// draws. Main-thread code holds this interface; audio-thread hot loops use
    /// <see cref="SfxXoshiro256StarStar"/> directly, so no draw pays for interface dispatch.
    /// </summary>
    public interface ISfxRandomStream
    {
        /// <summary>The seed this stream was derived from.</summary>
        ulong Seed { get; }

        /// <summary>Returns a uniformly distributed value in [0, 1).</summary>
        double NextDouble();

        /// <summary>
        /// Returns a value in [min, max]. An empty, inverted, NaN, or infinite range returns
        /// <paramref name="min"/> and spends no draw.
        /// </summary>
        float NextFloat(float min, float max);

        /// <summary>
        /// Passes with the given probability. The endpoint probabilities 0 and 1, and any invalid probability,
        /// answer without spending a draw.
        /// </summary>
        bool NextBoolSparing(double probability);

        /// <summary>
        /// Returns a memoryless exponential gap averaging 1 / rate. A rate of zero or less returns NaN and spends
        /// no draw.
        /// </summary>
        double NextExponential(double rate);

        /// <summary>
        /// Returns a Gaussian draw. A standard deviation of zero or less returns <paramref name="mean"/> and
        /// spends no draw; a real draw keeps one spare standard normal so the draw count stays stable.
        /// </summary>
        double NextGaussian(double mean, double standardDeviation);

        /// <summary>Returns a new stream whose seed derives from this stream's current position, including any pending spare.</summary>
        ISfxRandomStream Fork();

        /// <summary>Captures the full position, including any spare Gaussian draw.</summary>
        RandomStreamState Capture();

        /// <summary>
        /// Restores a captured position, so the same future draws come out again. A hand-built all-zero state is
        /// refused and nothing changes, matching the struct's refusal.
        /// </summary>
        void Restore(in RandomStreamState state);
    }
}
