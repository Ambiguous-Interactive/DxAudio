namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DspEqCurveAuthoring : VisualElement
    {
        private const int DefaultSampleRate = 48_000;
        private static readonly string[] Presets = { "Flat", "Broad low shelf", "Narrow notch", "Overlapping boosts" };
        private readonly DspEqCurvePlot _plot;
        private readonly Button _fitButton;
        private readonly Button _saveButton;
        private readonly Label _status;
        private float[] _target = new float[DspEqCurveFit.SampleCount];
        private DspEqCurveFit.Result _fit;
        private int _sampleRate;
        private int _bandLimit;

        internal DspEqCurveAuthoring()
        {
            List<int> rates = new() { 32_000, 44_100, 48_000, 96_000 };
            int outputRate = AudioSettings.outputSampleRate;
            if (DspEqCurveFit.MinimumFrequencyHz < DspEqCurveFit.GetMaximumFrequencyHz(outputRate) && !rates.Contains(outputRate))
            {
                rates.Add(outputRate);
                rates.Sort();
            }

            _sampleRate = rates.Contains(outputRate) ? outputRate : DefaultSampleRate;
            PopupField<int> rate = new("Preview rate", rates, defaultValue: _sampleRate, FormatRate, FormatRate);
            rate.RegisterValueChangedCallback(evt =>
            {
                _sampleRate = evt.newValue;
                InvalidateFit();
            });
            Add(rate);

            List<int> limits = new();
            for (int limit = 0; limit <= DspEqCurveFit.MaximumBandCount; ++limit)
            {
                limits.Add(limit);
            }

            PopupField<int> bands = new("Band limit", limits, defaultValue: 0, FormatBandLimit, FormatBandLimit);
            bands.RegisterValueChangedCallback(evt =>
            {
                _bandLimit = evt.newValue;
                InvalidateFit();
            });
            Add(bands);

            _plot = new DspEqCurvePlot();
            _plot.TargetChanged += InvalidateFit;
            Add(_plot);

            VisualElement legend = DxAudioInspectorUi.Row();
            Label targetLegend = DxAudioInspectorUi.AddText(legend, "Target");
            targetLegend.style.color = DspEqCurvePlot.TargetColor;
            Label fitLegend = DxAudioInspectorUi.AddText(legend, "Fitted");
            fitLegend.style.color = DspEqCurvePlot.FitColor;
            Add(legend);

            Label instructions = new("Drag to draw the target or load an example. The drawing is temporary.");
            instructions.style.whiteSpace = WhiteSpace.Normal;
            Add(instructions);

            List<string> examples = new(Presets);
            PopupField<string> preset = new("Example", examples, 0);
            VisualElement exampleRow = DxAudioInspectorUi.Row();
            preset.style.flexGrow = 1f;
            exampleRow.Add(preset);
            exampleRow.Add(
                new Button(() =>
                {
                    _target = CreateTarget(Array.IndexOf(Presets, preset.value), DefaultSampleRate);
                    InvalidateFit();
                })
                {
                    text = "Load",
                }
            );
            Add(exampleRow);

            VisualElement actions = DxAudioInspectorUi.Row();
            _fitButton = new Button(Fit) { text = "Fit target" };
            _saveButton = new Button(Save) { text = "Save new EQ chain" };
            actions.Add(_fitButton);
            actions.Add(_saveButton);
            Add(actions);

            _status = new Label();
            _status.style.whiteSpace = WhiteSpace.Normal;
            Add(_status);
            InvalidateFit();
        }

        internal static float[] CreateTarget(int preset, int sampleRate)
        {
            float[] samples = new float[DspEqCurveFit.SampleCount];
            for (int index = 0; index < samples.Length; ++index)
            {
                float frequency = DspEqCurveFit.FrequencyAt(index, sampleRate);
                switch (preset)
                {
                    case 1:
                        samples[index] = 6f * (1f - Mathf.SmoothStep(0f, 1f, (Mathf.Log(frequency / 250f, 2f) + 1f) * 0.5f));
                        break;
                    case 2:
                        samples[index] = -12f * Gaussian(frequency, 2_000f, 0.07f);
                        break;
                    case 3:
                        samples[index] = (7f * Gaussian(frequency, 650f, 0.48f)) + (6f * Gaussian(frequency, 1_100f, 0.60f));
                        break;
                }
            }

            return samples;
        }

        internal static DspChainAsset SaveChain(string path, DspEqCurveFit.Result result)
        {
            if (
                string.IsNullOrEmpty(path)
                || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                || !AssetDatabase.IsValidFolder(path.Substring(0, path.LastIndexOf('/')))
                || File.Exists(path)
                || AssetDatabase.LoadMainAssetAtPath(path) != null
            )
            {
                throw new ArgumentException("Choose a new .asset file under Assets.", nameof(path));
            }

            List<DspEffectAsset> effects = new(result.Bands.Length);
            DspChainAsset chain = null;
            string createdGuid = null;
            try
            {
                foreach (DspEqCurveFit.Band band in result.Bands)
                {
                    DspFilterAsset filter = DspFilterAsset.Create(DspFilterMode.Bell, band.FrequencyHz, band.Quality, band.GainDecibels);
                    filter.name = $"Bell {effects.Count + 1}";
                    effects.Add(filter);
                }

                chain = DspChainAsset.Create(effects);
                AssetDatabase.CreateAsset(chain, path);
                if (!AssetDatabase.Contains(chain))
                {
                    throw new InvalidOperationException("Unity could not create the EQ chain.");
                }

                createdGuid = AssetDatabase.AssetPathToGUID(path);
                foreach (DspEffectAsset filter in effects)
                {
                    AssetDatabase.AddObjectToAsset(filter, chain);
                }

                EditorUtility.SetDirty(chain);
                AssetDatabase.SaveAssetIfDirty(chain);
                // Unity registers the sub-assets when it imports the saved file.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                DspChainAsset saved = AssetDatabase.LoadAssetAtPath<DspChainAsset>(path);
                if (saved == null || saved.Count != effects.Count)
                {
                    throw new InvalidOperationException("Unity could not save the EQ chain.");
                }

                for (int index = 0; index < saved.Count; ++index)
                {
                    if (saved.Effects[index] == null || !AssetDatabase.IsSubAsset(saved.Effects[index]))
                    {
                        throw new InvalidOperationException("Unity could not save an EQ band.");
                    }
                }

                return saved;
            }
            catch
            {
                if (!string.IsNullOrEmpty(createdGuid) && AssetDatabase.AssetPathToGUID(path) == createdGuid)
                {
                    AssetDatabase.DeleteAsset(path);
                }

                foreach (DspEffectAsset filter in effects)
                {
                    if (filter != null && !AssetDatabase.Contains(filter))
                    {
                        UnityEngine.Object.DestroyImmediate(filter);
                    }
                }

                if (chain != null && !AssetDatabase.Contains(chain))
                {
                    UnityEngine.Object.DestroyImmediate(chain);
                }

                throw;
            }
        }

        private static float Gaussian(float frequency, float center, float widthOctaves)
        {
            float offset = Mathf.Log(frequency / center, 2f) / widthOctaves;
            return Mathf.Exp(-0.5f * offset * offset);
        }

        private static string FormatRate(int rate)
        {
            return $"{rate / 1_000f:0.###} kHz";
        }

        private static string FormatBandLimit(int limit)
        {
            return limit == 0 ? "Choose a limit" : limit.ToString();
        }

        private void InvalidateFit()
        {
            _fit = null;
            _plot.Show(_target, null, _sampleRate);
            _fitButton.SetEnabled(0 < _bandLimit);
            _saveButton.SetEnabled(false);
            UpdateStatus();
        }

        private void Fit()
        {
            _fit = DspEqCurveFit.FitDrawing(_target, _sampleRate, _bandLimit);
            _plot.Show(_target, _fit.PreviewDecibels, _sampleRate);
            _saveButton.SetEnabled(true);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            float maximum = DspEqCurveFit.GetMaximumFrequencyHz(_sampleRate);
            string coverage = maximum < DspFilter.MaximumFrequencyHz ? $"Fit covers 20 Hz to {maximum / 1_000f:0.###} kHz. " : string.Empty;
            if (_fit == null)
            {
                _status.text = coverage.TrimEnd();
                return;
            }

            string stageLabel = _fit.Bands.Length == 1 ? "band" : "bands";
            _status.text = $"{coverage}{_fit.Bands.Length} {stageLabel} | RMS error {_fit.RmsErrorDecibels:0.00} dB | Sampled max error {_fit.MaximumErrorDecibels:0.00} dB";
        }

        private void Save()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save EQ chain", "Fitted EQ", "asset", "Choose a new file for the fitted chain.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                EditorGUIUtility.PingObject(SaveChain(path, _fit));
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Could not save EQ chain", exception.Message, "OK");
            }
        }
    }
}
