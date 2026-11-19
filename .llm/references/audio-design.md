# Audio Design

Durable design facts and invariants. History and measurements live in `progress/`; this file carries only what the next
change must preserve.

## Layers

| Layer             | Unity dependency | Responsibility                                              |
| ----------------- | ---------------- | ----------------------------------------------------------- |
| `Runtime/Core`    | none             | Arbitration, selection, throttling, ramps, ducks, overrides |
| `Runtime/Assets`  | ScriptableObject | Data: cues, variants, tracks, libraries, ducks, chains      |
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

## Pause and mute

- Mute detaches output; pause holds every sounding voice at its playhead. Both keep voice, loop, duck and timer state,
  so resume continues rather than restarts.
- A voice started while paused is paused at its start. A pause is not a stop: a held loop keeps its lease and its
  looping-limit slot. A paused one-shot is not idle: an equal or higher cue may reuse its voice, while a lower cue is
  dropped, because the paused cue will resume.
- `AudioPauseRequest` is untargeted, so every relay sets `Paused` on its own player.

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
| `DspChain.Process`, four stages, stereo block  | 0 bytes                               |
| `SfxPlayer.Play`, cue listed with a chain      | 0 bytes once the voice has met it     |

Per-frame time: music duck application at three voices stays under 1.5 us per frame at p95, with no allocation. Measure
with [measure-audio-behaviour](../skills/measure-audio-behaviour/SKILL.md). A budget is only real when a probe pins it.

## Device and format notes

- `AudioSource` configuration is per pooled voice and owned by `SfxPlayer`. Do not reconfigure an `AudioSource` per play
  if the same settings can be applied once.
- Spatial blend comes from the cue. A flat play must not set a position.
- `Time.unscaledTime` orders stealing so hit-stop does not reorder voices.

## Clip load type

Unity stores a clip's load type in its import settings, so a build cannot change it at play time. A `MusicTrack` records
the choice, `MusicLoadPolicy` resolves `Auto` from the clip's length, and the track inspector applies the result to the
clip's importer on request. Nothing in the runtime reads it. A per-platform override wins over the default settings, so
the inspector reports a write that cannot change the effective type rather than rewriting a platform choice the author
made.

Measured in the host editor on 6000.6.7f1 with 44.1 kHz stereo clips, median of five unload and reload cycles:

| Clip  | Load type            | Memory held | Load call, main thread |
| ----- | -------------------- | ----------- | ---------------------- |
| 20 s  | Decompress On Load   | 3,452 KiB   | 31.2 ms                |
| 20 s  | Compressed In Memory | 373 KiB     | 0.3 ms                 |
| 20 s  | Streaming            | 199 KiB     | 1.0 ms                 |
| 30 s  | Decompress On Load   | 5,175 KiB   | 47.5 ms                |
| 30 s  | Compressed In Memory | 556 KiB     | 0.2 ms                 |
| 30 s  | Streaming            | 199 KiB     | 1.1 ms                 |
| 240 s | Decompress On Load   | 41,351 KiB  | 367.2 ms               |
| 240 s | Compressed In Memory | 4,390 KiB   | 1.0 ms                 |
| 240 s | Streaming            | 199 KiB     | 1.9 ms                 |

Decision: `MusicLoadPolicy` streams above 30 seconds and decompresses at or below it, and a track may override the
choice. At the threshold, decompressed holds 5 MiB and stalls the main thread 48 ms; both grow with track length, while
streaming stays at a 199 KiB front buffer. Edit mode renders no audio, so the audible start and the cost of a
loop-boundary read are not in this table; issue #46 carries that play-mode probe.

## DSP chain

A chain is an ordered list of `DspEffect` stages: order is the sound. Parallel paths are a `DspGraph` (below), which
fans a chain out into branches and sums them back with their latency aligned. Effects shape an interleaved block in
place, so a stage needs no scratch buffer and a voice's audio path allocates nothing.

`DspEffect.LatencySamples` is not zero for everything: the limiter reports its lookahead, and the pitch shifter reports
half its window. A chain sums the figure so two branches that must stay aligned can be told how far apart they are.

Rules for a new effect:

- `Process` runs on the audio thread: no allocation, no log, no lock, no throw.
- `TryPrepare` owns every allocation and must match the channel count the block arrives with. A stage that was never
  prepared, or that is handed another shape, passes the block through untouched. A layout that is not positive in every
  part, or that a stage cannot run, answers `false` rather than throwing, so game code reports it.
- `Reset` clears delay lines, envelopes and holds, because voices are pooled.
- Gain and shape move through `AudioMath.OnePoleCoefficient`, so a knob move is a short ramp: the limiter's release, the
  compressor's gain reduction, and the distortion's drive and tone. A compressor `AttackSeconds` or `ReleaseSeconds`
  change is not itself ramped: it recomputes the coefficient for the very next sample, which is why those knobs are set
  before audio starts.

