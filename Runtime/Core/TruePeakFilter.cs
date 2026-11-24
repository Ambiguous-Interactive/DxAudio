namespace WallstopStudios.DxAudio
{
    using System;
    using System.Numerics;

    /// <summary>Estimates the highest peak across all listener channels, including between samples.</summary>
    internal sealed class TruePeakFilter
    {
        // Unity's widest speaker layout is 7.1.
        internal const int MaxChannels = 8;
        private const int Taps = 12;

        // ITU-R BS.1770-5, Annex 2. Each row contains the four phases for one history sample.
        private static readonly Vector4[] Coefficients =
        {
            new(0.001708984375f, -0.0291748046875f, -0.0189208984375f, -0.00830078125f),
            new(0.010986328125f, 0.029296875f, 0.0330810546875f, 0.014892578125f),
            new(-0.0196533203125f, -0.0517578125f, -0.0582275390625f, -0.026611328125f),
            new(0.033203125f, 0.089111328125f, 0.1015625f, 0.047607421875f),
            new(-0.0594482421875f, -0.16650390625f, -0.2003173828125f, -0.102294921875f),
            new(0.1373291015625f, 0.465087890625f, 0.77978515625f, 0.97216796875f),
            new(0.97216796875f, 0.77978515625f, 0.465087890625f, 0.1373291015625f),
            new(-0.102294921875f, -0.2003173828125f, -0.16650390625f, -0.0594482421875f),
            new(0.047607421875f, 0.1015625f, 0.089111328125f, 0.033203125f),
            new(-0.026611328125f, -0.0582275390625f, -0.0517578125f, -0.0196533203125f),
            new(0.014892578125f, 0.0330810546875f, 0.029296875f, 0.010986328125f),
            new(-0.00830078125f, -0.0189208984375f, -0.0291748046875f, 0.001708984375f),
        };

        private readonly float[] _history = new float[MaxChannels * 2 * Taps];
        private int _channels;
        private int _write;

        internal void Reset()
        {
            Array.Clear(_history, 0, _history.Length);
            _write = 0;
        }

        internal float Observe(float[] samples, int offset, int count, int channels)
        {
            if (_channels != channels)
            {
                Reset();
                _channels = channels;
            }

            float samplePeak = 0f;
            float phasePeak = 0f;
            int lastFrame = offset + count - channels;
            for (int frame = offset; frame <= lastFrame; frame += channels)
            {
                int start = _write;
                for (int channel = 0; channel < channels; ++channel)
                {
                    float sample = samples[frame + channel];
                    float magnitude = Math.Abs(sample);
                    if (magnitude <= float.MaxValue)
                    {
                        samplePeak = Math.Max(samplePeak, magnitude);
                    }
                    else
                    {
                        sample = 0f;
                    }

                    // Duplicate the circular history so each FIR read is contiguous.
                    _history[start] = sample;
                    _history[start + Taps] = sample;
                    float phase0 = 0f;
                    float phase1 = 0f;
                    float phase2 = 0f;
                    float phase3 = 0f;
                    for (int tap = 0; tap < Taps; ++tap)
                    {
                        float input = _history[start + tap];
                        Vector4 taps = Coefficients[tap];
                        phase0 += input * taps.X;
                        phase1 += input * taps.Y;
                        phase2 += input * taps.Z;
                        phase3 += input * taps.W;
                    }

                    phasePeak = Math.Max(phasePeak, Math.Abs(phase0));
                    phasePeak = Math.Max(phasePeak, Math.Abs(phase1));
                    phasePeak = Math.Max(phasePeak, Math.Abs(phase2));
                    phasePeak = Math.Max(phasePeak, Math.Abs(phase3));
                    start += 2 * Taps;
                }

                _write = _write == 0 ? Taps - 1 : _write - 1;
            }

            return phasePeak <= float.MaxValue ? Math.Max(samplePeak, phasePeak) : samplePeak;
        }
    }
}
