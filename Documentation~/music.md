# Music

`MusicPlayer` crossfades between contexts. A context is a name such as `title`, `level-1` or `town`.

## Set up

1. Choose **Create > Wallstop Studios > DxAudio > Music Track**. Set **Context**, assign the clip, and set **Loop**, **Volume Scale** (the
   track's trim) and **Start Time Seconds**.
2. Choose **Create > Wallstop Studios > DxAudio > Music Library**, add the tracks, and set **Fade In Seconds** and **Fade Out Seconds**
   with their ease curves.
3. Add a **Music Player** component and assign the library.

## Clip load type

Unity holds a clip's audio data in one of three ways, and the choice lives in the clip's **Load Type** import setting,
not in playback. Set **Clip Load Type** on the track, then press **Apply \<type\> to the clip** in the track inspector
to write it:

- **Auto** (default) streams a clip longer than 30 seconds and decompresses a clip of 30 seconds or less.
- **Decompress On Load** keeps the whole decoded clip in memory, so playback never touches storage.
- **Compressed In Memory** keeps the compressed clip and decodes it as it plays.
- **Streaming** reads from disk as it plays, and reads again at every loop.

Measured in the host editor on Unity 6000.6.7f1 with 44.1 kHz stereo clips:

| Load type            | 20 s clip | 240 s clip | Load call   |
| -------------------- | --------- | ---------- | ----------- |
| Decompress On Load   | 3.4 MiB   | 40.4 MiB   | 31 / 367 ms |
| Compressed In Memory | 0.4 MiB   | 4.3 MiB    | 1 ms        |
| Streaming            | 0.2 MiB   | 0.2 MiB    | 1–2 ms      |

A decompressed clip holds about 170 KiB for each second of music, so a long track's memory grows with its length. A
streaming clip keeps a small buffer and pays a disk read at each loop. The editor renders no audio, so the table covers
memory and the main-thread load call, not the audible start. A mismatched clip still plays, because the import setting
wins; **Tools > Wallstop Studios > DxAudio > Validate Libraries** reports the mismatch, and the track inspector applies the track's choice.
The load type belongs to the clip, so every track and cue that uses it takes the same value.

## Play

<!-- snippet: unity statements MusicPlayer _music, MusicTrack track -->

```csharp
_music.PlayContext("level-1");     // crossfades; the same music asked for again keeps playing
_music.Play(track);                // by reference
_music.Stop();                     // fades out
```

Asking for the music that already plays does nothing, so a door trigger or a reload does not restart the track. Two
tracks with the same clip, loop flag, trim, start offset, chain and graph count as the same music, so switching between
their contexts keeps the playhead; a different chain or graph is a different sound and restarts it.

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

| Member              | Reports                                        |
| ------------------- | ---------------------------------------------- |
| `TrackChanged`      | The track that now plays                       |
| `Stopped`           | The music stopped                              |
| `Current`           | The track selected now, override included      |
| `IsPlaying`         | The voice is sounding with a loaded clip       |
| `Attenuation`       | The gain and filters applied this frame        |
| `TargetAttenuation` | The gain and filters the live ducks compose to |

## Fades and low-pass filters

The player fades with its own ramp on the voice's `AudioSource.volume`. It writes the low-pass and high-pass cutoffs to
`AudioLowPassFilter` and `AudioHighPassFilter` components on the voice. A duck's filter settings take effect through
those components, so a duck that changes only volume needs no filter work.

## Mixer groups

A music library, an SFX library, and a cue can each name an `AudioMixerGroup`. A game keeps its own Master, Music and
SFX groups and their sliders. Ducking does not need snapshots, because the player composes attenuation itself and writes
the result to each music voice. `Attenuation` reports the value applied this frame.

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

Both levels default to 1 and clamp to the range from 0 to 1. They scale new and active voices and survive library
changes. Music level changes ramp over 10 ms of update time and multiply the track level, fade, and duck gain.

Muting detaches output and keeps state, so loops and music resume in time. Pause holds each voice at its playhead
and keeps loops, leases and mute state. A cue or track started during the pause waits at its start.
See [voice selection](cues.md#how-a-voice-is-chosen) for how paused cues share the pool.
The player pause settings work independently of `AudioListener.pause`.

For a cue or music track whose clip you load or unload yourself, see [Clip lifetime](cues.md#clip-lifetime).

## Next

[Ducks](ducks.md).

_Snippet compiled in CI._
