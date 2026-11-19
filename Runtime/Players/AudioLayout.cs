namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>The output rate, block size and channel count an audio stage prepares its chains for.</summary>
    /// <remarks>
    /// Unity can report no device before one starts, so every figure falls back to a stereo 48 kHz, 1,024-frame layout
    /// rather than to zero. A device change invalidates the layout: a delay line is sized from the sample rate, so a
    /// prepared chain is wrong the moment the device moves.
    /// </remarks>
    internal sealed class AudioLayout
    {
        private const int FallbackSampleRate = 48_000;
        private const int FallbackBlockSize = 1_024;
        private const int FallbackChannels = 2;

        /// <summary>The output sample rate, or its fallback when the device reports none.</summary>
        public int SampleRate { get; private set; }

        /// <summary>The DSP buffer size, or its fallback when the device reports none.</summary>
        public int BlockSize { get; private set; }

        /// <summary>The device channel count, which is the widest block a stage must run.</summary>
        public int Channels { get; private set; }

        /// <summary>Reads the device figures once, until <see cref="Invalidate" /> asks for them again.</summary>
        public void Refresh()
        {
            if (0 < SampleRate)
            {
                return;
            }

            SampleRate = 0 < AudioSettings.outputSampleRate ? AudioSettings.outputSampleRate : FallbackSampleRate;
            Channels = ChannelCountOf(AudioSettings.speakerMode);
            AudioSettings.GetDSPBufferSize(out int blockSize, out _);
            BlockSize = 0 < blockSize ? blockSize : FallbackBlockSize;
        }

        /// <summary>Makes the next <see cref="Refresh" /> read the device again.</summary>
        public void Invalidate()
        {
            SampleRate = 0;
        }

        private static int ChannelCountOf(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono:
                    return 1;
                case AudioSpeakerMode.Quad:
                    return 4;
                case AudioSpeakerMode.Surround:
                    return 5;
                case AudioSpeakerMode.Mode5point1:
                    return 6;
                case AudioSpeakerMode.Mode7point1:
                    return 8;
                default:
                    // Stereo, Prologic, and an uninitialised device all render two channels, which is what a 2D
                    // source is mixed into.
                    return FallbackChannels;
            }
        }
    }
}
