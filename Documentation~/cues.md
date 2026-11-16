# Cues

An `SfxCue` asset describes one sound effect. `SfxPlayer` plays it on a pool of voices.

## Cue fields

| Field                        | Does                                                                                                               |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| **Key**                      | The name `Play(string)` looks up in the library. Optional for cues played by reference.                            |
| **Variants**                 | The takes. One clip each, with its own start offset. A take is drawn at random, never the same one twice in a row. |
| **Volume**                   | Gain for this cue, applied on the voice.                                                                           |
| **Pitch Jitter**             | Random pitch either side of 1. Values from 0.05 to 0.15 read as natural.                                           |
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