The limiter holds its reduction for the whole lookahead after a sample crosses the ceiling and only then releases. A
release that started at once would let the delayed peak through above the ceiling; measured with a full-scale 1 kHz sine
and a 0.5 ceiling, nothing leaves above 0.4999. Its release and the compressor's attack and release are time constants
in the value they smooth, so a compressor `AttackSeconds` of 5 ms reaches 63% of its reduction in 5 ms.

Makeup is a gain stage: it can take a full-scale input above full scale, which is why a chain that needs a ceiling puts
the limiter last. The bit crusher keeps exactly `2^bits` levels with both ends of the range included, so one bit keeps
only `-1` and `1`.

The filter is one topology-preserving state-variable core with five outputs: low pass, high pass, band pass, notch and
peak. Two integrators per channel feed it, and its coefficients follow the corner each frame, so a moving corner never
swaps one set of poles for another; corner, quality and peak gain are smoothed with a ten-millisecond time constant.
Measured at 48 kHz with a 1 kHz corner and 0.71 quality, a low pass leaves 1 kHz at -3.00 dB and 8 kHz at -37.80 dB and
a high pass leaves 125 Hz at -36.11 dB; a band pass at quality 2 is unity at its centre and -10.02 dB an octave below; a
notch leaves its centre below -120 dB; a +6 dB peak reaches +5.99 dB. A corner is capped at 45% of the rate, so a tangent
never runs away. The peak's poles do not move with the gain, so a deep cut is narrower than a large boost.

The pitch shifter resamples one delay line with two read heads half a window apart, each at a triangular gain, so the
head that jumps back to the start of its sweep is always silent. It is a granular shift, not a phase vocoder: it needs
no transform and does not smear a transient across the window, though a sample that falls between two read positions is
skipped, and a pure tone can warble. A sweep wraps inside one window, and each tap
stays at least one frame behind the write, so linear interpolation never reads the future. At unison the two heads would
comb the sound at their different delays, so the sweep parks at half a window, where one head is silent and the other
reads at exactly `LatencySamples`; the signal then passes through unchanged, only delayed. That keeps one latency figure
for both settings and means a chain that runs the shifter at zero is not silent, merely late. Returning to unison
crossfades the mix into that settled read over ten milliseconds, so a jump to zero never splices the waveform; measured
on a 997 Hz tone the largest sample-to-sample step across every switch direction is the tone's own slope, 0.13. The
crossfade can null a sustained high tone for about two milliseconds, because the two reads are at different delays:
5 kHz at the longest window measured -36 dB, and white noise lost about 5 dB of RMS. A shorter window and the same fade
narrow the case, and the alternative, an instant switch, was a full-scale splice. Its line
is allocated in `Prepare`: a stereo line is about 19 KiB at 50 ms and 48 kHz, a mono line about 10 KiB, and a host
prepares every block shape up to the output layout, so a warmed stereo voice holds about 28 KiB per chain asset. A pool
that warms many chained cues pays that for each voice separately; a window change is read at the next `Prepare`, never
by the setter, because the audio thread may be inside a block. Measured at 48 kHz with a 50 ms window: a 440 Hz tone
leaves at 880 Hz for +12 semitones, 659 Hz for +7, and 220 Hz for -12, at its original amplitude and within 1 Hz of the
tempered target; the delay is 1,201 samples, half the window plus one.

| Probe                                                                    | Result                                                    |
| ------------------------------------------------------------------------ | --------------------------------------------------------- |
| Limiter, compressor, distortion, bit crusher chain, 10,000 stereo blocks | 0 bytes allocated                                         |
| Pitch shifter, 10,000 stereo 256-frame blocks                            | 0 bytes allocated, 27 ms, about 2,100x realtime           |
| Same input in 64-frame and 1024-frame blocks                             | bit-identical output                                      |
| Full-scale sine, limiter ceiling 0.5                                     | peak 0.4998 to 0.4999                                     |
| Compressor, 0 dBFS, -20 dB threshold, 4:1                                | 15 dB reduction, matches the static curve within 5%       |
| Full-scale sine, pitch shifter at +12                                    | peak at or under the input peak                           |
| Filter, 20,000 stereo 256-frame blocks                                   | 0 bytes allocated, 5.87 us per block, about 900x realtime |
| The same four-stage chain, 256 s of stereo audio                         | 631 ms, about 400x realtime, 13 us per 256-frame block    |

The time figures are from .NET 9 on the dev container, not from an editor or a player. It is why the limiter, the
compressor, the distortion, the bit crusher, the filter and the delay-line pitch shifter need no FFT, no SIMD intrinsic
and no third-party library: each is per-sample work, and an FFT would add a window of latency and its own artefacts for
nothing. The pitch shifter is the one effect that needs a delay line and resampling, and it still needed no library:
the two-head triangular crossfade is a dozen multiplies per frame. Unity exposes no portable SIMD for a Mono/IL2CPP
build without a dependency, and the package keeps none.

## DSP node graph

`DspGraph` runs chains side by side and aligns their branches, and `DspGraphAsset` authors one as data and attaches it
to a cue or a track. The authoring rules, the alignment arithmetic, the memory budget and the measured cost are in
[dsp-graph](./dsp-graph.md).

