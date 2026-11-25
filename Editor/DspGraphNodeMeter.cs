namespace WallstopStudios.DxAudio.Editor
{
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DspGraphNodeMeter : VisualElement
    {
        private const float Floor = -60f;
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
            // A blown-out signal still needs a finite readout and a visible clip indicator.
            if (float.IsPositiveInfinity(peak))
            {
                return 999;
            }

            return 0f < peak ? Mathf.RoundToInt(AudioMath.LinearToDecibels(peak)) : int.MinValue;
        }

        internal static string FormatDb(int decibels)
        {
            return decibels == int.MinValue ? "−∞ dBFS" : $"{decibels} dBFS";
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
            string state = reading.Active ? string.Empty : "Muted by Solo";
            int decibels = PeakDb(Mathf.Max(reading.Left, reading.Right));
            if (!string.Equals(_displayed, state, System.StringComparison.Ordinal) || _decibels != decibels || _clipped != reading.Clipped)
            {
                _displayed = state;
                _decibels = decibels;
                _clipped = reading.Clipped;
                _caption.text = reading.Active ? FormatDb(decibels) + (_clipped ? "  CLIP" : string.Empty) : state;
                EnableInClassList("dxaudio-graph-meter-clipped", _clipped);
            }

            _left.style.width = Length.Percent(Percent(reading.Left));
            _right.style.width = Length.Percent(Percent(reading.Right));
            _heldLeft.style.left = Length.Percent(Mathf.Min(98f, Percent(reading.HeldLeft)));
            _heldRight.style.left = Length.Percent(Mathf.Min(98f, Percent(reading.HeldRight)));
        }
    }
}
