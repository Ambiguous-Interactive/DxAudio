namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Which part of the spectrum a <see cref="DspFilter" /> keeps.</summary>
    /// <remarks>
    /// Every mode reads the same state-variable core, so a mode change swaps which combination of the low, band and high
    /// outputs leaves the stage. It does not move a knob or rebuild the filter.
    /// </remarks>
    public enum DspFilterMode
    {
        [Obsolete("A filter must declare its mode; Unknown is the value an unset field holds.", true)]
        Unknown = 0,

        /// <summary>Keep the band below the corner and cut above it: a muffled or underwater sound.</summary>
        LowPass = 1,

        /// <summary>Keep the band above the corner and cut below it: a thin or distant sound.</summary>
        HighPass = 2,

        /// <summary>Keep a band around the corner and cut both sides: a radio or telephone sound.</summary>
        BandPass = 3,

        /// <summary>Cut a band around the corner and keep both sides: a hum or a resonance removed.</summary>
        Notch = 4,

        /// <summary>Lift or cut a band around the corner by <see cref="DspFilter.PeakDecibels" />, with unity away from it.</summary>
        Peak = 5,
    }
}
