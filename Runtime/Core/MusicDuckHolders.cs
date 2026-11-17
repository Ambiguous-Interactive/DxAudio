namespace WallstopStudios.DxAudio
{
    using System;

    /// <summary>How many things can hold one duck reason down at the same time.</summary>
    /// <remarks>
    /// This decides what a release means. A menu is open or it is not, so asserting it twice asserts one fact
    /// twice and one close ends it. A dialogue is an object with a lifetime, several can be alive at once, and
    /// the music has to stay down until the last of them is gone.
    ///
    /// Stated rather than inferred, because neither default is safe. Counting a state's repeated assertions
    /// leaves the music held down forever after one release; treating a claim as a state raises the music
    /// under a speaker who is still talking.
    /// </remarks>
    public enum MusicDuckHolders
    {
        [Obsolete("Every duck reason must declare its holders.", true)]
        Unknown = 0,

        /// <summary>The reason is a state that is on or off. Asserting it again changes nothing; one release ends it.</summary>
        One = 1,

        /// <summary>The reason is a claim several holders can make at once. The duck ends with the last release.</summary>
        Many = 2,
    }

    /// <summary>Why the music is held back. One per system allowed to hold it down.</summary>
    /// <remarks>
    /// <c>MusicDuck</c> is the authored asset that implements this. A game can also implement it on a
    /// plain object for a reason that only code raises.
    /// </remarks>
    public interface IMusicDuckReason
    {
        MusicDuckHolders Holders { get; }
    }
}
