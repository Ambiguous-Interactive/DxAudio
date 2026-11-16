namespace WallstopStudios.DxAudio
{
    /// <summary>Chooses how a music clip is held, from the track's preference and the clip's length.</summary>
    /// <remarks>
    /// Measured in the editor on Unity 6000.6.7f1 with 44.1 kHz stereo clips: a decompressed clip holds about
    /// 170 KiB for each second of music and spends about 1.6 ms a second loading on the main thread, while a
    /// streaming clip holds a 199 KiB front buffer and returns in about 2 ms. At the 30 second threshold that is
    /// 5 MiB and 48 ms against a disk read at each loop, so a longer track streams and a shorter one decompresses.
    /// </remarks>
    public static class MusicLoadPolicy
    {
        /// <summary>A clip longer than this many seconds streams by default; a shorter one decompresses.</summary>
        public const float StreamingThresholdSeconds = 30f;

        /// <summary>The load type for a clip of <paramref name="lengthSeconds" />, keeping an explicit choice.</summary>
        public static MusicClipLoadType Resolve(float lengthSeconds, MusicClipLoadType requested)
        {
            if (requested != MusicClipLoadType.Auto)
            {
                return requested;
            }

            return StreamingThresholdSeconds < lengthSeconds
                ? MusicClipLoadType.Streaming
                : MusicClipLoadType.DecompressOnLoad;
        }
    }
}
