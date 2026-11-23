---
doc_type: reference
---

# DxAudio reference

## Terms

- **Cue.** One sound effect with interchangeable takes, volume, pitch jitter, a rate limit, a priority and 3D falloff.
- **SFX library.** The cues a player looks up by key and the number of voices it owns.
- **Track.** One piece of music with a clip, a loop flag, a trim and a start offset under a context name.
- **Music library.** The tracks a player looks up by context and the fade times.
- **Duck.** A reason to lower and filter the music, with `One` or `Many` holders.
- **Voice.** One `AudioSource` the player owns and reuses. Playing a cue claims a voice.
- **Chain.** An ordered list of effects a cue, a track or a player's bus runs on its voices.

## Cue fields

| Field                        | Does                                                                                                |
| ---------------------------- | --------------------------------------------------------------------------------------------------- |
| **Key**                      | The name `Play(string)` looks up in the library. Optional for cues played by reference.             |
| **Takes**                    | The recordings. Each has its own start offset and level reduction from -24 to 0 dB.                 |
| **Shuffle Takes**            | Plays each available take once per cycle. With this off, a draw excludes only the last take.        |
| **Volume**                   | Gain for this cue, applied on the voice.                                                            |
| **Pitch Jitter**             | Legacy random pitch around the recording's original pitch.                                          |
| **Pitch Range**              | Optional minimum and maximum detune, in cents. Playback rate also changes the duration.             |
| **Level Range**              | Random attenuation, in dB. The cue volume sets the maximum level.                                   |
| **Vary Low-Pass Cutoff**     | Draws a cutoff between 500 and 20,000 Hz. Lower values soften bright sounds before the cue effects. |
| **Minimum Interval Seconds** | Rate limit. A play inside the interval is dropped as `Throttled`.                                   |
| **Priority**                 | `Low`, `Medium`, `High`. Decides which voice an incoming cue may take.                              |
| **Loop**                     | Marks the cue as a held state. Start it with `TryPlayLooping`.                                      |
| **Ignores Listener Pause**   | Keeps playing while `AudioListener.pause` is set, for menus over a paused game.                     |
| **Mixer Group Override**     | Routes this cue to a mixer group instead of the library's.                                          |
| **Spatial Blend**            | 0 plays flat. Above 0 plays at a position with falloff.                                             |
| **Falloff Start/End Units**  | Full volume within the start distance, silent past the end distance, linear in between.             |
| **Heard Centred**            | Fades with distance but never pans left or right.                                                   |
| **Follows Take Of**          | Plays the same take number as another cue, so two halves of one performance match.                  |

The playback call controls looping. An `AudioClip` has no loop flag.

## Duck fields

| Field                       | Does                                                                  |
| --------------------------- | --------------------------------------------------------------------- |
| **Holders**                 | `One` or `Many`. See [holder policies](duck-concepts.md#one-or-many). |
| **Volume Scale**            | Multiplier on the music gain while held.                              |
| **Low Pass Cutoff**         | Lower muffles more. 22000 leaves the low-pass doing nothing.          |
| **High Pass Cutoff**        | Higher thins more. 10 leaves the high-pass doing nothing.             |
| **Apply / Release Seconds** | How long the change takes, with an ease curve for each.               |

## Events and state

| Member              | Reports                                        |
| ------------------- | ---------------------------------------------- |
| `TrackChanged`      | The track that now plays                       |
| `Stopped`           | The music stopped                              |
| `Current`           | The track selected now, override included      |
| `IsPlaying`         | The voice is sounding with a loaded clip       |
| `Attenuation`       | The gain and filters applied this frame        |
| `TargetAttenuation` | The gain and filters the live ducks compose to |

## SFX messages

| Message                 | Kind       | Does                                        |
| ----------------------- | ---------- | ------------------------------------------- |
| `SfxPlayRequest`        | Targeted   | Plays one cue by key, flat or at a position |
| `SfxLoopRequest`        | Targeted   | Holds a looping cue and answers with its id |
| `SfxLoopStopRequest`    | Targeted   | Ends the loop an id names                   |
| `SfxStopAllRequest`     | Targeted   | Stops every voice, loops included           |
| `SfxMuteRequest`        | Untargeted | Sets every effect player's mute state       |
| `SfxLoopStartedMessage` | Broadcast  | Reports a held loop and its id              |
| `SfxCueDroppedMessage`  | Broadcast  | Reports a cue that did not play, and why    |

## Music messages

| Message                    | Kind       | Does                                          |
| -------------------------- | ---------- | --------------------------------------------- |
| `MusicPlayContextRequest`  | Untargeted | Plays the music a context names               |
| `MusicStopRequest`         | Untargeted | Fades the music out                           |
| `MusicDuckRequest`         | Targeted   | Holds the music and answers with its duck id  |
| `MusicUnduckRequest`       | Targeted   | Releases the duck an id names                 |
| `AudioPauseRequest`        | Untargeted | Holds or resumes every effect and music voice |
| `MusicTrackChangedMessage` | Broadcast  | Reports the track that now plays              |
| `MusicStoppedMessage`      | Broadcast  | Reports that the music stopped                |
| `MusicDuckStartedMessage`  | Broadcast  | Reports a held duck and its id                |

## What the package contains

| Folder            | Ships | Contents                                                           |
| ----------------- | ----- | ------------------------------------------------------------------ |
| `Runtime/`        | Yes   | Cues, tracks, ducks, libraries and the two players                 |
| `Editor/`         | Yes   | Cue, duck and music track inspectors, library validation           |
| `Tests/`          | Yes   | EditMode tests, run only when the package is listed in `testables` |
| `Documentation~/` | No    | These pages                                                        |
| `Samples~/`       | No    | The `BasicUsage` sample                                            |

Unity ignores folders whose name ends in `~` and npm does not pack them, so neither adds to a build or a download. A
registry or tarball install has no sample; install from git, or copy `Samples~/BasicUsage` from the repository.
