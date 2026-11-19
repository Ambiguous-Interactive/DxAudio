namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DxAudio.Editor;

    public sealed class DspEqCurveTests
    {
        private const int BlockFrames = 256;

        [Test]
        public void AFlatTargetFitsWithoutShapingTheSignal()
        {
            DspEqCurveFit.Result fit = DspEqCurveFit.FitDrawing(new float[DspEqCurveFit.SampleCount], 48_000, DspEqCurveFit.MaximumBandCount);

            Assert.That(fit.ResponseDecibels.Length, Is.EqualTo(DspEqCurveFit.SampleCount));
            Assert.That(fit.ResponseDecibels, Is.All.EqualTo(0f));
            Assert.That(fit.MaximumErrorDecibels, Is.EqualTo(0f));
            Assert.That(fit.RmsErrorDecibels, Is.EqualTo(0f));
        }

        [Test]
        public void LowerPreviewRatesKeepTheDrawingAtItsOriginalFrequencies()
        {
            float[] drawing = new float[DspEqCurveFit.SampleCount];
            drawing[drawing.Length - 1] = 12f;

            DspEqCurveFit.Result lowerRate = DspEqCurveFit.FitDrawing(drawing, 32_000, 1);
            DspEqCurveFit.Result fullRange = DspEqCurveFit.FitDrawing(drawing, 48_000, 1);

            Assert.That(lowerRate.Bands, Is.Empty);
            Assert.That(lowerRate.MaximumErrorDecibels, Is.EqualTo(0f));
            Assert.That(fullRange.Bands, Has.Length.EqualTo(1));
        }

        [Test]
        public void LowerPreviewRatesMeasureErrorAgainstTheVisibleDrawing()
        {
            float[] drawing = new float[DspEqCurveFit.SampleCount];
            for (int index = 0; index < drawing.Length; ++index)
            {
                drawing[index] = 12f * index / (drawing.Length - 1);
            }

            DspEqCurveFit.Result fit = DspEqCurveFit.FitDrawing(drawing, 32_000, 3);
            double visibleFraction = Math.Log(15_680d / 20d, 20_000d / 20d);
            (float maximum, float rms, _) = MeasureCurveError(drawing, fit.PreviewDecibels, visibleFraction);

            Assert.That(fit.Bands, Is.Not.Empty);
            Assert.That(fit.RmsErrorDecibels, Is.EqualTo(rms).Within(0.0001f));
            Assert.That(fit.MaximumErrorDecibels, Is.GreaterThanOrEqualTo(maximum - 0.0001f));
        }

        [Test]
        public void ACroppedPreviewMeasuresErrorAtTheOriginalDrawPoint()
        {
            const int SampleRate = 32_000;
            const int SpikeIndex = 245;
            float[] target = new float[DspEqCurveFit.SampleCount];
            target[SpikeIndex] = 24f;
            DspEqCurveFit.Result fit = DspEqCurveFit.FitDrawing(target, SampleRate, DspEqCurveFit.MaximumBandCount);
            float frequency = DspEqCurveFit.FrequencyAt(SpikeIndex, 48_000);
            float response = 0f;
            foreach (DspEqCurveFit.Band band in fit.Bands)
            {
                response += band.GetResponseDecibels(frequency, SampleRate);
            }

            Assert.That(fit.MaximumErrorDecibels, Is.GreaterThanOrEqualTo(Math.Abs(target[SpikeIndex] - response) - 0.001f));
        }

        [TestCase(44_100, 1_000f, 0.8f, 9f, 0.25f)]
        [TestCase(48_000, 12_000f, 8f, -12f, 0.65f)]
        public void OneBellFollowsAKnownShapeDrawnAtTheSelectedRate(int sampleRate, float frequency, float quality, float gain, float sampleTolerance)
        {
            float[] target = SampleTarget(sampleRate, new DspEqCurveFit.Band(frequency, quality, gain));
            DspEqCurveFit.Result fit = DspEqCurveFit.FitDrawing(target, sampleRate, 1);
            DspEqCurveFit.Result repeated = DspEqCurveFit.FitDrawing(target, sampleRate, 1);

            Assert.That(fit.Bands, Has.Length.EqualTo(1));
            Assert.That(fit.ResponseDecibels, Is.EqualTo(repeated.ResponseDecibels));
            (float maximum, _, _) = MeasureCurveError(target, fit.ResponseDecibels);
            // Straight segments cut across the narrow Bell between samples. Fitting those segments shifts its gain.
            Assert.That(maximum, Is.LessThan(sampleTolerance), $"single Bell at {frequency} Hz");
            Assert.That(fit.RmsErrorDecibels, Is.LessThan(0.05f));
        }

        [TestCase(44_100, false)]
        [TestCase(48_000, false)]
        [TestCase(44_100, true)]
        public void AReloadedChainProcessesThePreviewedFrequencyResponse(int sampleRate, bool endpointSpike)
        {
            float[] target = endpointSpike
                ? CreateEndpointSpike(24f)
                : SampleTarget(sampleRate, new DspEqCurveFit.Band(400f, 0.6f, 8f), new DspEqCurveFit.Band(1_350f, 1.2f, -12f));
            int bandLimit = endpointSpike ? DspEqCurveFit.MaximumBandCount : 3;
            DspEqCurveFit.Result fit = DspEqCurveFit.FitDrawing(target, sampleRate, bandLimit);
            Assert.That(fit.Bands.Length, Is.GreaterThan(1));

            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/DxAudioEqReloadTest.asset");
            try
            {
                DspChainAsset authored = DspEqCurveAsset.SaveChain(path, target, sampleRate, bandLimit, fit);
                Assert.That(authored != null, Is.True);
                for (int index = 0; index < authored.Count; ++index)
                {
                    Resources.UnloadAsset(authored.Effects[index]);
                }

                Resources.UnloadAsset(authored);
                DspChainAsset loaded = AssetDatabase.LoadAssetAtPath<DspChainAsset>(path);
                Assert.That(loaded != null, Is.True);
                Assert.That(loaded, Is.Not.SameAs(authored));
                Assert.That(loaded.Count, Is.EqualTo(fit.Bands.Length));
                DspChainAsset.DrawnEqSettings settings = DspEqCurveAsset.ReadSettings(loaded);
                Assert.That(settings.Target, Is.EqualTo(target));
                Assert.That(settings.SampleRate, Is.EqualTo(sampleRate));
                Assert.That(settings.BandLimit, Is.EqualTo(bandLimit));
                for (int index = 0; index < loaded.Count; ++index)
                {
                    DspFilterAsset stage = loaded.Effects[index] as DspFilterAsset;
                    Assert.That(stage != null, Is.True, $"stage {index} reference survives reload");
                    Assert.That(AssetDatabase.IsSubAsset(stage), Is.True);
                    Assert.That(stage.Mode, Is.EqualTo(DspFilterMode.Bell));
                    Assert.That(stage.FrequencyHz, Is.EqualTo(fit.Bands[index].FrequencyHz));
                    Assert.That(stage.Quality, Is.EqualTo(fit.Bands[index].Quality));
                    Assert.That(stage.PeakDecibels, Is.EqualTo(fit.Bands[index].GainDecibels));
                }

                DspChain chain = loaded.CreateChain();
                Assert.That(chain.TryPrepare(sampleRate, BlockFrames, 1), Is.True);
                int last = fit.PreviewDecibels.Length - 1;
                int interval = last / (DspEqCurveFit.SampleCount - 1);
                int[] positions = endpointSpike
                    ? new[] { last - interval, last - (interval / 2), last - (interval / 4), last }
                    : new[] { 0, 48 * interval, 111 * interval, 156 * interval, 200 * interval, last };
                double span = DspEqCurveFit.GetMaximumFrequencyHz(sampleRate) / (double)DspEqCurveFit.MinimumFrequencyHz;
                float maximumMeasuredChange = 0f;
                foreach (int position in positions)
                {
                    float frequency = (float)(DspEqCurveFit.MinimumFrequencyHz * Math.Pow(span, position / (double)last));
                    float measured = MeasureToneDecibels(chain, frequency, sampleRate);
                    maximumMeasuredChange = Math.Max(maximumMeasuredChange, Math.Abs(measured));
                    Assert.That(measured, Is.EqualTo(fit.PreviewDecibels[position]).Within(0.015f), $"{frequency:0.##} Hz at {sampleRate} Hz");
                }

                Assert.That(maximumMeasuredChange, Is.GreaterThan(3f), "the exported chain shapes a tone away from unity");
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void ThePreviewUsesTheSameCornerLimitAsThePreparedBell()
        {
            const int SampleRate = 32_000;
            const float CappedFrequency = 14_400f;
            DspEqCurveFit.Band band = new(20_000f, 1.25f, 9f);
            DspChain chain = PrepareBell(band, SampleRate);

            float preview = band.GetResponseDecibels(CappedFrequency, SampleRate);
            float measured = MeasureToneDecibels(chain, CappedFrequency, SampleRate);

            Assert.That(preview, Is.EqualTo(band.GainDecibels).Within(0.0001f));
            Assert.That(measured, Is.EqualTo(preview).Within(0.015f));
        }

        [Test]
        public void ThePreviewTracksANarrowHighBellBetweenTargetPoints()
        {
            const int SampleRate = 44_100;
            float[] target = new float[DspEqCurveFit.SampleCount];
            target[target.Length - 4] = -18f;
            DspEqCurveFit.Result fit = DspEqCurveFit.FitDrawing(target, SampleRate, 1);
            Assert.That(fit.Bands, Has.Length.EqualTo(1));
            Assert.That(fit.PreviewDecibels.Length, Is.GreaterThan(target.Length));

            (float difference, _, int sample) = MeasureCurveError(fit.ResponseDecibels, fit.PreviewDecibels);
            Assert.That(difference, Is.GreaterThan(1f), "the dense preview exposes detail between target points");
            double span = DspEqCurveFit.GetMaximumFrequencyHz(SampleRate) / (double)DspEqCurveFit.MinimumFrequencyHz;
            float frequency = (float)(DspEqCurveFit.MinimumFrequencyHz * Math.Pow(span, sample / (fit.PreviewDecibels.Length - 1d)));
            float measured = MeasureToneDecibels(PrepareBell(fit.Bands[0], SampleRate), frequency, SampleRate);

            Assert.That(measured, Is.EqualTo(fit.PreviewDecibels[sample]).Within(0.015f), $"{frequency:0.##} Hz");
            (float maximum, float rms, _) = MeasureCurveError(target, fit.PreviewDecibels);
            Assert.That(fit.MaximumErrorDecibels, Is.GreaterThanOrEqualTo(maximum - 0.0001f));
            Assert.That(fit.RmsErrorDecibels, Is.EqualTo(rms).Within(0.0001f));
        }

        [TestCase(44_100, 24f)]
        [TestCase(44_100, -24f)]
        [TestCase(48_000, 24f)]
        [TestCase(96_000, 24f)]
        public void AnEndpointSpikeStaysCloseToTheDrawingBetweenDrawPoints(int sampleRate, float gain)
        {
            DspEqCurveFit.Result fit = DspEqCurveFit.FitDrawing(CreateEndpointSpike(gain), sampleRate, DspEqCurveFit.MaximumBandCount);
            float peak = 0f;
            double maximumError = 0d;
            double drawStep = Math.Log(1_000d) / (DspEqCurveFit.SampleCount - 1);
            for (int frequency = 19_000; frequency <= 20_000; ++frequency)
            {
                float response = 0f;
                foreach (DspEqCurveFit.Band band in fit.Bands)
                {
                    response += band.GetResponseDecibels(frequency, sampleRate);
                }

                peak = Math.Max(peak, Math.Abs(response));
                double target = gain * Math.Max(0d, 1d + (Math.Log(frequency / 20_000d) / drawStep));
                maximumError = Math.Max(maximumError, Math.Abs(target - response));
            }

            Assert.That(peak, Is.LessThan(26f), "response magnitude between drawing points");
            Assert.That(maximumError, Is.LessThan(4f), "the fit still follows the endpoint spike");
            Assert.That(fit.RmsErrorDecibels, Is.LessThan(0.10f));
        }

        [Test]
        public void ExportRefusesToReplaceAnExistingChain()
        {
            const int SampleRate = 48_000;
            float[] target = SampleTarget(SampleRate, new DspEqCurveFit.Band(1_000f, 0.8f, 9f));
            DspEqCurveFit.Result fit = DspEqCurveFit.FitDrawing(target, SampleRate, 1);
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/DxAudioEqCollisionTest.asset");
            try
            {
                DspChainAsset authored = DspEqCurveAsset.SaveChain(path, target, SampleRate, 1, fit);

                Assert.Throws<ArgumentException>(() => DspEqCurveAsset.SaveChain(path, target, SampleRate, 1, fit));
                Assert.That(AssetDatabase.LoadAssetAtPath<DspChainAsset>(path) == authored, Is.True);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void AReopenedInspectorUsesTheSavedRateAndBandLimit()
        {
            const int CustomRate = 72_000;
            float[] target = DspEqCurveAuthoring.CreateTarget(1, 48_000);
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/DxAudioEqInspectorTest.asset");
            try
            {
                DspChainAsset chain = DspEqCurveAsset.SaveChain(path, target, CustomRate, 1, DspEqCurveFit.FitDrawing(target, CustomRate, 1));
                Resources.UnloadAsset(chain);
                DspChainAsset loaded = AssetDatabase.LoadAssetAtPath<DspChainAsset>(path);
                DspEqCurveAuthoring inspector = new(loaded);
                Assert.That(inspector.Q<PopupField<int>>("dx-audio-eq-rate").value, Is.EqualTo(CustomRate));
                Assert.That(inspector.Q<PopupField<int>>("dx-audio-eq-band-limit").value, Is.EqualTo(1));
                Assert.That(inspector.Q<Button>("dx-audio-eq-apply").enabledSelf, Is.False);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void ARefitKeepsGeneratedFileIdsAndRestoresTheSettingsOnUndo()
        {
            const int InitialRate = 48_000;
            const int UpdatedRate = 32_000;
            string chainPath = AssetDatabase.GenerateUniqueAssetPath("Assets/DxAudioEqRefitTest.asset");
            string otherPath = AssetDatabase.GenerateUniqueAssetPath("Assets/DxAudioEqReferenceTest.asset");
            float[] original = SampleTarget(InitialRate, new DspEqCurveFit.Band(1_000f, 0.8f, 9f));
            float[] updated = DspEqCurveAuthoring.CreateTarget(3, 48_000);
            DspEqCurveFit.Result originalFit = DspEqCurveFit.FitDrawing(original, InitialRate, 1);
            DspEqCurveFit.Result updatedFit = DspEqCurveFit.FitDrawing(updated, UpdatedRate, 3);
            Assert.That(updatedFit.Bands.Length, Is.GreaterThan(1));

            DspFilterAsset manual = DspFilterAsset.Create();
            try
            {
                DspChainAsset chain = DspEqCurveAsset.SaveChain(chainPath, original, InitialRate, 1, originalFit);
                DspFilterAsset first = (DspFilterAsset)chain.Effects[0];
                long firstId = GetFileId(first);
                DspChainAsset other = DspChainAsset.Create(new[] { first });
                AssetDatabase.CreateAsset(other, otherPath);
                AssetDatabase.AddObjectToAsset(manual, other);
                using (SerializedObject serialized = new(chain))
                {
                    SerializedProperty effects = serialized.FindProperty("_effects");
                    effects.arraySize = 2;
                    effects.GetArrayElementAtIndex(1).objectReferenceValue = manual;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                DspEqCurveAsset.ApplyFit(chain, updated, UpdatedRate, 3, updatedFit);
                int grownEffectCount = updatedFit.Bands.Length + 1;
                Assert.That(chain.Count, Is.EqualTo(grownEffectCount));
                Assert.That(GetFileId(chain.Effects[0]), Is.EqualTo(firstId));
                Assert.That(chain.Effects[grownEffectCount - 1] == manual, Is.True);
                Assert.That(other.Effects[0] == first, Is.True);
                long secondId = GetFileId(chain.Effects[1]);
                Assert.That(first.PeakDecibels, Is.EqualTo(updatedFit.Bands[0].GainDecibels));
                Assert.That(DspEqCurveAsset.ReadSettings(chain).Target, Is.EqualTo(updated));

                Undo.PerformUndo();
                Assert.That(chain.Count, Is.EqualTo(2));
                Assert.That(first.PeakDecibels, Is.EqualTo(originalFit.Bands[0].GainDecibels));
                DspChainAsset.DrawnEqSettings restored = DspEqCurveAsset.ReadSettings(chain);
                Assert.That(restored.Target, Is.EqualTo(original));
                Assert.That(restored.SampleRate, Is.EqualTo(InitialRate));
                Assert.That(restored.BandLimit, Is.EqualTo(1));

                Undo.PerformRedo();
                Assert.That(chain.Count, Is.EqualTo(grownEffectCount));
                Assert.That(GetFileId(chain.Effects[1]), Is.EqualTo(secondId));
                restored = DspEqCurveAsset.ReadSettings(chain);
                Assert.That(restored.Target, Is.EqualTo(updated));
                Assert.That(restored.SampleRate, Is.EqualTo(UpdatedRate));
                Assert.That(restored.BandLimit, Is.EqualTo(3));
                using (SerializedObject serialized = new(chain))
                {
                    SerializedProperty effects = serialized.FindProperty("_effects");
                    effects.MoveArrayElement(grownEffectCount - 1, 1);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                AssetDatabase.SaveAssetIfDirty(chain);
                AssetDatabase.ImportAsset(chainPath, ImportAssetOptions.ForceSynchronousImport);
                Assert.That(GetFileId(chain.Effects[2]), Is.EqualTo(secondId));
                Assert.That(AssetDatabase.GetAssetPath(chain.Effects[2]), Is.EqualTo(chainPath));

                float[] flat = new float[DspEqCurveFit.SampleCount];
                DspEqCurveAsset.ApplyFit(chain, flat, UpdatedRate, 1, DspEqCurveFit.FitDrawing(flat, UpdatedRate, 1));
                Assert.That(chain.Count, Is.EqualTo(1));
                Assert.That(chain.Effects[0] == manual, Is.True);
                Assert.That(other.Effects[0] == first, Is.True);
                Assert.That(first.PeakDecibels, Is.EqualTo(updatedFit.Bands[0].GainDecibels));
                Assert.That(GetFileId(first), Is.EqualTo(firstId));

                Undo.PerformUndo();
                Assert.That(GetFileId(chain.Effects[2]), Is.EqualTo(secondId));
                Assert.That(chain.Effects[1] == manual, Is.True);
                Undo.PerformRedo();
                DspEqCurveAsset.ApplyFit(chain, updated, UpdatedRate, 3, updatedFit);
                Assert.That(GetFileId(chain.Effects[0]), Is.EqualTo(firstId));
                Assert.That(chain.Effects[1] == manual, Is.True);
                Assert.That(GetFileId(chain.Effects[2]), Is.EqualTo(secondId));
            }
            finally
            {
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(chainPath))
                {
                    Undo.ClearUndo(asset);
                }

                AssetDatabase.DeleteAsset(chainPath);
                AssetDatabase.DeleteAsset(otherPath);
                if (manual != null)
                {
                    UnityEngine.Object.DestroyImmediate(manual);
                }
            }
        }

        [Test]
        public void AChainWithoutSavedSettingsKeepsItsExistingStages()
        {
            using MadeObjects made = new();
            DspFilterAsset stage = made.Track(DspFilterAsset.Create());
            DspChainAsset chain = made.Track(DspChainAsset.Create(new[] { stage }));
            DspEqCurveAuthoring inspector = new(chain);
            Assert.That(DspEqCurveAsset.ReadSettings(chain) == null, Is.True);
            Assert.That(inspector.Q<PopupField<int>>("dx-audio-eq-band-limit").value, Is.Zero);
            Assert.That(inspector.Q<Button>("dx-audio-eq-apply").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(chain.Effects[0] == stage, Is.True);
        }

        private static long GetFileId(DspEffectAsset asset)
        {
            Assert.That(asset != null, Is.True);
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long fileId), Is.True);
            return fileId;
        }

        private static float[] CreateEndpointSpike(float gain)
        {
            float[] drawing = new float[DspEqCurveFit.SampleCount];
            drawing[drawing.Length - 1] = gain;
            return drawing;
        }

        private static float[] SampleTarget(int sampleRate, params DspEqCurveFit.Band[] bands)
        {
            float[] target = new float[DspEqCurveFit.SampleCount];
            for (int index = 0; index < target.Length; ++index)
            {
                float frequency = DspEqCurveFit.FrequencyAt(index, sampleRate);
                foreach (DspEqCurveFit.Band band in bands)
                {
                    target[index] += band.GetResponseDecibels(frequency, sampleRate);
                }
            }

            return target;
        }

        private static DspChain PrepareBell(DspEqCurveFit.Band band, int sampleRate)
        {
            DspChain chain = new();
            chain.Add(
                new DspFilter
                {
                    Mode = DspFilterMode.Bell,
                    FrequencyHz = band.FrequencyHz,
                    Quality = band.Quality,
                    PeakDecibels = band.GainDecibels,
                }
            );
            Assert.That(chain.TryPrepare(sampleRate, BlockFrames, 1), Is.True);
            return chain;
        }

        private static (float, float, int) MeasureCurveError(float[] reference, float[] response, double width = 1d)
        {
            double squaredError = 0d;
            double maximum = 0d;
            int maximumIndex = 0;
            for (int index = 0; index < response.Length; ++index)
            {
                double position = index * width * (reference.Length - 1d) / (response.Length - 1d);
                int left = (int)position;
                int right = Math.Min(left + 1, reference.Length - 1);
                double expected = reference[left] + ((position - left) * (reference[right] - reference[left]));
                double error = Math.Abs(response[index] - expected);
                if (maximum < error)
                {
                    maximum = error;
                    maximumIndex = index;
                }

                squaredError += error * error;
            }

            return ((float)maximum, (float)Math.Sqrt(squaredError / response.Length), maximumIndex);
        }

        private static float MeasureToneDecibels(DspChain chain, float frequency, int sampleRate)
        {
            const int WarmFrames = 32_768;
            const int MeasureFrames = 4_096;
            const double Amplitude = 0.125;
            double phaseStep = 2.0 * Math.PI * frequency / sampleRate;
            float[] block = new float[BlockFrames];
            double sineEnergy = 0.0;
            double cosineEnergy = 0.0;
            double cross = 0.0;
            double outputSine = 0.0;
            double outputCosine = 0.0;
            chain.Reset();
            for (int offset = 0; offset < WarmFrames + MeasureFrames; offset += BlockFrames)
            {
                for (int index = 0; index < block.Length; ++index)
                {
                    block[index] = (float)(Amplitude * Math.Sin((offset + index) * phaseStep));
                }

                chain.Process(block, 0, block.Length, 1);
                if (offset < WarmFrames)
                {
                    continue;
                }

                // Fit both phases so the measurement works when the window holds a fraction of a cycle.
                for (int index = 0; index < block.Length; ++index)
                {
                    double phase = (offset + index) * phaseStep;
                    double sine = Math.Sin(phase);
                    double cosine = Math.Cos(phase);
                    sineEnergy += sine * sine;
                    cosineEnergy += cosine * cosine;
                    cross += sine * cosine;
                    outputSine += block[index] * sine;
                    outputCosine += block[index] * cosine;
                }
            }

            double determinant = (sineEnergy * cosineEnergy) - (cross * cross);
            double sineAmplitude = ((outputSine * cosineEnergy) - (outputCosine * cross)) / determinant;
            double cosineAmplitude = ((outputCosine * sineEnergy) - (outputSine * cross)) / determinant;
            double outputPower = (sineAmplitude * sineAmplitude) + (cosineAmplitude * cosineAmplitude);
            return (float)(10.0 * Math.Log10(outputPower / (Amplitude * Amplitude)));
        }
    }
}
