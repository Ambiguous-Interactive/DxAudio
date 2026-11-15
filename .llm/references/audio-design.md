# Audio Design

Durable design facts and invariants. History and measurements live in `progress/`; this file carries only what the next change must preserve.

## Layers

| Layer             | Unity dependency | Responsibility                                              |
| ----------------- | ---------------- | ----------------------------------------------------------- |
| `Runtime/Core`    | none             | Arbitration, selection, throttling, ramps, ducks, overrides |
| `Runtime/Assets`  | ScriptableObject | Data: cues, variants, tracks, libraries, ducks              |
| `Runtime/Players` | MonoBehaviour    | Owning `AudioSource` pools and the update loop              |

`UnityObjects.cs` is the only Unity-aware shim in `Core`. A new `UnityEngine` reference in `Core` is a defect, not a convenience.

## Sound-effects state machine

```text
Play(cue)
  -> resolve cue (reference or key)
  -> throttle check (per-cue window)          -> dropped: Throttled
  -> choose voice (idle, else lower priority)  -> dropped: NoVoice
  -> loop limit check                          -> dropped: LoopLimit
  -> take variant (round-robin, no repeat)
  -> start the voice, record start time
     -> looping: return a handle; the voice is held
     -> one-shot: free the voice on completion
```

Rules:

1. A held (looping) voice is never stolen. Stopping uses the handle's voice generation; a stale handle stops nothing.
2. Equal or higher priority drops the incoming cue. Strictly lower priority may be stolen, least important first, then oldest.
3. Round-robin start spreads consecutive cues across the pool.
4. A take is never repeated twice in a row while any other take exists.
5. `Play` must not allocate.

## Music state machine

```text
PlayContext(key) / Play(track)
  -> same audible track: keep playing
  -> different track: crossfade (fade out old, fade in new)
  -> returning while the same track still fades out: fade back in from its current level
Override(track) -> lease
  -> newest override owns output; releasing it hands back to the context
  -> a context change clears the override
Stop() -> fade out
```

## Duck algebra

- A duck contributes `(volumeDb, lowPassHz, highPassHz)`.
- Composition: `volumeDb = min(all)`, `lowPassHz = min(all)`, `highPassHz = max(all)`.
- Composition is commutative and idempotent. Adding the same reason twice adds nothing.
- `One` holders: one live contribution regardless of assert count.
- `Many` holders: one contribution until the last holder releases.
- Leases are copy-safe values with a generation. Stale, foreign, and default leases release nothing.
- A destroyed holder is released on the next frame.

## Allocation budget

| Path                    | Budget                                |
| ----------------------- | ------------------------------------- |
| `SfxPlayer.Play`        | 0 bytes per call                      |
| Per-frame player update | 0 bytes                               |
| Duck compose per frame  | 0 bytes                               |
| `DescribeDucks()`       | allowed to allocate; diagnostics only |

Measure with [measure-audio-behaviour](../skills/measure-audio-behaviour/SKILL.md). A budget is only real when a probe pins it.

## Device and format notes

- `AudioSource` configuration is per pooled voice and owned by `SfxPlayer`. Do not reconfigure an `AudioSource` per play if the same settings can be applied once.
- Spatial blend comes from the cue. A flat play must not set a position.
- `Time.unscaledTime` orders stealing so hit-stop does not reorder voices.

## Open questions

- Mixer-group routing versus per-source volume: benchmark both before adding an `AudioMixer` dependency.
- Addressables and clip lifetime: unload policy for a cue whose clips are released while a voice holds one.
- Streaming versus decompressed clips for long music tracks.
