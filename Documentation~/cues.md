# Cues

An `SfxCue` asset describes one sound effect. `SfxPlayer` plays it on a pool of voices.

## Create a cue from clips

Select recordings of the same sound in the Project window. Choose **Assets > Wallstop Studios > DxAudio > Create Cue from Selected Clips**.
DxAudio creates the cue, enables Shuffle for multiple takes, and opens it for audition. Pitch stays at the original
rate. Choose **Subtle** if you want detune and level variation.

DxAudio compares the loudest 50 ms of each readable take and lowers louder takes by up to 6 dB. It also leaves
3 dB of sample peak headroom before effects. Silent takes and clips with unavailable PCM keep their original level.
Analysis uses loaded, decompressed clips up to 10 seconds and 2 million samples. The new cue stores the levels.

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

Note: an `AudioClip` carries no loop flag. Looping is a property of how the cue is started, not of the clip.

## Author and audition variation

1. Drop AudioClips into the cue's take drop area. For a bell, record strikes at different points on the bell.
2. Audition each take and reduce the level of louder recordings as needed. Enable **Shuffle Takes** to hear every playable take in a cycle.
3. Select **Capture A** to keep the current settings. Try **Subtle** for -50 to +50 cents and -1.5 to 0 dB, or **Pitch Stable** for original pitch and a fixed level.
4. Press **Play A** and **Play B** to hear the captured settings and your current edits from the same seed. Capture A again after changing a recording or trim.
5. Use **Play Cue** for one strike or **Play Series** for eight consecutive strikes. Check the take name, trim, pitch, level, and cutoff.
6. **Replay Last** keeps the same take and trim. **Bypass variation on replay** keeps its authored take level and uses the original pitch and brightness.

Use the Inspector or **Tools > Wallstop Studios > DxAudio > Authoring**. The cue preview uses the cue's chain and graph and an isolated output.
If your editor supplies no Edit Mode audio, enter Play Mode and audition again. Enter a seed and use **Reset Seed** to repeat the
main series. For **Follows Take Of**, play the leader first in the same main preview. A matching follower plays the leader's
take and restarts the fallback shuffle cycle. A/B starts each side with fresh take history.

Select **Set Pitch in Cents** to use the pitch range in place of **Pitch Jitter**. The cents range accepts -1,200 to +1,200,
and the level range accepts -24 to 0 dB. The low-pass cutoff is sampled evenly across octaves. The player draws the values once
at each accepted play, including the start of a held loop. For code-authored cues, pass `SfxVariation` to
`SfxCue.CreateWithVariation` and give each `SfxVariant` its attenuation in the three-argument constructor.

## Playing

<!-- snippet: unity statements SfxPlayer _sfx, SfxCue cue -->

```csharp
_sfx.Play(cue);                        // flat, by reference
_sfx.Play(cue, transform.position);    // placed
_sfx.Play("door/creak");               // by key, through the library
```

`Play` returns `false` when nothing started. `CueDropped` reports the reason.

To supply a custom random generator, set `SfxPlayer.RandomFactory` before assigning the library. Return a fresh
`ISfxRandom` for each seed. The player requests independent streams for take and pitch, and for level. To use a
unity-helpers generator, implement `ISfxRandom.NextDouble()` by forwarding to the generator's `IRandom.NextDouble()`.
Low-pass uses its own internal stream. The player prepares shuffle storage for cues in the library. Call `Warm(cue)` before
playing a cue held by reference to move its shuffle and DSP setup out of the first play.

## Loops

A looping cue stands for a state. Whoever starts it stops it:

<!-- snippet: unity statements SfxPlayer _sfx, SfxCue _hum, SfxLoopHandle _humHandle -->

```csharp
if (_sfx.TryPlayLooping(_hum, out SfxLoopHandle hum))
{
    _humHandle = hum;
}

_sfx.StopLoop(_humHandle); // a stale handle stops nothing
```

The handle carries a lease number. A handle from a stopped loop, or from a voice that another cue reused, matches
nothing, so stopping it does nothing. `StopAll` ends every voice, loops included, and clears rate limits.

The library's **Looping Voice Limit** caps how many loops run at once. A loop beyond the cap is dropped as `LoopLimit`.

## How a voice is chosen

| Situation                                | Result                                                                                             |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------- |
| A voice is idle                          | The cue takes it, searching round-robin so cues spread across the pool.                            |
| Every voice is busy                      | The cue takes the least important voice of **strictly lower** priority, oldest first.              |
| Only equal or higher priority is playing | The cue is dropped as `NoVoice`. Cutting off an audible sound is worse than one that never starts. |
| An equal priority voice is paused        | The cue takes it, because nothing is audible while it is held.                                     |
| A voice holds a loop                     | It is never taken.                                                                                 |

A pool too small for the busiest moment drops cues. Raise **Voice Count**, or give important cues a higher priority.

## Clip lifetime

The players never own a clip. A game that loads and unloads clips itself, with Addressables or `Resources`, decides when
a clip goes. These are the rules that make a release safe:

| Moment                                       | What happens                                                                                        |
| -------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Released before its cue plays                | The cue has nothing to play: `Play` returns false and `CueDropped` reports `NotPlayable`.           |
| Released while an SFX loop plays it          | The hold ends within a frame and the handle then stops nothing.                                     |
| Released while an SFX one-shot plays         | The voice makes no sound and is not busy, so the next `Play` stops it while it reads the pool.      |
| Released while its music plays               | The music stops. Asking for its context again returns true and starts it once the track has a clip. |
| Its data unloaded while an SFX loop plays it | Same as a release: the hold ends within a frame and the handle then stops nothing.                  |
| Its data unloaded while its music plays      | `IsPlaying` reads false, and the next frame stops the voice so the music after it starts cleanly.   |

Unity clears a source's clip when that clip's asset is destroyed, and keeps the clip assigned when its audio data is
unloaded, by `AudioClip.UnloadAudioData`, `Resources.UnloadAsset`, or an Addressables release. In both cases the source
still reports playback with no sound. The players check the clip and its load state instead of trusting that report, so
a voice never stays stranded and a music context never sticks as silent. `Unloaded` and `Failed` count as gone, and the
check reads a voice that reports playback, so a streaming clip that reports `Loading` or `Partially` as it starts is not
mistaken for a released one.

Releasing early is safe but cuts the sound short. `Stop` and `StopLoop` end a sound on purpose, so let a music fade
finish before unloading its clip if you want to hear it. Load a clip again with `AudioClip.LoadAudioData`, or start it
again and Unity loads it as it plays.

## Next

[Music](music.md).

_Snippet compiled in CI._
