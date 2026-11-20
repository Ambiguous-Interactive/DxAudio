namespace WallstopStudios.DxAudio
{
    internal readonly struct SfxPlayback
    {
        internal readonly SfxVariant Variant;
        internal readonly float StartSeconds;
        internal readonly float Pitch;
        internal readonly float BaseVolume;
        internal readonly float Volume;
        internal readonly float LevelDecibels;
        internal readonly float LowPassHertz;
        internal readonly bool FollowedTake;

        internal SfxPlayback(SfxVariant variant, float startSeconds, float pitch, float baseVolume, float volume, float levelDecibels, float lowPassHertz, bool followedTake)
        {
            Variant = variant;
            StartSeconds = startSeconds;
            Pitch = pitch;
            BaseVolume = baseVolume;
            Volume = volume;
            LevelDecibels = levelDecibels;
            LowPassHertz = lowPassHertz;
            FollowedTake = followedTake;
        }
    }
}
