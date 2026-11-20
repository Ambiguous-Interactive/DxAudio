namespace WallstopStudios.DxAudio.Editor
{
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DspEqCurvePlot : IMGUIContainer
    {
        private const float PlotHeight = 196f;
        private const float TargetLimitDecibels = 24f;

        internal event System.Action TargetChanged;
        internal static Color TargetColor => EditorGUIUtility.isProSkin ? new Color(0.38f, 0.80f, 1f) : new Color(0f, 0.32f, 0.57f);
        internal static Color FitColor => EditorGUIUtility.isProSkin ? new Color(1f, 0.73f, 0.39f) : new Color(0.58f, 0.23f, 0f);
        internal static Color SavedColor => EditorGUIUtility.isProSkin ? new Color(0.60f, 0.93f, 0.66f) : new Color(0.12f, 0.39f, 0.18f);
        private static readonly float[] TickFrequencies = { 20f, 100f, 1_000f, 10_000f, 20_000f };
        private readonly Vector3[] _targetLine = new Vector3[DspEqCurveFit.SampleCount];
        private readonly Vector3[] _fitLine = new Vector3[DspEqCurveFit.PreviewSampleCount];
        private readonly Vector3[] _savedLine = new Vector3[DspEqCurveFit.PreviewSampleCount];
        private float[] _target;
        private float[] _fit;
        private float[] _saved;
        private float _fitWidth;
        private int _previousIndex;
        private float _previousDecibels;
        private float _plotLimitDecibels = TargetLimitDecibels;
        private float _strokeLimitDecibels;

        internal DspEqCurvePlot()
        {
            name = "dx-audio-eq-plot";
            style.height = PlotHeight;
            onGUIHandler = Draw;
        }

        private static void DrawCurve(Rect plot, float[] curve, Vector3[] line, float limit, Color color, float width)
        {
            for (int index = 0; index < curve.Length; ++index)
            {
                float x = Mathf.Lerp(plot.xMin, plot.xMax, (float)index / (curve.Length - 1));
                float y = Mathf.Lerp(plot.yMax, plot.yMin, Mathf.InverseLerp(-limit, limit, curve[index]));
                line[index] = new Vector3(x, y, 0f);
            }

            using (new Handles.DrawingScope(color))
            {
                Handles.DrawAAPolyLine(width, line);
            }
        }

        internal void Show(float[] target, float[] fit, float[] saved, int sampleRate)
        {
            _target = target;
            _fit = fit;
            _saved = saved;
            _fitWidth = DspEqCurveFit.GetFitWidth(sampleRate);
            _plotLimitDecibels = TargetLimitDecibels;
            ExpandScale(saved);
            ExpandScale(fit);

            MarkDirtyRepaint();
        }

        private void ExpandScale(float[] response)
        {
            if (response != null)
            {
                foreach (float decibels in response)
                {
                    _plotLimitDecibels = Mathf.Max(_plotLimitDecibels, Mathf.Ceil(Mathf.Abs(decibels) / 12f) * 12f);
                }
            }
        }

        private void Draw()
        {
            Rect bounds = GUILayoutUtility.GetRect(0f, PlotHeight, GUILayout.ExpandWidth(true));
            Rect plot = new(bounds.x + 53f, bounds.y + 11f, bounds.width - 80f, bounds.height - 34f);
            if (plot.width <= 0f)
            {
                return;
            }

            int control = GUIUtility.GetControlID(FocusType.Passive);
            Event current = Event.current;
            bool editing = GUIUtility.hotControl == control;
            switch (current.GetTypeForControl(control))
            {
                case EventType.MouseDown when current.button == 0 && plot.Contains(current.mousePosition):
                    _strokeLimitDecibels = _plotLimitDecibels;
                    GUIUtility.hotControl = control;
                    DrawTargetAt(plot, current.mousePosition, false);
                    current.Use();
                    break;
                case EventType.MouseDrag when editing:
                    DrawTargetAt(plot, current.mousePosition, true);
                    current.Use();
                    break;
                case EventType.MouseUp when editing && current.button == 0:
                    DrawTargetAt(plot, current.mousePosition, true);
                    GUIUtility.hotControl = 0;
                    MarkDirtyRepaint();
                    current.Use();
                    break;
                case EventType.Repaint:
                    float limit = editing ? _strokeLimitDecibels : _plotLimitDecibels;
                    DrawAxes(plot, limit);
                    DrawCurve(plot, _target, _targetLine, limit, TargetColor, 3.5f);
                    Rect fitted = new(plot.xMin, plot.yMin, plot.width * _fitWidth, plot.height);
                    if (_saved != null)
                    {
                        DrawCurve(fitted, _saved, _savedLine, limit, SavedColor, 2.5f);
                    }

                    if (_fit != null)
                    {
                        DrawCurve(fitted, _fit, _fitLine, limit, FitColor, 1.5f);
                    }

                    break;
            }
        }

        private void DrawTargetAt(Rect plot, Vector2 point, bool interpolate)
        {
            float position = Mathf.InverseLerp(plot.xMin, plot.xMax, point.x);
            int index = Mathf.RoundToInt(position * (_target.Length - 1));
            float decibels = Mathf.Lerp(_strokeLimitDecibels, -_strokeLimitDecibels, Mathf.InverseLerp(plot.yMin, plot.yMax, point.y));
            decibels = Mathf.Clamp(decibels, -TargetLimitDecibels, TargetLimitDecibels);
            if (interpolate && _previousIndex != index)
            {
                int first = Mathf.Min(index, _previousIndex);
                int last = Mathf.Max(index, _previousIndex);
                for (int sample = first; sample <= last; ++sample)
                {
                    float along = (float)(sample - _previousIndex) / (index - _previousIndex);
                    _target[sample] = Mathf.Lerp(_previousDecibels, decibels, along);
                }
            }

            _target[index] = decibels;
            _previousIndex = index;
            _previousDecibels = decibels;
            TargetChanged?.Invoke();
        }

        private void DrawAxes(Rect plot, float limit)
        {
            bool dark = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(plot, dark ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.91f, 0.91f, 0.91f));
            if (_fitWidth < 1f)
            {
                float boundary = plot.xMin + (plot.width * _fitWidth);
                EditorGUI.DrawRect(new Rect(boundary, plot.yMin, plot.xMax - boundary, plot.height), dark ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.78f, 0.78f, 0.78f));
            }

            Color grid = dark ? new Color(1f, 1f, 1f, 0.16f) : new Color(0f, 0f, 0f, 0.20f);
            for (int row = 0; row <= 4; ++row)
            {
                float y = Mathf.Lerp(plot.yMin, plot.yMax, row * 0.25f);
                EditorGUI.DrawRect(new Rect(plot.xMin, y, plot.width, 1f), grid);
                float decibels = limit * (1f - (row * 0.5f));
                GUI.Label(new Rect(plot.xMin - 51f, y - 9f, 51f, 18f), $"{decibels:+0;-0;0} dB", EditorStyles.miniLabel);
            }

            float minimum = DspEqCurveFit.MinimumFrequencyHz;
            const float Maximum = DspFilter.MaximumFrequencyHz;
            float previousLabel = plot.xMin - 45f;
            foreach (float frequency in TickFrequencies)
            {
                float x = Mathf.Lerp(plot.xMin, plot.xMax, Mathf.Log(frequency / minimum) / Mathf.Log(Maximum / minimum));
                EditorGUI.DrawRect(new Rect(x, plot.yMin, 1f, plot.height), grid);
                if (frequency < Maximum && (x - previousLabel < 45f || (minimum < frequency && plot.xMax - x < 45f)))
                {
                    continue;
                }

                string label = frequency < 1_000f ? $"{frequency:0} Hz" : $"{frequency / 1_000f:0} kHz";
                GUI.Label(new Rect(x - 23f, plot.yMax + 2f, 49f, 18f), label, EditorStyles.centeredGreyMiniLabel);
                previousLabel = x;
            }
        }
    }
}
