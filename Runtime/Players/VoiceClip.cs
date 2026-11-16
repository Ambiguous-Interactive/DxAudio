namespace WallstopStudios.DxAudio
{
    using UnityEngine;

    /// <summary>Whether a pool voice still has a clip that can make sound.</summary>
    /// <remarks>
    /// A source reports playback when it has no sound left to make. Unity clears the source's clip when the clip's asset
    /// is destroyed, and leaves it assigned when the clip's audio data is unloaded, so neither the clip nor
    /// <c>isPlaying</c> alone answers the question. Both players ask here so a stranded voice frees at the same moment
    /// on both paths.
    /// </remarks>
    internal static class VoiceClip
    {
        /// <summary>Whether <paramref name="source"/> holds a clip whose audio data is loaded, so it can play.</summary>
        /// <remarks>
        /// Only <see cref="AudioDataLoadState.Unloaded"/> counts as gone. A streaming clip reports
        /// <see cref="AudioDataLoadState.Loading"/> or <see cref="AudioDataLoadState.Partially"/> while it is fine, and a
        /// clip plays its data in when it starts, so a source that is starting one never reads as lost.
        /// </remarks>
        internal static bool HoldsSound(AudioSource source)
        {
            return source != null && source.clip != null && !IsUnloaded(source.clip);
        }

        /// <summary>Whether <paramref name="source"/> is playing sound rather than reporting playback over a dead clip.</summary>
        internal static bool IsSounding(AudioSource source)
        {
            return HoldsSound(source) && source.isPlaying;
        }

        private static bool IsUnloaded(AudioClip clip)
        {
            return clip.loadState == AudioDataLoadState.Unloaded;
        }
    }
}
