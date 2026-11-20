namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Suggests safe relative levels for short, readable one-shot clips.</summary>
    internal static class SfxCueClipDefaults
    {
        private const int MaximumDurationSeconds = 10;
        private const int MaximumSamples = 2_000_000;
        private const int WindowsPerSecond = 20;
        private const double SilenceRms = 0.001;
        private const float MaximumBalanceCutDecibels = -6f;

        internal static float[] AttenuationsFor(IReadOnlyList<AudioClip> clips)
        {
            float[] attenuation = new float[clips.Count];
            float[] levels = new float[clips.Count];
            float targetLevel = float.PositiveInfinity;
            for (int index = 0; index < clips.Count; ++index)
            {
                if (TryMeasure(clips[index], out float level, out float peak))
                {
                    levels[index] = level;
                    targetLevel = Math.Min(targetLevel, level);
                    attenuation[index] = Math.Min(0f, AudioLoudness.DefaultPeakCeilingDbfs - peak);
                }
                else
                {
                    levels[index] = float.NaN;
                }
            }

            for (int index = 0; index < levels.Length; ++index)
            {
                if (AudioMath.IsFinite(levels[index]))
                {
                    float balance = Math.Max(MaximumBalanceCutDecibels, targetLevel - levels[index]);
                    attenuation[index] = Math.Min(attenuation[index], balance);
                }
            }

            return attenuation;
        }

        private static bool TryMeasure(AudioClip clip, out float level, out float peak)
        {
            if (clip == null || clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.loadState != AudioDataLoadState.Loaded)
            {
                level = default;
                peak = default;
                return false;
            }

            int frequency = clip.frequency;
            int channels = clip.channels;
            long count = (long)clip.samples * channels;
            if (count <= 0 || MaximumSamples < count || (long)frequency * MaximumDurationSeconds < clip.samples)
            {
                level = default;
                peak = default;
                return false;
            }

            float[] samples = new float[(int)count];
            if (!clip.GetData(samples, 0))
            {
                level = default;
                peak = default;
                return false;
            }

            // The loudest 50 ms of energy measures the strike without letting the tail or lead-in set its level.
            int window = Math.Min(Math.Max(1, frequency / WindowsPerSecond) * channels, samples.Length);
            double energy = 0d;
            double loudest = 0d;
            float highest = 0f;
            for (int index = 0; index < samples.Length; ++index)
            {
                float sample = samples[index];
                highest = Math.Max(highest, Math.Abs(sample));
                energy += (double)sample * sample;
                if (window <= index)
                {
                    float removed = samples[index - window];
                    energy -= (double)removed * removed;
                }

                loudest = Math.Max(loudest, energy);
            }

            double rms = Math.Sqrt(loudest / window);
            if (!(SilenceRms <= rms) || !AudioMath.IsFinite(highest))
            {
                level = default;
                peak = default;
                return false;
            }

            level = AudioMath.LinearToDecibels((float)rms);
            peak = AudioMath.LinearToDecibels(highest);
            return true;
        }
    }
}
