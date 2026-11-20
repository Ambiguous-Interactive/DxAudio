namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Chooses which pooled voice a cue plays on, and whether taking it cuts something short.</summary>
    /// <remarks>
    /// Never stealing is right between equals: clipping an audible sound is more noticeable than one that
    /// never starts. It is wrong when they are not equal. Footsteps are the most frequent cue, so a burst can
    /// hold every voice and drop the beat the player most needs.
    ///
    /// So an idle voice always wins, and priority decides only when there is none. Among the voices that may
    /// be taken, the least important goes first, and among equals the oldest, which is closest to finishing.
    /// A held voice (a loop) is never taken. A paused voice is not idle, but an equal or higher cue may still take
    /// it, because nothing is audible while it is held.
    /// </remarks>
    public static class SfxVoiceArbiter
    {
        /// <summary>Picks the voice a cue of priority <paramref name="incoming"/> should play on.</summary>
        /// <param name="voices">The pool, in index order.</param>
        /// <param name="incoming">The priority of the cue asking for a voice.</param>
        /// <param name="roundRobinStart">
        /// Where to start looking for an idle voice, so consecutive cues spread across the pool rather than
        /// reusing one slot and cutting themselves off.
        /// </param>
        /// <param name="index">The chosen voice.</param>
        /// <param name="interrupts">Whether the chosen voice is still audible and taking it cuts it short.</param>
        /// <returns><see langword="false"/> when the cue has to be dropped.</returns>
        public static bool TryChoose(ReadOnlySpan<SfxVoiceState> voices, SfxPriority incoming, int roundRobinStart, out int index, out bool interrupts)
        {
            int voiceCount = voices.Length;
            if (0 < voiceCount)
            {
                int start = AudioMath.PositiveMod(roundRobinStart, voiceCount);
                for (int offset = 0; offset < voiceCount; ++offset)
                {
                    int candidate = (start + offset) % voiceCount;
                    if (voices[candidate].IsFree)
                    {
                        index = candidate;
                        interrupts = false;
                        return true;
                    }
                }
            }

            int victim = -1;
            for (int i = 0; i < voiceCount; ++i)
            {
                SfxVoiceState state = voices[i];
                if (!state.CanBeTakenFrom || incoming < state.priority || (incoming == state.priority && !state.isPaused))
                {
                    continue;
                }

                if (victim < 0)
                {
                    victim = i;
                    continue;
                }

                // Ordering by age alone would let a High cue cut a long Medium while a fresh Low sat untouched.
                SfxVoiceState best = voices[victim];
                if (state.priority < best.priority || (state.priority == best.priority && state.startedAtUnscaledTime < best.startedAtUnscaledTime))
                {
                    victim = i;
                }
            }

            if (victim < 0)
            {
                index = default;
                interrupts = default;
                return false;
            }

            index = victim;
            interrupts = true;
            return true;
        }
    }
}
