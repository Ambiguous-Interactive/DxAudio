namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>
    /// Pure distribution math over uniforms in [0, 1), shared by <see cref="SfxRandomStream"/> and by any audio
    /// path that draws through a <see cref="SfxXoshiro256StarStar"/> directly. Every function validates its input
    /// and answers NaN or a constant without asking for a draw it cannot use.
    /// </summary>
    public static class SfxRandomMath
    {
        /// <summary>
        /// An exponential gap with the given rate: -ln(1 - u) / rate, averaging 1 / rate. A rate of zero or less,
        /// or a uniform outside [0, 1), returns NaN.
        /// </summary>
        public static double ExponentialFromUniform(double uniform, double rate)
        {
            if (!(0d < rate) || uniform < 0d || 1d <= uniform)
            {
                return double.NaN;
            }

            return -Math.Log(1d - uniform) / rate;
        }

        /// <summary>
        /// A Box-Muller Gaussian pair for a mean and standard deviation. The uniforms come from a stream in [0, 1),
        /// so 1 - u sits in (0, 1] and the logarithm stays finite. A non-finite standard deviation, a standard
        /// deviation of zero or less, or a NaN or out-of-range uniform returns the mean for both draws.
        /// </summary>
        public static void GaussianPairFromUniform(double firstUniform, double secondUniform, double mean, double standardDeviation, out double firstDraw, out double secondDraw)
        {
            if (
                !(0d < standardDeviation)
                || double.IsInfinity(standardDeviation)
                || double.IsNaN(firstUniform)
                || firstUniform < 0d
                || 1d <= firstUniform
                || double.IsNaN(secondUniform)
                || secondUniform < 0d
                || 1d <= secondUniform
            )
            {
                firstDraw = mean;
                secondDraw = mean;
                return;
            }

            double radius = Math.Sqrt(-2d * Math.Log(1d - firstUniform));
            double angle = 2d * Math.PI * secondUniform;
            firstDraw = mean + (standardDeviation * radius * Math.Cos(angle));
            secondDraw = mean + (standardDeviation * radius * Math.Sin(angle));
        }
    }
}
