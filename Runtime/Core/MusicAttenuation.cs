namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>How far the music is held back: a gain, and how much of its top and bottom is cut.</summary>
    /// <remarks>
    /// One value for all three because ducks compose by taking the most restrictive of each, and a duck that
    /// only quietens sounds different from one that also muffles. Equality is quantized below what anyone can
    /// hear, so two ducks that agree to a hundredth of a percent count as one and nothing re-fades.
    /// </remarks>
    public readonly struct MusicAttenuation : IEquatable<MusicAttenuation>
    {
        public const float FullVolumeScale = 1f;
        public const float SilentVolumeScale = 0f;

        /// <summary>A low-pass cutoff at the top of hearing, which filters nothing.</summary>
        public const float OpenLowPassCutoffHz = 22_000f;

        /// <summary>A high-pass cutoff at the bottom of hearing, which filters nothing.</summary>
        public const float OpenHighPassCutoffHz = 10f;

        private const float VolumeQuantum = 0.0001f;
        private const float CutoffQuantumHz = 0.5f;

        public static readonly MusicAttenuation Unattenuated = new(FullVolumeScale, OpenLowPassCutoffHz, OpenHighPassCutoffHz);

        public static readonly MusicAttenuation Silent = new(SilentVolumeScale, OpenLowPassCutoffHz, OpenHighPassCutoffHz);

        public bool IsUnattenuated => Equals(Unattenuated);

        public readonly float volumeScale;
        public readonly float lowPassCutoffHz;
        public readonly float highPassCutoffHz;

        /// <remarks>
        /// Every input is clamped into the audible band, and the high-pass never rises above the low-pass:
        /// crossed filters cut everything, which is a silent duck nobody asked for.
        /// </remarks>
        public MusicAttenuation(float volumeScale, float lowPassCutoffHz, float highPassCutoffHz)
        {
            this.volumeScale = AudioMath.Clamp01(volumeScale);
            this.lowPassCutoffHz = BandedCutoffHz(lowPassCutoffHz);
            this.highPassCutoffHz = Math.Min(BandedCutoffHz(highPassCutoffHz), this.lowPassCutoffHz);
        }

        public static float BandedCutoffHz(float cutoffHz)
        {
            return AudioMath.Clamp(cutoffHz, OpenHighPassCutoffHz, OpenLowPassCutoffHz);
        }

        /// <summary>The quietest volume, lowest low-pass and highest high-pass of the two.</summary>
        public static MusicAttenuation MostRestrictive(MusicAttenuation left, MusicAttenuation right)
        {
            float quietest = Math.Min(left.volumeScale, right.volumeScale);
            float lowestLowPass = Math.Min(left.lowPassCutoffHz, right.lowPassCutoffHz);
            float highestHighPass = Math.Max(left.highPassCutoffHz, right.highPassCutoffHz);
            return new MusicAttenuation(quietest, lowestLowPass, highestHighPass);
        }

        /// <summary>The attenuation <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
        /// <remarks>Cutoffs move in log frequency, which is how the ear hears a filter sweep.</remarks>
        public static MusicAttenuation Lerp(MusicAttenuation from, MusicAttenuation to, float t)
        {
            float clamped = AudioMath.Clamp01(t);
            float volume = AudioMath.Lerp(from.volumeScale, to.volumeScale, clamped);
            float lowPass = LerpLog(from.lowPassCutoffHz, to.lowPassCutoffHz, clamped);
            float highPass = LerpLog(from.highPassCutoffHz, to.highPassCutoffHz, clamped);
            return new MusicAttenuation(volume, lowPass, highPass);
        }

        private static int Quantized(float value, float quantum)
        {
            return (int)Math.Round(value / quantum);
        }

        private static float LerpLog(float from, float to, float t)
        {
            double logFrom = Math.Log(Math.Max(from, OpenHighPassCutoffHz));
            double logTo = Math.Log(Math.Max(to, OpenHighPassCutoffHz));
            return (float)Math.Exp(logFrom + ((logTo - logFrom) * t));
        }

        public bool Equals(MusicAttenuation other)
        {
            return Quantized(volumeScale, VolumeQuantum) == Quantized(other.volumeScale, VolumeQuantum)
                && Quantized(lowPassCutoffHz, CutoffQuantumHz) == Quantized(other.lowPassCutoffHz, CutoffQuantumHz)
                && Quantized(highPassCutoffHz, CutoffQuantumHz) == Quantized(other.highPassCutoffHz, CutoffQuantumHz);
        }

        public override bool Equals(object obj)
        {
            return obj is MusicAttenuation other && Equals(other);
        }

        // Equals and the hash read the same integers, so equal values always hash the same.
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Quantized(volumeScale, VolumeQuantum);
                hash = (hash * 397) ^ Quantized(lowPassCutoffHz, CutoffQuantumHz);
                return (hash * 397) ^ Quantized(highPassCutoffHz, CutoffQuantumHz);
            }
        }

        public override string ToString()
        {
            return $"volume x{volumeScale:0.###}, low-pass {lowPassCutoffHz:0}Hz, high-pass {highPassCutoffHz:0}Hz";
        }
    }
}
