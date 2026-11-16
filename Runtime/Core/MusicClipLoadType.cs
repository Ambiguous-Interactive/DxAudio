namespace WallstopStudios.DxAudio
{
    /// <summary>How much of a music clip sits in memory while it plays.</summary>
    /// <remarks>
    /// Unity keeps this on the clip's import settings, not on playback, so a track can only ask for it. The Music
    /// Track inspector writes the resolved value into the clip's importer, and the library check reports a clip
    /// that does not follow its track.
    /// </remarks>
    public enum MusicClipLoadType
    {
        /// <summary>Choose from the clip's length; see <see cref="MusicLoadPolicy" />.</summary>
        Auto = 0,

        /// <summary>Hold the whole decoded clip, so playback never touches storage. Costs the most memory.</summary>
        DecompressOnLoad = 1,

        /// <summary>Hold the compressed clip and decode as it plays. Less memory, more decode work.</summary>
        CompressedInMemory = 2,

        /// <summary>Read and decode from disk as it plays. Costs the least memory, and reads disk at every loop.</summary>
        Streaming = 3,
    }
}
