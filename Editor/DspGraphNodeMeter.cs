namespace WallstopStudios.DxAudio.Editor
{
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DspGraphNodeMeter : VisualElement
    {
        internal const string MutedBySolo = "Muted by Solo";
        private const float Floor = -60f;
        private const int MinimumDb = (int)AudioMath.SilentDecibels;

        // 20 log10(float.MaxValue) rounds to 771.
        private const int MaximumFiniteDb = 771;
        private static readonly (string Normal, string Clipped)[] DbLabels = BuildLabels();
        private readonly VisualElement _left = new();
        private readonly VisualElement _right = new();
        private readonly VisualElement _heldLeft = new();
        private readonly VisualElement _heldRight = new();
        private readonly Label _caption = new() { pickingMode = PickingMode.Ignore };
        private string _displayed;
        private int _decibels = int.MinValue;
        private bool _clipped;

        internal DspGraphNodeMeter()
        {
            pickingMode = PickingMode.Ignore;
            name = "DxAudioGraphNodeMeter";
            tooltip = "Left and right sample peaks after the node. Markers and CLIP hold for 2 seconds.";
            AddToClassList("dxaudio-graph-node-meter");
            Add(Track(_left, _heldLeft));
            Add(Track(_right, _heldRight));
            _caption.AddToClassList("dxaudio-graph-meter-caption");
            Add(_caption);
            style.display = DisplayStyle.None;
        }

        internal static int PeakDb(float peak)
        {
            if (float.IsPositiveInfinity(peak))
            {
                return int.MaxValue;
            }

            return 0f < peak ? Mathf.RoundToInt(AudioMath.LinearToDecibels(peak)) : int.MinValue;
        }

        internal static string FormatDb(int decibels, bool clipped = false)
        {
            if (decibels < MinimumDb)
            {
                return clipped ? "−∞ dBFS  CLIP" : "−∞ dBFS";
            }

            if (MaximumFiniteDb < decibels)
            {
                return clipped ? "∞ dBFS  CLIP" : "∞ dBFS";
            }

            (string normal, string clip) = DbLabels[decibels - MinimumDb];
            return clipped ? clip : normal;
        }

        private static (string, string)[] BuildLabels()
        {
            (string, string)[] labels = new (string, string)[MaximumFiniteDb - MinimumDb + 1];
            for (int decibels = MinimumDb; decibels <= MaximumFiniteDb; ++decibels)
            {
                string label = $"{decibels} dBFS";
                labels[decibels - MinimumDb] = (label, label + "  CLIP");
            }

            return labels;
        }

        private static VisualElement Track(VisualElement bar, VisualElement hold)
        {
            VisualElement track = new() { pickingMode = PickingMode.Ignore };
            track.AddToClassList("dxaudio-graph-meter-track");
            bar.AddToClassList("dxaudio-graph-meter-fill");
            bar.pickingMode = PickingMode.Ignore;
            hold.AddToClassList("dxaudio-graph-meter-hold");
            hold.pickingMode = PickingMode.Ignore;
            track.Add(bar);
            track.Add(hold);
            return track;
        }

        private static float Percent(float peak)
        {
            return 100f * Mathf.Clamp01((AudioMath.LinearToDecibels(peak) - Floor) / -Floor);
        }

        internal void Update((bool Active, float Left, float Right, float HeldLeft, float HeldRight, bool Clipped) reading)
        {
            string state = reading.Active ? string.Empty : MutedBySolo;
            int decibels = PeakDb(Mathf.Max(reading.Left, reading.Right));
            if (!string.Equals(_displayed, state, System.StringComparison.Ordinal) || _decibels != decibels || _clipped != reading.Clipped)
            {
                _displayed = state;
                _decibels = decibels;
                _clipped = reading.Clipped;
                _caption.text = reading.Active ? FormatDb(decibels, _clipped) : state;
                EnableInClassList("dxaudio-graph-meter-clipped", _clipped);
            }

            _left.style.width = Length.Percent(Percent(reading.Left));
            _right.style.width = Length.Percent(Percent(reading.Right));
            _heldLeft.style.left = Length.Percent(Mathf.Min(98f, Percent(reading.HeldLeft)));
            _heldRight.style.left = Length.Percent(Mathf.Min(98f, Percent(reading.HeldRight)));
        }
    }
}
