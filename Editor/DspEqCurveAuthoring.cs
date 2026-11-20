namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DspEqCurveAuthoring : VisualElement
    {
        private const int DefaultSampleRate = 48_000;
        private static readonly string[] Presets = { "Flat", "Broad low shelf", "Narrow notch", "Overlapping boosts" };
        private readonly DspChainAsset _chain;
        private readonly PopupField<int> _rateField;
        private readonly PopupField<int> _bandField;
        private readonly DspEqCurvePlot _plot;
        private readonly Label _savedLegend;
        private readonly Button _fitButton;
        private readonly Button _saveButton;
        private readonly Button _applyButton;
        private readonly Label _status;
        private float[] _target = new float[DspEqCurveFit.SampleCount];
        private DspChainAsset.DrawnEqSettings _loadedSettings;
        private float[] _savedResponse;
        private DspEqCurveFit.Result _fit;
        private int _sampleRate;
        private int _bandLimit;

        internal DspEqCurveAuthoring(DspChainAsset chain)
        {
            _chain = chain;
            List<int> rates = new() { 32_000, 44_100, 48_000, 96_000 };
            int outputRate = AudioSettings.outputSampleRate;
            if (DspEqCurveFit.MinimumFrequencyHz < DspEqCurveFit.GetMaximumFrequencyHz(outputRate) && !rates.Contains(outputRate))
            {
                rates.Add(outputRate);
                rates.Sort();
            }

            _sampleRate = rates.Contains(outputRate) ? outputRate : DefaultSampleRate;
            _rateField = new PopupField<int>("Preview rate", rates, defaultValue: _sampleRate, FormatRate, FormatRate) { name = "dx-audio-eq-rate" };
            _rateField.RegisterValueChangedCallback(evt =>
            {
                _sampleRate = evt.newValue;
                RefreshCurrentResponse();
                InvalidateFit();
            });
            Add(_rateField);

            List<int> limits = new();
            for (int limit = 0; limit <= DspEqCurveFit.MaximumBandCount; ++limit)
            {
                limits.Add(limit);
            }

            _bandField = new PopupField<int>("Band limit", limits, defaultValue: 0, FormatBandLimit, FormatBandLimit) { name = "dx-audio-eq-band-limit" };
            _bandField.RegisterValueChangedCallback(evt =>
            {
                _bandLimit = evt.newValue;
                InvalidateFit();
            });
            Add(_bandField);

            _plot = new DspEqCurvePlot();
            _plot.TargetChanged += InvalidateFit;
            Add(_plot);

            VisualElement legend = DxAudioInspectorUi.Row();
            legend.style.flexWrap = Wrap.Wrap;
            Label targetLegend = DxAudioInspectorUi.AddText(legend, "Target");
            targetLegend.style.color = DspEqCurvePlot.TargetColor;
            _savedLegend = DxAudioInspectorUi.AddText(legend, "Current generated Bells");
            _savedLegend.style.color = DspEqCurvePlot.SavedColor;
            Label fitLegend = DxAudioInspectorUi.AddText(legend, "Proposed fit");
            fitLegend.style.color = DspEqCurvePlot.FitColor;
            Add(legend);

            Label instructions = new("Drag to draw the target or load an example. Fit the drawing before you save or apply it.");
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
            actions.style.flexWrap = Wrap.Wrap;
            _fitButton = new Button(Fit) { text = "Fit target" };
            _applyButton = new Button(Apply) { text = "Apply fit to this chain", name = "dx-audio-eq-apply" };
            _saveButton = new Button(Save) { text = "Save new EQ chain" };
            actions.Add(_fitButton);
            actions.Add(_applyButton);
            actions.Add(_saveButton);
            Add(actions);

            _status = new Label();
            _status.style.whiteSpace = WhiteSpace.Normal;
            Add(_status);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed += RefreshSavedAsset;
                ObjectChangeEvents.changesPublished += OnAssetsChanged;
                RefreshSavedAsset();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed -= RefreshSavedAsset;
                ObjectChangeEvents.changesPublished -= OnAssetsChanged;
            });
            RefreshSavedAsset();
        }

        internal static float[] CreateTarget(int preset, int sampleRate)
        {
            float[] samples = new float[DspEqCurveFit.SampleCount];
            for (int index = 0, samplesLength = samples.Length; index < samplesLength; ++index)
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

        private void OnAssetsChanged(ref ObjectChangeEventStream stream)
        {
            if (_chain == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(_chain);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            GUID guid = AssetDatabase.GUIDFromAssetPath(path);
            for (int index = 0; index < stream.length; ++index)
            {
                if (stream.GetEventType(index) != ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    continue;
                }

                stream.GetChangeAssetObjectPropertiesEvent(index, out ChangeAssetObjectPropertiesEventArgs change);
                if (change.guid == guid)
                {
                    RefreshSavedAsset();
                    return;
                }
            }
        }

        private void RefreshSavedAsset()
        {
            if (_chain == null)
            {
                return;
            }

            DspChainAsset.DrawnEqSettings saved = DspEqCurveAsset.ReadSettings(_chain);
            if (
                saved != null
                && (
                    _loadedSettings == null
                    || _loadedSettings.SampleRate != saved.SampleRate
                    || _loadedSettings.BandLimit != saved.BandLimit
                    || !_loadedSettings.Target.AsSpan().SequenceEqual(saved.Target)
                )
            )
            {
                _target = (float[])saved.Target.Clone();
                _sampleRate = saved.SampleRate;
                _bandLimit = saved.BandLimit;
                if (!_rateField.choices.Contains(_sampleRate))
                {
                    _rateField.choices.Add(_sampleRate);
                    _rateField.choices.Sort();
                }

                _rateField.SetValueWithoutNotify(_sampleRate);
                _bandField.SetValueWithoutNotify(_bandLimit);
                _fit = null;
            }

            _loadedSettings = saved == null ? null : new DspChainAsset.DrawnEqSettings(saved.Target, saved.SampleRate, saved.BandLimit, saved.GeneratedStages);
            RefreshCurrentResponse();
            RefreshPreview();
        }

        private void RefreshCurrentResponse()
        {
            _savedResponse =
                _loadedSettings == null ? null : DspEqCurveFit.MeasureFit(_target, DspEqCurveAsset.ReadCurrentBands(_chain, _loadedSettings), _sampleRate).PreviewDecibels;
        }

        private void InvalidateFit()
        {
            _fit = null;
            RefreshPreview();
        }

        private void Fit()
        {
            _fit = DspEqCurveFit.FitDrawing(_target, _sampleRate, _bandLimit);
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            _plot.Show(_target, _fit?.PreviewDecibels, _savedResponse, _sampleRate);
            _savedLegend.style.display = _loadedSettings == null ? DisplayStyle.None : DisplayStyle.Flex;
            _applyButton.style.display = _loadedSettings == null ? DisplayStyle.None : DisplayStyle.Flex;
            _fitButton.SetEnabled(0 < _bandLimit);
            _saveButton.SetEnabled(_fit != null);
            _applyButton.SetEnabled(_fit != null);
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
                EditorGUIUtility.PingObject(DspEqCurveAsset.SaveChain(path, _target, _sampleRate, _bandLimit, _fit));
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Could not save EQ chain", exception.Message, "OK");
            }
        }

        private void Apply()
        {
            try
            {
                DspEqCurveAsset.ApplyFit(_chain, _target, _sampleRate, _bandLimit, _fit);
                _fit = null;
                RefreshSavedAsset();
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Could not apply EQ fit", exception.Message, "OK");
            }
        }
    }
}
