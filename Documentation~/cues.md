---
doc_type: how-to
---

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

[Look up cue fields](reference.md#cue-fields).

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

[Learn how the player chooses a voice](cue-concepts.md#how-a-voice-is-chosen).

## Clip lifetime

[Check clip lifetime rules](cue-concepts.md#clip-lifetime).

## Next

[Music](music.md) and [Cue playback](cue-concepts.md).

_Snippet compiled in CI._