## DSP attachment

`AudioFilterHost` is the only Unity-coupled audio path. It runs one `DspChain` on `OnAudioFilterRead`, and
`ProcessBuffer` is that same path, so an EditMode test drives a synthetic block with no audio device. `SfxPlayer` puts
one host on each pooled voice; `MusicPlayer` does the same for its three voices.

- A cue (`SfxCue.Chain`) and a track (`MusicTrack.Chain`) name a `DspChainAsset`; each player (`SfxPlayer.BusChain`,
  `MusicPlayer.BusChain`) names one bus chain. Every voice builds its own effect instances from the asset, because a
  delay line or an envelope belongs to the voice that is playing.
- A cue (`SfxCue.Graph`) and a track (`MusicTrack.Graph`) may also name a `DspGraphAsset`, which runs after the voice's
  chain and before the bus: a branch sees the shaped signal, and the bus still acts on the sum. A voice builds its own
  graph for each block shape, so two voices never share a branch buffer; the measured per-voice cost is in
  [dsp-graph](./dsp-graph.md).
- A host prepares every asset it meets for each block shape from one channel up to the output layout. Do not prepare
  only for the output layout: a filter block can arrive narrower than the device, and a chain of the wrong shape passes
  the block through, which is a bypass nothing else reports. A shape wider than the layout passes through and counts in
  `SkippedBlocks`.
- `Chain`'s setter clears both the voice's chain and the bus. `SfxPlayer` and `MusicPlayer` set it on every play, so a
  reused voice never inherits the last play's gain, delay line or envelope. The bus needs the same clear for the same
  reason.
- A chain asset is a recipe: a voice reads it the first time it meets it and keeps those settings, so an edit reaches
  the voices that have not met the asset yet. This is why a live knob belongs on an effect instance, not on an asset.
- A device change re-prepares the selected chains from `AudioSettings.OnAudioConfigurationChanged`, on the main thread.
  A chain's delay line is sized from the sample rate, so a prepared chain is wrong the moment the device moves.
- `SfxPlayer.BuildVoices` warms the bus and every listed cue's chain and graph on every voice, and
  `MusicPlayer.EnsureVoices` does the same for its tracks. A cue played by reference that no library lists is built the
  first time a voice meets it, which is the one allocation a chained play can make; `SfxPlayer.Warm(cue)` moves that
  first build off the play.
- The bus runs inside each voice after the voice's own chain. Unity gives a script no way to sum one source into
  another, so every bus effect acts on one voice at a time: two voices that each fit under a ceiling can still sum
  above it, which makes a bus a shared shaping stage rather than an output limiter. The sum is the mix bus's job.
- `Chain`'s setter asks for a clear, and `ProcessBuffer` applies it at the next block boundary before the stages run.
  The clear covers every block shape the asset prepared, because a play can begin at one width and continue at another.
  Resetting the effects on the main thread would race the block the audio thread is inside. The request is a generation
  counter per asset: a block records the generation it cleared for, so a request written while that block ran is still
  owed to the next one instead of being spent by it.
- `DspChain.GainReductionDecibels` sums its stages, because stages in series add their decibels; makeup gain is not
  counted. The chain, cue and track inspectors show one row per stage in signal order with the live reading any running
  host reports. That reading is diagnostics, so it may allocate.

| Probe                                              | Result                                         |
| -------------------------------------------------- | ---------------------------------------------- |
| `AudioFilterHost` driven with a synthetic block    | ceiling holds; mono and stereo blocks both run |
| Cue with a chain, 100,000 plays                    | 0 bytes of heap growth                         |
| A block wider than the layout                      | passes through, `SkippedBlocks` counts it      |
| Reused voice after a limited block                 | chain and bus read 0 dB, so both were cleared  |
| Pool build, 16 voices, 1 chained four-effect cue   | 2.5 ms, under the memory gauge's resolution    |
| Pool build, 16 voices, 16 chained four-effect cues | 4.5 ms and 652 KiB                             |
| Pool build, 16 voices, 64 chained four-effect cues | 9.3 ms and 3.2 MiB                             |

The warm cost is why a project should share a handful of chain assets rather than author one per cue: the pool builds
one prepared chain per voice per asset per shape. Building on first play instead would put an allocation on the play
path, which is the invariant this package keeps.

## Open questions

Mixer-group routing was measured against per-source writes for ducking and does not replace composing in package code;
see [product-decisions](./product-decisions.md#ducks-compose-in-package-code). A sum over several sources is the mix bus
below; separate group buses are still open.

## DSP mix bus

`AudioMixBus` runs a chain on the object that carries the `AudioListener`, which Unity hands the sum of every source.
It is the one stage that holds a ceiling over several voices, it shapes the whole scene rather than one player, and it
costs one pass per block instead of one per voice. Group buses stay with `AudioMixer` groups. Authoring rules, the
measured ceiling, the delay and the allocation probe are in [dsp-mix-bus](./dsp-mix-bus.md).
