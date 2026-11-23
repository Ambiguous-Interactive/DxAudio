---
doc_type: explanation
---

# Cue playback

An SFX library sets the size of the voice pool. Each voice reuses one `AudioSource`.

## How a voice is chosen

| Situation                                | Result                                                                                             |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------- |
| A voice is idle                          | The cue takes it, searching round-robin so cues spread across the pool.                            |
| Every voice is busy                      | The cue takes the least important voice of **strictly lower** priority, oldest first.              |
| Only equal or higher priority is playing | The cue is dropped as `NoVoice`. Cutting off an audible sound is worse than one that never starts. |
| An equal priority voice is paused        | The cue takes it, because nothing is audible while it is held.                                     |
| A voice holds a loop                     | It is never taken.                                                                                 |

A pool too small for the busiest moment drops cues. **Voice Count** sets the pool size. Higher priority lets a cue take a lower priority voice.

## Clip lifetime

The players never own a clip. A game that loads and unloads clips itself, with Addressables or `Resources`, decides when
a clip goes. These are the rules that make a release safe:

| Moment                                      | What happens                                                                                        |
| ------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Released before its cue plays               | The cue has nothing to play: `Play` returns false and `CueDropped` reports `NotPlayable`.           |
| Released while an SFX loop plays it         | The hold ends within a frame and the handle then stops nothing.                                     |
| Released while an SFX one-shot plays        | The voice makes no sound and is not busy, so the next `Play` stops it while it reads the pool.      |
| Released while its music plays              | The music stops. Asking for its context again returns true and starts it once the track has a clip. |
| Its data unloaded during an active SFX loop | The hold ends within a frame while its source reports playback.                                     |
| Its data unloaded while its music plays     | `IsPlaying` reads false, and the next frame stops the voice so the music after it starts cleanly.   |

`AudioClip.UnloadAudioData` leaves the clip assigned. DxAudio treats `Unloaded` and `Failed` as lost data when a source
reports playback. A paused source reports no playback. Stop a paused loop before unloading its audio data to free its
voice. Call `AudioClip.LoadAudioData` before playing a clip whose data you unloaded. If Unity loads it in the background,
wait for the clip to finish loading.

`Resources.UnloadAsset` invalidates the clip object. Load a new clip and assign that object to the cue or track.
If releasing an Addressables handle unloads the clip asset, load it again and assign the new clip.

Stop a cue or loop before unloading its clip when you want to end the sound. Let a music fade finish before unloading
its clip if you want to hear the full fade.

See [Cues](cues.md) to author and play a sound effect.
