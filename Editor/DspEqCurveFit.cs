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
        private const int FitSubdivisions = 2;
        private const int PreviewSubdivisions = 8;
        private const int HighFrequencyIntervals = 16;
        private const int BroadEdgeIntervals = 4;
        private const int HighFrequencyStart = SampleCount - 1 - HighFrequencyIntervals;
        private const int HighFrequencyFitStart = HighFrequencyStart * FitSubdivisions;
        private const int FitSampleCount = HighFrequencyFitStart + (HighFrequencyIntervals * PreviewSubdivisions) + 1;
        private const double MinimumSquaredImprovement = 1e-8;
        private const double MaximumEdgeWeight = 8d;
        private static readonly float[] Qualities = { 0.1f, 0.2f, 0.4f, 0.8f, 1.6f, 3.2f, 6.4f, 12.8f, DspFilter.MaximumQuality };

        internal static float GetMaximumFrequencyHz(int sampleRate)
        {
            return Math.Min(DspFilter.MaximumFrequencyHz, 0.49f * sampleRate);
        }

        internal static float GetFitWidth(int sampleRate)
        {
            double fullSpan = DspFilter.MaximumFrequencyHz / (double)MinimumFrequencyHz;
            return (float)Math.Log(GetMaximumFrequencyHz(sampleRate) / (double)MinimumFrequencyHz, fullSpan);
        }

        internal static float FrequencyAt(int index, int sampleRate)
        {
            double span = GetMaximumFrequencyHz(sampleRate) / (double)MinimumFrequencyHz;
            return (float)(MinimumFrequencyHz * Math.Pow(span, index / (SampleCount - 1d)));
        }

        internal static Result FitDrawing(IReadOnlyList<float> drawing, int sampleRate, int bandCount)
        {
            if (drawing == null || drawing.Count != SampleCount)
            {
                throw new ArgumentException($"Supply {SampleCount} target samples.", nameof(drawing));
            }

            if (GetMaximumFrequencyHz(sampleRate) <= MinimumFrequencyHz)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (bandCount < 1 || MaximumBandCount < bandCount)
            {
                throw new ArgumentOutOfRangeException(nameof(bandCount));
            }

            for (int index = 0; index < SampleCount; ++index)
            {
                if (!AudioMath.IsFinite(drawing[index]))
                {
                    throw new ArgumentException("Target samples must be finite.", nameof(drawing));
                }
            }

            float[] frequencies = new float[FitSampleCount];
            double[] warped = new double[FitSampleCount];
            double[] residual = new double[FitSampleCount];
            double[] shape = new double[FitSampleCount];
            double[] weights = new double[FitSampleCount];
            double drawingWidth = GetFitWidth(sampleRate);
            float maximumFrequencyHz = GetMaximumFrequencyHz(sampleRate);
            double span = maximumFrequencyHz / (double)MinimumFrequencyHz;
            double edge = (SampleCount - 1) * drawingWidth;
            double edgeStart = Math.Max(0d, edge - 1d);
            double edgeWeight = 0d;
            // Beyond the last legal Bell center, a steep edge needs several bands. Emphasize that interval in the score.
            if (1 < bandCount && GetMaximumCenterHz(sampleRate) < maximumFrequencyHz)
            {
                double startGain = InterpolateTarget(drawing, edgeStart);
                double cornerGain = drawing[(int)edge];
                double endGain = InterpolateTarget(drawing, edge);
                double variation = Math.Abs(cornerGain - startGain) + Math.Abs(endGain - cornerGain);
                edgeWeight = MaximumEdgeWeight * Math.Min(1d, variation / DspFilter.MaximumPeakDecibels);
            }

            double squaredError = 0d;
            // Near Nyquist, a Bell occupies fewer log-frequency samples. Use more points in the top range.
            for (int index = 0; index < FitSampleCount; ++index)
            {
                double position = FitPositionAt(index);
                double drawingPosition = position * drawingWidth;
                frequencies[index] = (float)(MinimumFrequencyHz * Math.Pow(span, position / (SampleCount - 1d)));
                warped[index] = Warp(frequencies[index], sampleRate);
                residual[index] = InterpolateTarget(drawing, drawingPosition);
                // Weight each sample by the log-frequency interval it represents.
                int left = Math.Max(0, index - 1);
                int right = Math.Min(index + 1, FitSampleCount - 1);
                double edgeProgress = Math.Max(0d, drawingPosition - edgeStart);
                weights[index] = (FitPositionAt(right) - FitPositionAt(left)) * 0.5d * (1d + (edgeWeight * edgeProgress));
                squaredError += weights[index] * residual[index] * residual[index];
            }

            BandResponses responses = new BandResponses(bandCount);
            List<Band> bands = new List<Band>(bandCount);
            float maximumCenter = GetMaximumCenterHz(sampleRate);
            double endpoint = residual[FitSampleCount - 1];
            double[] pairedResidual =
                1 < bandCount && MinimumSquaredImprovement < squaredError && maximumCenter < maximumFrequencyHz && HasBroadEdge(drawing, edge, endpoint)
                    ? (double[])residual.Clone()
                    : null;

            GrowBands(bands, bandCount, frequencies, warped, residual, shape, weights, sampleRate, squaredError, responses);
            Result greedy = MeasureFit(drawing, bands.ToArray(), sampleRate);
            double endpointResidual = residual[FitSampleCount - 1];
            if (pairedResidual == null || endpoint * endpointResidual <= 0d || Math.Abs(endpointResidual) <= greedy.RmsErrorDecibels)
            {
                return greedy;
            }

            // Beyond the legal center, the wider Bell keeps its gain after the narrower Bell falls away.
            float gain = (float)Math.Min(DspFilter.MaximumPeakDecibels, 2d * Math.Abs(endpoint)) * Math.Sign(endpoint);
            // The greedy result holds its own bands, so this search can reuse the response buffers.
            List<Band> pairedBands = new List<Band>(bandCount);
            AddBand(pairedBands, new Band(maximumCenter, DspFilter.MinimumQuality, gain), warped, pairedResidual, sampleRate, responses);
            AddBand(pairedBands, new Band(maximumCenter, DspFilter.MinimumQuality * 2f, -gain), warped, pairedResidual, sampleRate, responses);
            double pairedError = 0d;
            for (int index = 0; index < FitSampleCount; ++index)
            {
                pairedError += weights[index] * pairedResidual[index] * pairedResidual[index];
            }

            pairedError = RefineBands(pairedBands, warped, pairedResidual, shape, weights, sampleRate, pairedError, responses);
            pairedError = GrowBands(pairedBands, bandCount, frequencies, warped, pairedResidual, shape, weights, sampleRate, pairedError, responses);
            RefineBands(pairedBands, warped, pairedResidual, shape, weights, sampleRate, pairedError, responses);

            Result paired = MeasureFit(drawing, pairedBands.ToArray(), sampleRate);
            if (
                Math.Abs(pairedResidual[FitSampleCount - 1]) < Math.Abs(endpointResidual)
                && paired.MaximumErrorDecibels < greedy.MaximumErrorDecibels
                && paired.RmsErrorDecibels < greedy.RmsErrorDecibels
                && IsPeakWithinRange(paired, greedy, drawing, edge, endpoint, sampleRate)
            )
            {
                return paired;
            }

            return greedy;
        }

        internal static Result MeasureFit(IReadOnlyList<float> targetDecibels, Band[] bands, int sampleRate)
        {
            double drawingWidth = GetFitWidth(sampleRate);
            float[] response = new float[SampleCount];
            float[] preview = new float[PreviewSampleCount];
            double span = GetMaximumFrequencyHz(sampleRate) / (double)MinimumFrequencyHz;
            double maximumError = 0d;
            double squaredError = 0d;
            for (int index = 0; index < PreviewSampleCount; ++index)
            {
                float frequencyHz = (float)(MinimumFrequencyHz * Math.Pow(span, index / (PreviewSampleCount - 1d)));
                preview[index] = MeasureResponse(bands, frequencyHz, sampleRate);
                if (index % PreviewSubdivisions == 0)
                {
                    response[index / PreviewSubdivisions] = preview[index];
                }

                double error = InterpolateTarget(targetDecibels, index * drawingWidth / PreviewSubdivisions) - preview[index];
                maximumError = Math.Max(maximumError, Math.Abs(error));
                squaredError += error * error;
            }

            // At cropped rates the preview grid can step over a corner in the original drawing.
            if (drawingWidth < 1d)
            {
                double visibleIntervals = (SampleCount - 1) * drawingWidth;
                for (int index = 1; index < visibleIntervals; ++index)
                {
                    float frequencyHz = (float)(MinimumFrequencyHz * Math.Pow(span, index / visibleIntervals));
                    double error = targetDecibels[index] - MeasureResponse(bands, frequencyHz, sampleRate);
                    maximumError = Math.Max(maximumError, Math.Abs(error));
                }
            }

            // Narrow high bands can peak between even the denser preview points. Include their centers in the max.
            foreach (Band band in bands)
            {
                float center = Math.Min(band.FrequencyHz, GetMaximumCenterHz(sampleRate));
                if (center < MinimumFrequencyHz)
                {
                    continue;
                }

                double position = (SampleCount - 1) * drawingWidth * Math.Log(center / (double)MinimumFrequencyHz, span);
                double error = InterpolateTarget(targetDecibels, position) - MeasureResponse(bands, center, sampleRate);
                maximumError = Math.Max(maximumError, Math.Abs(error));
            }

            return new Result(bands, response, preview, (float)maximumError, (float)Math.Sqrt(squaredError / PreviewSampleCount));
        }

        private static bool HasBroadEdge(IReadOnlyList<float> drawing, double edge, double endpoint)
        {
            if (endpoint == 0d || edge < BroadEdgeIntervals)
            {
                return false;
            }

            // Leave isolated spikes to the edge-weighted search.
            double minimumGain = Math.Abs(endpoint) * 0.5d;
            int sign = Math.Sign(endpoint);
            for (int index = (int)edge - BroadEdgeIntervals; index <= (int)edge; ++index)
            {
                if (drawing[index] * sign < minimumGain)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPeakWithinRange(Result paired, Result greedy, IReadOnlyList<float> drawing, double edge, double endpoint, int sampleRate)
        {
            double limit = Math.Abs(endpoint);
            for (int index = 0; index <= (int)edge; ++index)
            {
                limit = Math.Max(limit, Math.Abs(drawing[index]));
            }

            // An extra Bell can peak below the 20 Hz drawing or above the high-frequency edge.
            return MeasurePeak(paired, sampleRate) <= Math.Max(limit, MeasurePeak(greedy, sampleRate));
        }

        private static float MeasurePeak(Result fit, int sampleRate)
        {
            float peak = 0f;
            foreach (float gain in fit.PreviewDecibels)
            {
                peak = Math.Max(peak, Math.Abs(gain));
            }

            foreach (Band band in fit.Bands)
            {
                peak = Math.Max(peak, Math.Abs(MeasureResponse(fit.Bands, band.FrequencyHz, sampleRate)));
            }

            const int TailIntervals = 32;
            float previewEnd = GetMaximumFrequencyHz(sampleRate);
            float upperSpan = (sampleRate * 0.5f) - previewEnd;
            for (int index = 1; index < TailIntervals; ++index)
            {
                float fraction = index / (float)TailIntervals;
                peak = Math.Max(peak, Math.Abs(MeasureResponse(fit.Bands, MinimumFrequencyHz * fraction, sampleRate)));
                peak = Math.Max(peak, Math.Abs(MeasureResponse(fit.Bands, previewEnd + (upperSpan * fraction), sampleRate)));
            }

            return peak;
        }

        private static double GrowBands(
            List<Band> bands,
            int bandCount,
            float[] frequencies,
            double[] warped,
            double[] residual,
            double[] shape,
            double[] weights,
            int sampleRate,
            double squaredError,
            BandResponses responses
        )
        {
            while (bands.Count < bandCount && MinimumSquaredImprovement < squaredError)
            {
                double candidateError = squaredError;
                Band candidate = FindBand(frequencies, warped, residual, shape, weights, sampleRate, ref candidateError);
                if (squaredError - candidateError <= MinimumSquaredImprovement)
                {
                    break;
                }

                AddBand(bands, candidate, warped, residual, sampleRate, responses);
                squaredError = RefineBands(bands, warped, residual, shape, weights, sampleRate, candidateError, responses);
            }

            return squaredError;
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

        private static double FitPositionAt(int index)
        {
            return index < HighFrequencyFitStart ? index / (double)FitSubdivisions : HighFrequencyStart + ((index - HighFrequencyFitStart) / (double)PreviewSubdivisions);
        }

        private static int DrawSampleToFitIndex(int index)
        {
            return index < HighFrequencyStart ? index * FitSubdivisions : HighFrequencyFitStart + ((index - HighFrequencyStart) * PreviewSubdivisions);
        }

        private static Band FindBand(float[] frequencies, double[] warped, double[] residual, double[] shape, double[] weights, int sampleRate, ref double bestError)
        {
            Band best = default;
            float maximumCenter = GetMaximumCenterHz(sampleRate);
            TryCenter(DspFilter.MinimumFrequencyHz, sampleRate, warped, residual, shape, weights, ref best, ref bestError);
            // Keep the seed grid at the drawing resolution. Score each seed at the finer resolution.
            for (int sample = 0; sample < SampleCount; ++sample)
            {
                int index = DrawSampleToFitIndex(sample);
                if (maximumCenter <= frequencies[index])
                {
                    break;
                }

                bool isPeak =
                    0 < sample
                    && sample + 1 < SampleCount
                    && Math.Abs(residual[DrawSampleToFitIndex(sample - 1)]) < Math.Abs(residual[index])
                    && Math.Abs(residual[DrawSampleToFitIndex(sample + 1)]) <= Math.Abs(residual[index]);
                if (sample % CenterStride == 0 || isPeak)
                {
                    TryCenter(frequencies[index], sampleRate, warped, residual, shape, weights, ref best, ref bestError);
                }
            }

            TryCenter(maximumCenter, sampleRate, warped, residual, shape, weights, ref best, ref bestError);
            return best;
        }

        private static void TryCenter(float frequencyHz, int sampleRate, double[] warped, double[] residual, double[] shape, double[] weights, ref Band best, ref double bestError)
        {
            double center = Warp(frequencyHz, sampleRate);
            for (int index = 0; index < FitSampleCount; ++index)
            {
                shape[index] = GetBellOffset(warped[index], center);
            }

            foreach (float quality in Qualities)
            {
                double projection = 0d;
                double basisEnergy = 0d;
                for (int index = 0; index < FitSampleCount; ++index)
                {
                    double offset = quality * shape[index];
                    double basis = 1d / (1d + (offset * offset));
                    projection += weights[index] * basis * residual[index];
                    basisEnergy += weights[index] * basis * basis;
                }

                // The small-signal response estimates gain. Score the full nonlinear response before accepting it.
                float gainDb = AudioMath.Clamp((float)(projection / basisEnergy), DspFilter.MinimumPeakDecibels, DspFilter.MaximumPeakDecibels);
                double gain = AudioMath.DecibelsToLinear(gainDb);
                double inverseGain = 1d / gain;
                double error = 0d;
                // Check the high-frequency samples first to reject poor fits for narrow peaks sooner.
                for (int index = FitSampleCount - 1; 0 <= index && error < bestError; index--)
                {
                    double offset = quality * shape[index];
                    double bellShape = offset * offset;
                    double difference = residual[index] - (10d * Math.Log10((bellShape + gain) / (bellShape + inverseGain)));
                    error += weights[index] * difference * difference;
                }

                if (error < bestError)
                {
                    bestError = error;
                    best = new Band(frequencyHz, quality, gainDb);
                }
            }
        }

        private static double RefineBands(
            List<Band> bands,
            double[] warped,
            double[] residual,
            double[] shape,
            double[] weights,
            int sampleRate,
            double bestError,
            BandResponses responses
        )
        {
            float maximumCenter = GetMaximumCenterHz(sampleRate);
            // Refine all bands after adding one, so neighboring bands can trade overlapping gain.
            for (int pass = 0; pass < 6; ++pass)
            {
                double step = Math.Pow(0.5d, pass);
                float frequencyRatio = (float)Math.Pow(GetMaximumFrequencyHz(sampleRate) / MinimumFrequencyHz, CenterStride * step / (SampleCount - 1d));
                float qualityRatio = (float)Math.Pow(2d, step);

                for (int index = bands.Count - 1; 0 <= index; index--)
                {
                    Band band = bands[index];
                    UpdateResidual(residual, responses.Active[index], 1);
                    Band best = band;
                    TryRefinement(band, warped, residual, shape, weights, sampleRate, responses, index, ref best, ref bestError);
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        float frequency = AudioMath.Clamp(
                            direction < 0 ? band.FrequencyHz / frequencyRatio : band.FrequencyHz * frequencyRatio,
                            DspFilter.MinimumFrequencyHz,
                            maximumCenter
                        );
                        if (frequency != band.FrequencyHz)
                        {
                            TryRefinement(
                                new Band(frequency, band.Quality, band.GainDecibels),
                                warped,
                                residual,
                                shape,
                                weights,
                                sampleRate,
                                responses,
                                index,
                                ref best,
                                ref bestError
                            );
                        }
                    }

                    band = best;
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        float quality = AudioMath.Clamp(
                            direction < 0 ? band.Quality / qualityRatio : band.Quality * qualityRatio,
                            DspFilter.MinimumQuality,
                            DspFilter.MaximumQuality
                        );
                        if (quality != band.Quality)
                        {
                            TryRefinement(
                                new Band(band.FrequencyHz, quality, band.GainDecibels),
                                warped,
                                residual,
                                shape,
                                weights,
                                sampleRate,
                                responses,
                                index,
                                ref best,
                                ref bestError
                            );
                        }
                    }

                    bands[index] = best;
                    UpdateResidual(residual, responses.Active[index], -1);
                }
            }

            return bestError;
        }

        private static void TryRefinement(
            Band candidate,
            double[] warped,
            double[] residual,
            double[] shape,
            double[] weights,
            int sampleRate,
            BandResponses responses,
            int bandIndex,
            ref Band best,
            ref double bestError
        )
        {
            double center = Warp(candidate.FrequencyHz, sampleRate);
            for (int index = 0; index < FitSampleCount; ++index)
            {
                shape[index] = GetShape(warped[index], center, candidate.Quality);
            }

            // Solve for gain again after each frequency or quality move. A wider band can use less gain to fit.
            float gainDb = candidate.GainDecibels;
            for (int iteration = 0; iteration < 4; ++iteration)
            {
                double gain = AudioMath.DecibelsToLinear(gainDb);
                double inverseGain = 1d / gain;
                double error = 0d;
                double slopeProjection = 0d;
                double slopeEnergy = 0d;
                double[] candidateResponse = responses.Candidate;
                for (int index = 0; index < FitSampleCount; ++index)
                {
                    double peak = shape[index] + gain;
                    double trough = shape[index] + inverseGain;
                    double response = 10d * Math.Log10(peak / trough);
                    candidateResponse[index] = response;
                    double difference = residual[index] - response;
                    double slope = 0.5d * ((gain / peak) + (inverseGain / trough));
                    error += weights[index] * difference * difference;
                    slopeProjection += weights[index] * difference * slope;
                    slopeEnergy += weights[index] * slope * slope;
                }

                if (error < bestError)
                {
                    bestError = error;
                    best = new Band(candidate.FrequencyHz, candidate.Quality, gainDb);
                    responses.KeepCandidate(bandIndex);
                }

                float nextGain = AudioMath.Clamp(gainDb + (float)(slopeProjection / slopeEnergy), DspFilter.MinimumPeakDecibels, DspFilter.MaximumPeakDecibels);
                if (Math.Abs(nextGain - gainDb) < 0.0001f)
                {
                    break;
                }

                gainDb = nextGain;
            }
        }

        private static void AddBand(List<Band> bands, Band band, double[] warped, double[] residual, int sampleRate, BandResponses responses)
        {
            double[] response = responses.Active[bands.Count] ??= new double[FitSampleCount];
            double center = Warp(band.FrequencyHz, sampleRate);
            double gain = AudioMath.DecibelsToLinear(band.GainDecibels);
            for (int index = 0; index < FitSampleCount; ++index)
            {
                response[index] = GetResponse(GetShape(warped[index], center, band.Quality), gain);
            }

            UpdateResidual(residual, response, -1);
            bands.Add(band);
        }

        private static void UpdateResidual(double[] residual, double[] response, int sign)
        {
            for (int index = 0; index < FitSampleCount; ++index)
            {
                residual[index] += sign * response[index];
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
            double shape = quality * GetBellOffset(frequency, center);
            return shape * shape;
        }

        private static double GetBellOffset(double frequency, double center)
        {
            double ratio = frequency / center;
            return ratio - (1d / ratio);
        }

        private static double GetResponse(double shape, double gain)
        {
            // Square the Bell transfer magnitude, then divide both terms by the squared band contribution.
            return 10d * Math.Log10((shape + gain) / (shape + (1d / gain)));
        }

        private sealed class BandResponses
        {
            internal double[][] Active { get; }
            internal double[] Candidate { get; private set; }

            internal BandResponses(int bandCount)
            {
                Active = new double[bandCount][];
                Candidate = new double[FitSampleCount];
            }

            internal void KeepCandidate(int bandIndex)
            {
                // Reuse the previous band's buffer for the next candidate.
                double[] previous = Active[bandIndex];
                Active[bandIndex] = Candidate;
                Candidate = previous;
            }
        }

        internal readonly struct Band
        {
            internal float FrequencyHz { get; }
            internal float Quality { get; }
            internal float GainDecibels { get; }

            internal Band(float frequencyHz, float quality, float gainDecibels)
            {
                FrequencyHz = frequencyHz;
                Quality = quality;
                GainDecibels = gainDecibels;
            }

            internal float GetResponseDecibels(float frequencyHz, int sampleRate)
            {
                double center = Warp(Math.Min(FrequencyHz, GetMaximumCenterHz(sampleRate)), sampleRate);
                double shape = GetShape(Warp(frequencyHz, sampleRate), center, Quality);
                return (float)GetResponse(shape, AudioMath.DecibelsToLinear(GainDecibels));
            }
        }

        internal sealed class Result
        {
            internal Band[] Bands { get; }
            internal float[] ResponseDecibels { get; }
            internal float[] PreviewDecibels { get; }
            internal float MaximumErrorDecibels { get; }
            internal float RmsErrorDecibels { get; }

            internal Result(Band[] bands, float[] responseDecibels, float[] previewDecibels, float maximumErrorDecibels, float rmsErrorDecibels)
            {
                Bands = bands;
                ResponseDecibels = responseDecibels;
                PreviewDecibels = previewDecibels;
                MaximumErrorDecibels = maximumErrorDecibels;
                RmsErrorDecibels = rmsErrorDecibels;
            }
        }
    }
}
