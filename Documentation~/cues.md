# Cues

An `SfxCue` asset describes one sound effect. `SfxPlayer` plays it on a pool of voices.

## Cue fields

| Field                        | Does                                                                                                               |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| **Key**                      | The name `Play(string)` looks up in the library. Optional for cues played by reference.                            |
| **Variants**                 | The takes. One clip each, with its own start offset. A take is drawn at random, never the same one twice in a row. |
| **Volume**                   | Gain for this cue, applied on the voice.                                                                           |
| **Pitch Jitter**             | Legacy random pitch around the recording's original pitch.                                                         |
| **Pitch Range**              | Optional minimum and maximum detune, in cents. Playback rate also changes the duration.                            |
| **Level Range**              | Random attenuation, in dB. The cue volume sets the maximum level.                                                  |
| **Minimum Interval Seconds** | Rate limit. A play inside the interval is dropped as `Throttled`.                                                  |
| **Priority**                 | `Low`, `Medium`, `High`. Decides which voice an incoming cue may take.                                             |
| **Loop**                     | Marks the cue as a held state. Start it with `TryPlayLooping`.                                                     |
| **Ignores Listener Pause**   | Keeps playing while `AudioListener.pause` is set, for menus over a paused game.                                    |
| **Mixer Group Override**     | Routes this cue to a mixer group instead of the library's.                                                         |
| **Spatial Blend**            | 0 plays flat. Above 0 plays at a position with falloff.                                                            |
| **Falloff Start/End Units**  | Full volume within the start distance, silent past the end distance, linear in between.                            |
| **Heard Centred**            | Fades with distance but never pans left or right.                                                                  |
| **Follows Take Of**          | Plays the same take number as another cue, so two halves of one performance match.                                 |

Note: an `AudioClip` carries no loop flag. Looping is a property of how the cue is started, not of the clip.

## Author and audition variation

1. Add two or more takes of the same sound. For a bell, record strikes at different points on the bell.
2. Select **Apply Subtle Preset** to start with -50 to +50 cents and -1.5 to 0 dB. Use 0 dB at both ends for a fixed level.
3. Open the cue in **Tools > DxAudio > Authoring**. Press **Play Cue** for one strike or **Play Series** for eight consecutive strikes.
4. Check the take name, trim, pitch, and level after each strike. **Replay Last** keeps the same take and trim. Select **Bypass variation on replay** to compare it at the original pitch and cue volume.
5. Enter a seed and use **Reset Seed** to repeat the series. For **Follows Take Of**, play the leader in the same window first.

Open the chain or graph from the cue's signal path. The preview uses the cue's own effects and an isolated output.
If your editor supplies no Edit Mode audio, enter Play Mode and audition again. Use a take's **Audition** button to check its trim at the original pitch.

Select **Set Pitch in Cents** to use the pitch range in place of **Pitch Jitter**. The cents range accepts -1,200 to +1,200, and the level range accepts -24 to 0 dB. The player rolls the values once at each accepted play, including the start of a held loop. For code-authored cues, pass `SfxVariation` to `SfxCue.CreateWithVariation`.

## Playing

```csharp
_sfx.Play(cue);                        // flat, by reference
_sfx.Play(cue, transform.position);    // placed
_sfx.Play("door/creak");               // by key, through the library
```

`Play` returns `false` when nothing started. `CueDropped` reports the reason.

## Loops

A looping cue stands for a state. Whoever starts it stops it:

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

The player never owns a clip. If the game unloads or destroys a clip while it plays, the player frees the voice within a
frame instead of leaving it held and silent. A cue whose clip is gone is refused as `NotPlayable`. See the
[README clip lifetime table](../README.md#clip-lifetime).

## Next

[Music](music.md).
