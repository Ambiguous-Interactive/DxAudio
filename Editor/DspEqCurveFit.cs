namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;

    internal static class DspEqCurveFit
    {
        internal const int SampleCount = 257;
        internal const int PreviewSampleCount = ((SampleCount - 1) * PreviewSubdivisions) + 1;
        internal const int MaximumBandCount = 11;
        internal const float MinimumFrequencyHz = 20f;

        private const int CenterStride = 4;
        private const int PreviewSubdivisions = 8;
        private const double MinimumSquaredImprovement = 1e-8;
        private static readonly float[] Qualities = { 0.1f, 0.2f, 0.4f, 0.8f, 1.6f, 3.2f, 6.4f, 12.8f, DspFilter.MaximumQuality };

        internal readonly struct Band
        {
            internal Band(float frequencyHz, float quality, float gainDecibels)
            {
                FrequencyHz = frequencyHz;
                Quality = quality;
                GainDecibels = gainDecibels;
            }

            internal float FrequencyHz { get; }
            internal float Quality { get; }
            internal float GainDecibels { get; }

            internal float GetResponseDecibels(float frequencyHz, int sampleRate)
            {
                double center = Warp(Math.Min(FrequencyHz, GetMaximumCenterHz(sampleRate)), sampleRate);
                double shape = GetShape(Warp(frequencyHz, sampleRate), center, Quality);
                return (float)GetResponse(shape, AudioMath.DecibelsToLinear(GainDecibels));
            }
        }

        internal sealed class Result
        {
            internal Result(Band[] bands, float[] responseDecibels, float[] previewDecibels, float maximumErrorDecibels, float rmsErrorDecibels)
            {
                Bands = bands;
                ResponseDecibels = responseDecibels;
                PreviewDecibels = previewDecibels;
                MaximumErrorDecibels = maximumErrorDecibels;
                RmsErrorDecibels = rmsErrorDecibels;
            }

            internal Band[] Bands { get; }
            internal float[] ResponseDecibels { get; }
            internal float[] PreviewDecibels { get; }
            internal float MaximumErrorDecibels { get; }
            internal float RmsErrorDecibels { get; }
        }

        internal static float GetMaximumFrequencyHz(int sampleRate)
        {
            return Math.Min(DspFilter.MaximumFrequencyHz, 0.49f * sampleRate);
        }

        internal static float FrequencyAt(int index, int sampleRate)
        {
            double span = GetMaximumFrequencyHz(sampleRate) / (double)MinimumFrequencyHz;
            return (float)(MinimumFrequencyHz * Math.Pow(span, index / (SampleCount - 1d)));
        }

        internal static Result Fit(IReadOnlyList<float> targetDecibels, int sampleRate, int bandCount)
        {
            if (targetDecibels == null || targetDecibels.Count != SampleCount)
            {
                throw new ArgumentException($"Supply {SampleCount} target samples.", nameof(targetDecibels));
            }

            if (GetMaximumFrequencyHz(sampleRate) <= MinimumFrequencyHz)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (bandCount < 1 || MaximumBandCount < bandCount)
            {
                throw new ArgumentOutOfRangeException(nameof(bandCount));
            }

            float[] frequencies = new float[SampleCount];
            double[] warped = new double[SampleCount];
            double[] residual = new double[SampleCount];
            double squaredError = 0d;
            for (int index = 0; index < SampleCount; index++)
            {
                float target = targetDecibels[index];
                if (!AudioMath.IsFinite(target))
                {
                    throw new ArgumentException("Target samples must be finite.", nameof(targetDecibels));
                }

                frequencies[index] = FrequencyAt(index, sampleRate);
                warped[index] = Warp(frequencies[index], sampleRate);
                residual[index] = target;
                squaredError += (double)target * target;
            }

            List<Band> bands = new List<Band>(bandCount);
            while (bands.Count < bandCount && MinimumSquaredImprovement < squaredError)
            {
                Band candidate = FindBand(frequencies, warped, residual, sampleRate, squaredError, out double candidateError);
                if (squaredError - candidateError <= MinimumSquaredImprovement)
                {
                    break;
                }

                bands.Add(candidate);
                UpdateResidual(candidate, warped, residual, sampleRate, -1);
                squaredError = RefineBands(bands, warped, residual, sampleRate, candidateError);
            }

            return MeasureFit(targetDecibels, bands.ToArray(), sampleRate);
        }

        private static Result MeasureFit(IReadOnlyList<float> targetDecibels, Band[] bands, int sampleRate)
        {
            float[] response = new float[SampleCount];
            float[] preview = new float[PreviewSampleCount];
            double span = GetMaximumFrequencyHz(sampleRate) / (double)MinimumFrequencyHz;
            double maximumError = 0d;
            double squaredError = 0d;
            for (int index = 0; index < PreviewSampleCount; index++)
            {
                float frequencyHz = (float)(MinimumFrequencyHz * Math.Pow(span, index / (PreviewSampleCount - 1d)));
                preview[index] = MeasureResponse(bands, frequencyHz, sampleRate);
                if (index % PreviewSubdivisions == 0)
                {
                    response[index / PreviewSubdivisions] = preview[index];
                }

                double error = InterpolateTarget(targetDecibels, index / (double)PreviewSubdivisions) - preview[index];
                maximumError = Math.Max(maximumError, Math.Abs(error));
                squaredError += error * error;
            }

            // Narrow high bands can peak between even the denser preview points. Include their centers in the max.
            foreach (Band band in bands)
            {
                if (band.FrequencyHz < MinimumFrequencyHz)
                {
                    continue;
                }

                double position = (SampleCount - 1) * Math.Log(band.FrequencyHz / (double)MinimumFrequencyHz, span);
                double error = InterpolateTarget(targetDecibels, position) - MeasureResponse(bands, band.FrequencyHz, sampleRate);
                maximumError = Math.Max(maximumError, Math.Abs(error));
            }

            return new Result(bands, response, preview, (float)maximumError, (float)Math.Sqrt(squaredError / PreviewSampleCount));
        }

        private static float MeasureResponse(Band[] bands, float frequencyHz, int sampleRate)
        {
            double response = 0d;
            foreach (Band band in bands)
            {
                response += band.GetResponseDecibels(frequencyHz, sampleRate);
            }

            return (float)response;
        }

        private static double InterpolateTarget(IReadOnlyList<float> targetDecibels, double position)
        {
            int left = Math.Min((int)position, SampleCount - 2);
            double fraction = position - left;
            return (targetDecibels[left] * (1d - fraction)) + (targetDecibels[left + 1] * fraction);
        }

        private static Band FindBand(float[] frequencies, double[] warped, double[] residual, int sampleRate, double squaredError, out double bestError)
        {
            Band best = default;
            bestError = squaredError;
            float maximumCenter = GetMaximumCenterHz(sampleRate);
            double[] shape = new double[SampleCount];
            TryCenter(DspFilter.MinimumFrequencyHz, sampleRate, warped, residual, shape, ref best, ref bestError);
            for (int index = 0; index < SampleCount && frequencies[index] < maximumCenter; index++)
            {
                // The coarse grid explores broad curves. Residual extrema also seed narrow bands between grid points.
                bool isPeak =
                    0 < index && index + 1 < SampleCount && Math.Abs(residual[index - 1]) < Math.Abs(residual[index]) && Math.Abs(residual[index + 1]) <= Math.Abs(residual[index]);
                if (index % CenterStride == 0 || isPeak)
                {
                    TryCenter(frequencies[index], sampleRate, warped, residual, shape, ref best, ref bestError);
                }
            }

            TryCenter(maximumCenter, sampleRate, warped, residual, shape, ref best, ref bestError);
            return best;
        }

        private static void TryCenter(float frequencyHz, int sampleRate, double[] warped, double[] residual, double[] shape, ref Band best, ref double bestError)
        {
            double center = Warp(frequencyHz, sampleRate);
            foreach (float quality in Qualities)
            {
                double projection = 0d;
                double basisEnergy = 0d;
                for (int index = 0; index < SampleCount; index++)
                {
                    shape[index] = GetShape(warped[index], center, quality);
                    double basis = 1d / (1d + shape[index]);
                    projection += basis * residual[index];
                    basisEnergy += basis * basis;
                }

                // The small-signal response estimates gain. Score the full nonlinear response before accepting it.
                float gainDb = AudioMath.Clamp((float)(projection / basisEnergy), DspFilter.MinimumPeakDecibels, DspFilter.MaximumPeakDecibels);
                double gain = AudioMath.DecibelsToLinear(gainDb);
                double error = 0d;
                for (int index = 0; index < SampleCount; index++)
                {
                    double difference = residual[index] - GetResponse(shape[index], gain);
                    error += difference * difference;
                }

                if (error < bestError)
                {
                    bestError = error;
                    best = new Band(frequencyHz, quality, gainDb);
                }
            }
        }

        private static double RefineBands(List<Band> bands, double[] warped, double[] residual, int sampleRate, double bestError)
        {
            float maximumCenter = GetMaximumCenterHz(sampleRate);
            // Refine all bands after adding one, so neighboring bands can trade overlapping gain.
            double[] shape = new double[SampleCount];
            for (int pass = 0; pass < 8; pass++)
            {
                double step = Math.Pow(0.5d, pass);
                float frequencyRatio = (float)Math.Pow(GetMaximumFrequencyHz(sampleRate) / MinimumFrequencyHz, CenterStride * step / (SampleCount - 1d));
                float qualityRatio = (float)Math.Pow(2d, step);

                for (int index = bands.Count - 1; 0 <= index; index--)
                {
                    Band band = bands[index];
                    UpdateResidual(band, warped, residual, sampleRate, 1);
                    Band best = band;
                    TryRefinement(band, warped, residual, shape, sampleRate, ref best, ref bestError);
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        float frequency = AudioMath.Clamp(
                            direction < 0 ? band.FrequencyHz / frequencyRatio : band.FrequencyHz * frequencyRatio,
                            DspFilter.MinimumFrequencyHz,
                            maximumCenter
                        );
                        TryRefinement(new Band(frequency, band.Quality, band.GainDecibels), warped, residual, shape, sampleRate, ref best, ref bestError);
                    }

                    band = best;
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        float quality = AudioMath.Clamp(
                            direction < 0 ? band.Quality / qualityRatio : band.Quality * qualityRatio,
                            DspFilter.MinimumQuality,
                            DspFilter.MaximumQuality
                        );
                        TryRefinement(new Band(band.FrequencyHz, quality, band.GainDecibels), warped, residual, shape, sampleRate, ref best, ref bestError);
                    }

                    bands[index] = best;
                    UpdateResidual(best, warped, residual, sampleRate, -1);
                }
            }

            return bestError;
        }

        private static void TryRefinement(Band candidate, double[] warped, double[] residual, double[] shape, int sampleRate, ref Band best, ref double bestError)
        {
            double center = Warp(candidate.FrequencyHz, sampleRate);
            for (int index = 0; index < SampleCount; index++)
            {
                shape[index] = GetShape(warped[index], center, candidate.Quality);
            }

            // Solve for gain again after each frequency or quality move. A wider band can use less gain to fit.
            float gainDb = candidate.GainDecibels;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                double gain = AudioMath.DecibelsToLinear(gainDb);
                double inverseGain = 1d / gain;
                double error = 0d;
                double slopeProjection = 0d;
                double slopeEnergy = 0d;
                for (int index = 0; index < SampleCount; index++)
                {
                    double peak = shape[index] + gain;
                    double trough = shape[index] + inverseGain;
                    double difference = residual[index] - (10d * Math.Log10(peak / trough));
                    double slope = 0.5d * ((gain / peak) + (inverseGain / trough));
                    error += difference * difference;
                    slopeProjection += difference * slope;
                    slopeEnergy += slope * slope;
                }

                if (error < bestError)
                {
                    bestError = error;
                    best = new Band(candidate.FrequencyHz, candidate.Quality, gainDb);
                }

                float nextGain = AudioMath.Clamp(gainDb + (float)(slopeProjection / slopeEnergy), DspFilter.MinimumPeakDecibels, DspFilter.MaximumPeakDecibels);
                if (Math.Abs(nextGain - gainDb) < 0.0001f)
                {
                    break;
                }

                gainDb = nextGain;
            }
        }

        private static void UpdateResidual(Band band, double[] warped, double[] residual, int sampleRate, int sign)
        {
            double center = Warp(band.FrequencyHz, sampleRate);
            double gain = AudioMath.DecibelsToLinear(band.GainDecibels);
            for (int index = 0; index < SampleCount; index++)
            {
                residual[index] += sign * GetResponse(GetShape(warped[index], center, band.Quality), gain);
            }
        }

        private static float GetMaximumCenterHz(int sampleRate)
        {
            return Math.Min(DspFilter.MaximumFrequencyHz, 0.45f * sampleRate);
        }

        private static double Warp(float frequencyHz, int sampleRate)
        {
            return Math.Tan(Math.PI * frequencyHz / sampleRate);
        }

        private static double GetShape(double frequency, double center, float quality)
        {
            double ratio = frequency / center;
            double shape = quality * (ratio - (1d / ratio));
            return shape * shape;
        }

        private static double GetResponse(double shape, double gain)
        {
            // Square the Bell transfer magnitude, then divide both terms by the squared band contribution.
            return 10d * Math.Log10((shape + gain) / (shape + (1d / gain)));
        }
    }
}
