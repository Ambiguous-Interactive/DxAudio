namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>Owns the random streams and take history for one player or audition session.</summary>
    internal sealed class SfxPlaybackResolver
    {
        private readonly SfxSelection<SfxCue> _selection = new();
        private Random _takeAndPitch;
        private Random _level;

        internal void Reset(int seed)
        {
            SetSeed(seed);
            ClearHistory();
        }

        internal void SetSeed(int seed)
        {
            _takeAndPitch = new Random(seed);
            _level = new Random(seed ^ 0x51F0A7);
        }

        internal void ClearHistory()
        {
            _selection.Clear();
        }

        internal bool TryResolve(SfxCue cue, out SfxPlayback play)
        {
            int count = cue == null ? 0 : cue.PlayableVariantCount;
            if (count == 0)
            {
                play = default;
                return false;
            }

            bool followed = _selection.TryFollow(cue, cue.FollowsTakeOf, count, out int index);
            if (!followed && !_selection.TryNext(cue, count, (float)_takeAndPitch.NextDouble(), out index))
            {
                play = default;
                return false;
            }

            if (!cue.TryGetPlayableVariant(index, out SfxVariant variant))
            {
                play = default;
                return false;
            }

            float pitch = cue.PitchFor((float)_takeAndPitch.NextDouble());
            float volume = cue.Variation.Volume(cue.Volume, (float)_level.NextDouble(), out float decibels);
            play = new SfxPlayback(variant, cue.PlaybackStartSecondsFor(variant), pitch, cue.Volume, volume, decibels, followed);
            return true;
        }
    }
}
