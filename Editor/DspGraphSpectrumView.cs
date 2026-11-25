namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DspGraphSpectrumView : VisualElement
    {
        private const int Bands = 32;
        private readonly DspGraphAsset _graph;
        private readonly DspAuditionPanel _audition;
        private readonly MixMonitorPlayerMeter _inputMeter = new(spectrum: true);
        private readonly MixMonitorPlayerMeter _outputMeter = new(spectrum: true);
        private readonly float[] _input = new float[2 * MixMonitorPlayerMeter.SampleCount];
        private readonly float[] _output = new float[2 * MixMonitorPlayerMeter.SampleCount];
        private readonly VisualElement[] _inputBars = new VisualElement[Bands];
        private readonly VisualElement[] _outputBars = new VisualElement[Bands];
        private readonly int[] _bandStart = new int[Bands + 1];
        private readonly Label _status = new();
        private readonly VisualElement _plot = new();
        private DspGraphSpectrumTap _tap;
        private int _node = -1;

        internal DspGraphSpectrumView(DspGraphAsset graph, DspAuditionPanel audition)
        {
            _graph = graph;
            _audition = audition;
            name = "DxAudioGraphSpectrum";
            Label heading = new("What it does to the sound");
            heading.AddToClassList("dxaudio-graph-title");
            Add(heading);
            Add(new Label("Input (left) · Output (right)"));
            _plot.AddToClassList("dxaudio-graph-spectrum");
            Add(_plot);
            int lastBin = MixMonitorPlayerMeter.SampleCount / 2;
            for (int band = 0; band < Bands; ++band)
            {
                _bandStart[band] = Mathf.CeilToInt(Mathf.Pow(lastBin, (float)band / Bands));
                VisualElement column = new();
                column.AddToClassList("dxaudio-spectrum-column");
                _inputBars[band] = AddBar(column, "dxaudio-spectrum-input");
                _outputBars[band] = AddBar(column, "dxaudio-spectrum-output");
                _plot.Add(column);
            }

            _bandStart[Bands] = lastBin + 1;
            Add(_status);
            RegisterCallback<DetachFromPanelEvent>(_ => Disconnect());
            schedule.Execute(UpdateSpectrum).Every(100);
            ShowUnavailable("Play wet audition to compare this node.");
        }

        private static VisualElement AddBar(VisualElement column, string styleClass)
        {
            VisualElement bar = new() { pickingMode = PickingMode.Ignore };
            bar.AddToClassList(styleClass);
            column.Add(bar);
            return bar;
        }

        internal void Select(int node)
        {
            if (_node != node)
            {
                Disconnect();
                _node = node;
            }

            UpdateSpectrum();
        }

        private void Disconnect()
        {
            _tap?.Select(-1);
            _tap = null;
            ShowUnavailable("Play wet audition to compare this node.");
        }

        private void UpdateSpectrum()
        {
            DspGraphMeterTap meters = _node < 0 ? null : _audition.GetGraphMeters(_graph);
            DspGraphSpectrumTap tap = meters?.Spectrum;
            if (tap != _tap)
            {
                Disconnect();
                _tap = tap;
                _tap?.Select(_node);
            }

            if (tap == null)
            {
                return;
            }

            if (meters.PreparedNode(_node) < 0)
            {
                ShowUnavailable("Muted by Solo");
                return;
            }

            if (!tap.TryRead(_input, _output))
            {
                return;
            }

            ReadSpectrum(_inputMeter, _input, _inputBars);
            ReadSpectrum(_outputMeter, _output, _outputBars);
            if (_plot.style.display.value == DisplayStyle.None)
            {
                _plot.style.display = DisplayStyle.Flex;
                _status.text = $"{(float)tap.SampleRate / MixMonitorPlayerMeter.SampleCount:0} Hz – {tap.SampleRate / 2000f:0.#} kHz";
            }
        }

        private void ReadSpectrum(MixMonitorPlayerMeter meter, float[] interleaved, VisualElement[] bars)
        {
            meter.AddChannel(interleaved, right: false, offset: 0, stride: 2);
            meter.AddChannel(interleaved, right: true, offset: 1, stride: 2);
            meter.FinishSample(0d);
            ReadOnlySpan<float> bins = meter.Spectrum;
            for (int band = 0; band < Bands; ++band)
            {
                float peak = 0f;
                int start = _bandStart[band];
                int end = Math.Max(start + 1, _bandStart[band + 1]);
                for (int bin = start; bin < end; ++bin)
                {
                    peak = Mathf.Max(peak, bins[bin]);
                }

                bars[band].style.height = Length.Percent(100f * Mathf.InverseLerp(AudioMath.SilentDecibels, 0f, AudioMath.LinearToDecibels(peak)));
            }
        }

        private void ShowUnavailable(string message)
        {
            _plot.style.display = DisplayStyle.None;
            if (!string.Equals(_status.text, message, StringComparison.Ordinal))
            {
                _status.text = message;
            }
        }
    }
}
