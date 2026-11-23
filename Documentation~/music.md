---
doc_type: how-to
---

# Music

`MusicPlayer` crossfades between contexts. A context is a name such as `title`, `level-1` or `town`.

## Set up

1. Choose **Create > Wallstop Studios > DxAudio > Music Track**. Set **Context**, assign the clip, and set **Loop**, **Volume Scale** (the
   track's trim) and **Start Time Seconds**.
2. Choose **Create > Wallstop Studios > DxAudio > Music Library**, add the tracks, and set **Fade In Seconds** and **Fade Out Seconds**
   with their ease curves.
3. Add a **Music Player** component and assign the library.

## Clip load type

Set **Clip Load Type** on the track, then press **Apply \<type\> to the clip** in the track inspector.
See [how Unity stores clip data](music-concepts.md#clip-load-type) to choose a load type.

## Play

<!-- snippet: unity statements MusicPlayer _music, MusicTrack track -->

```csharp
_music.PlayContext("level-1");     // crossfades; the same music asked for again keeps playing
_music.Play(track);                // by reference
_music.Stop();                     // fades out
```

See [context switches](music-concepts.md#context-switches) for when playback restarts.

## Overrides

An override plays other music over the context until its lease is released, then hands the context back:

<!-- snippet: unity async MusicPlayer _music, MusicTrack bossTrack, Func<Task> BossFight -->

```csharp
using (_music.Override(bossTrack))
{
    await BossFight();
}
```

`IsOverridden` reports whether an override is live. `ReleaseOverride` releases a lease that outlives the `using` scope.

## Events and state

[Look up music events and state](reference.md#events-and-state).

## Fades and low-pass filters

[Learn how fades and filters work](music-concepts.md#fades-and-low-pass-filters).

## Mixer groups

To route music through your mix, assign the Music group to the music library.
Assign the SFX group to the SFX library. Set a cue's **Mixer Group Override** to route it to another group.

## Volume, mute, and pause

<!-- snippet: unity statements SfxPlayer _sfx, MusicPlayer _music, (float SoundLevel, float MusicLevel, bool SoundOn, bool MusicOn) settings, bool paused -->

```csharp
_sfx.Volume = settings.SoundLevel;
_music.Volume = settings.MusicLevel;
_sfx.Muted = !settings.SoundOn;
_music.Muted = !settings.MusicOn;
_sfx.Paused = paused;
_music.Paused = paused;
```

See [output levels, mute, and pause](music-concepts.md#volume-mute-and-pause) for how these settings affect active voices.

## Next

[Ducks](ducks.md) and [Music playback](music-concepts.md).

_Snippet compiled in CI._
