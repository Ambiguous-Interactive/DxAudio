namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;

    // Shared by Unity wall-time measurements and the Linux instruction regression check.
    internal sealed class DspRenderWorkload
    {
        internal const int Rate = 48_000;
        internal const int Frames = 1_024;
        internal const int Passes = 240;
        internal const int WarmupPasses = 64;
        private const int Channels = 2;

        internal static readonly (int Voices, string Effect, bool Bursts)[] Cases =
        {
            (1, "Delay", false),
            (8, "Delay", false),
            (1, "Noise Gate", false),
            (8, "Noise Gate", false),
            (8, "Noise Gate", true),
            (1, "Chorus", false),
            (8, "Chorus", false),
            (1, "Tremolo", false),
            (8, "Tremolo", false),
            (1, "Phaser", false),
            (8, "Phaser", false),
            (8, "Phaser", true),
            (1, "Reverb", false),
            (8, "Reverb", false),
            (8, "Reverb", true),
            (1, "Stereo Width", false),
            (8, "Stereo Width", false),
        };

        private readonly float[] _input = new float[Frames * Channels];
        private readonly float[] _block = new float[Frames * Channels];
        private readonly DspChain[] _chains;

        internal DspRenderWorkload(int voices, string effect, bool bursts, bool enabled)
        {
            _chains = new DspChain[voices];
            for (int voice = 0; voice < voices; ++voice)
            {
                DspChain chain = new();
                if (enabled)
                {
                    chain.Add(CreateEffect(effect));
                }

                if (!chain.TryPrepare(Rate, Frames, Channels))
                {
                    throw new InvalidOperationException("Could not prepare the DSP render chain.");
                }

                _chains[voice] = chain;
            }

            bool gate = string.Equals(effect, "Noise Gate", StringComparison.Ordinal);
            for (int frame = 0; frame < Frames; ++frame)
            {
                float amplitude = 0.1f;
                if (bursts && gate && frame % 128 < 64)
                {
                    amplitude = 0.0001f;
                }
                else if (bursts && !gate && 64 <= frame)
                {
                    amplitude = 0f;
                }

                _input[frame * Channels] = amplitude * (float)Math.Sin(2d * Math.PI * frame / 64d);
                _input[frame * Channels + 1] = amplitude * (float)Math.Cos(2d * Math.PI * frame / 64d);
            }
        }

        private static DspEffect CreateEffect(string effect)
        {
            return effect switch
            {
                "Delay" => new DspDelay { DelaySeconds = DspDelay.MaximumDelaySeconds, Feedback = DspDelay.MaximumFeedback },
                "Chorus" => new DspChorus { DepthSeconds = DspChorus.MaximumDepthSeconds, RateHertz = DspChorus.MaximumRateHertz },
                "Tremolo" => new DspTremolo { RateHertz = DspTremolo.MaximumRateHertz, Depth = 1f },
                "Phaser" => new DspPhaser { RateHertz = DspPhaser.MaximumRateHertz, Depth = 1f },
                "Reverb" => new DspReverb { DecaySeconds = DspReverb.MaximumDecaySeconds, WetLevel = 1f },
                "Stereo Width" => new DspStereoWidth { Width = DspStereoWidth.MaximumWidth },
                "Noise Gate" => new DspNoiseGate(),
                _ => throw new ArgumentOutOfRangeException(nameof(effect)),
            };
        }

        internal void CopyInput()
        {
            Array.Copy(_input, _block, _input.Length);
        }

        internal void Process(int voice)
        {
            _chains[voice].Process(_block, 0, _block.Length, Channels);
        }
    }
}
