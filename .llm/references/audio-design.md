# Audio Design

Durable design facts and invariants. History and measurements live in `progress/`; this file carries only what the next
change must preserve.

## Layers

| Layer             | Unity dependency | Responsibility                                              |
| ----------------- | ---------------- | ----------------------------------------------------------- |
| `Runtime/Core`    | none             | Arbitration, selection, throttling, ramps, ducks, overrides |
| `Runtime/Assets`  | ScriptableObject | Data: cues, variants, tracks, libraries, ducks              |
| `Runtime/Players` | MonoBehaviour    | Owning `AudioSource` pools and the update loop              |

`UnityObjects.cs` is the only Unity-aware shim in `Core`. A new `UnityEngine` reference in `Core` is a defect, not a
convenience.

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
2. Equal or higher priority drops the incoming cue. Strictly lower priority may be stolen, least important first, then
   oldest.
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

## Clip lifetime

- A player never owns a clip, and reads it only through a voice's `AudioSource` at the moment of use. Unity clears a
  source's clip when that clip's asset is destroyed, and leaves the clip assigned when its audio data is unloaded, so a
  cue or track that points at one is not playable.
- A dead clip still leaves its source reporting playback with no sound. Never trust `isPlaying` alone: a voice is busy
  only while it holds a clip whose data can play and it reports playback. `VoiceClip` is the one place both players ask,
  so the loop and music paths agree.
- Gone means `AudioDataLoadState.Unloaded` or `Failed`: unloaded data was released on purpose, and a failed load never
  completes. `Loading` and `Partially` play, so they are fine, and a streaming clip reports them while it is healthy.
- The loss is read from a source that reports playback, not from the asset. A fresh import reports `Unloaded` until
  something loads it, and a source that starts a clip loads its data as it starts, so requiring `isPlaying` keeps a clip
  that loads on play from being mistaken for a released one in the frame it starts. A destroyed clip clears the source's
  clip outright and is reclaimed whether or not the source reports playback. Data unloaded while the listener is paused
  is detected when playback resumes.
- A held loop whose clip or source is gone is freed by `SfxPlayer`'s frame sweep, and every voice whose clip is gone is
  stopped when the pool is next read. A music voice whose clip or data is gone is stopped in `MusicPlayer.Tick`, so its
  context can play again.
- Define the outcome, not the timing: a released clip frees a held loop within a frame while the player component is
  enabled, and the stale loop handle stops nothing after it. A one-shot voice is not stopped by that sweep; it makes no
  sound and is not busy, and the next `Play` stops it while it reads the pool.
- Asset-level checks (`SfxVariant.IsPlayable`, `MusicTrack.IsPlayable`) stay null-only. They answer whether an asset is
  authored; data that Unity unloaded under a live asset is a playback question, not an authoring one.
- Measured on 6000.6.7f1, host Editor, against a committed imported clip: `UnloadAudioData` returns true and leaves
  `loadState` at `Unloaded` while the source keeps `isPlaying` true. `Play` on a clip whose data is unloaded reloads it
  (`loadState` reads `Loaded` right after), so a source that starts again recovers by itself, and only a source already
  playing when the data went away is stranded. A freshly imported clip reads `Unloaded` until something loads it.
- The test fixture `Tests/Editor/Unity/Data/TestTone.wav` is an exception to the generated-clip rule: `AudioClip.Create`
  cannot unload its data, so only an imported asset can exercise this state.

## Allocation budget

| Path                                           | Budget                                |
| ---------------------------------------------- | ------------------------------------- |
| `SfxPlayer.Play`                               | 0 bytes per call                      |
| Per-frame player update                        | 0 bytes                               |
| Duck compose per frame                         | 0 bytes                               |
| `DescribeDucks()`                              | allowed to allocate; diagnostics only |
| `AudioDiagnosticsOverlay.Sample`, switched off | 0 bytes                               |
| `AudioDiagnosticsOverlay.Sample`, switched on  | allowed to allocate; diagnostics only |

Per-frame time: music duck application at three voices stays under 1.5 us per frame at p95, with no allocation. Measure
with [measure-audio-behaviour](../skills/measure-audio-behaviour/SKILL.md). A budget is only real when a probe pins it.

## Device and format notes

- `AudioSource` configuration is per pooled voice and owned by `SfxPlayer`. Do not reconfigure an `AudioSource` per play
  if the same settings can be applied once.
- Spatial blend comes from the cue. A flat play must not set a position.
- `Time.unscaledTime` orders stealing so hit-stop does not reorder voices.

## Open questions

Mixer-group routing was measured against per-source writes for ducking and does not replace composing in package code;
see [product-decisions](./product-decisions.md#ducks-compose-in-package-code).

- Streaming versus decompressed clips for long music tracks.
