namespace WallstopStudios.DxAudio
{
    /// <summary>Supplies an allocation-free random draw for SFX playback.</summary>
    public interface ISfxRandom
    {
        /// <summary>Returns a uniformly distributed value in [0, 1).</summary>
        double NextDouble();
    }
}
