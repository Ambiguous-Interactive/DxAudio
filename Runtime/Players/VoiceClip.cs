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
        /// <summary>Whether <paramref name="source"/> holds a clip whose audio data can play.</summary>
        internal static bool HoldsSound(AudioSource source)
        {
            return source != null && source.clip != null && !IsDeadData(source.clip);
        }

        /// <summary>Whether <paramref name="source"/> is playing sound rather than reporting playback over a dead clip.</summary>
        internal static bool IsSounding(AudioSource source)
        {
            return HoldsSound(source) && source.isPlaying;
        }

        /// <summary>Whether <paramref name="source"/> has lost the sound it should be playing.</summary>
        /// <remarks>
        /// A destroyed clip clears the source's clip at once, which is a loss whether or not the source reports
        /// playback. Unloaded data only strands a source that was already playing it: a source starting a clip loads the
        /// data as it starts, so no data on a source that is not playing yet is not a loss. That keeps a clip which
        /// loads on play, such as a streaming one, from being mistaken for a released one as it starts.
        /// </remarks>
        internal static bool HasLostSound(AudioSource source)
        {
            if (source == null || source.clip == null)
            {
                return true;
            }

            return source.isPlaying && IsDeadData(source.clip);
        }

        /// <summary>Whether a clip's data cannot make sound: it was unloaded, or its load already failed.</summary>
        private static bool IsDeadData(AudioClip clip)
        {
            // Loading and partial data play, so they are fine. A failed load never completes, and is as gone as data
            // that was unloaded on purpose.
            AudioDataLoadState state = clip.loadState;
            return state == AudioDataLoadState.Unloaded || state == AudioDataLoadState.Failed;
        }
    }
}
