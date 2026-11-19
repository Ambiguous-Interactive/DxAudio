# DSP mix bus

`AudioMixBus` runs a chain on the object that carries the `AudioListener`. Unity sums every source and calls a filter on
that object with the mix, which is the only place in a scene where a script sees the sum. A per-voice bus cannot hold a
ceiling over several voices, because it only ever holds one.

- It shapes the whole scene, including sources this package does not play, so it is a master stage and not a group bus.
  Group routing stays with `AudioMixer` groups through the libraries' `MixerGroup` fields and the cue override.
- It adds no buffering. `LatencySamples` is its chain's own: a chain with no lookahead and no window reports zero, and a
  limiter reports its lookahead, 240 samples for 5 ms at 48 kHz.
- Its chain is a recipe like any other. Changing `Chain` selects a prepared slot and asks for a clear at the next block;
  setting the asset that already runs does nothing, so a scene that sets the same bus again does not restart a release.
- A device change re-prepares the chain from `AudioSettings.OnAudioConfigurationChanged`, on the main thread.
- Attached to any object but the listener's, the component receives no block. `IsOnListener` and the inspector report
  that, and `SkippedBlocks` counts a block no prepared chain could run. `ProcessBuffer` is the path a test drives.
- A mix bus is not a routing tree. One bus shapes one sum, and a game that needs Master, Music and SFX group buses with
  their own effect chains is a separate design.

## Measured

On the 6000.6.7f1 host editor at 48 kHz stereo, Mono with no optimizations:

| Probe                                                  | Result                         |
| ------------------------------------------------------ | ------------------------------ |
| Two voices, a -6 dB limiter on each                    | peaks 0.5012, sum 1.0024       |
| The same sum through a mix bus limiter at -6 dB        | peak 0.5012                    |
| Mix bus with a 5 ms lookahead limiter                  | 240 samples of delay, no more  |
| Limiter, compressor, limiter; 1,024-frame stereo block | 778 us per block, 27x realtime |
| A mix bus block, 100,000 blocks                        | 0 bytes of heap growth         |

Two voices that each leave their own bus under the ceiling still sum 6 dB above it, which is the case no per-voice stage
can fix. The mix bus runs once per block, so its cost does not grow with the pool; the chain above is the same work a
per-voice bus would do for every voice.

The play-mode path, where the engine actually calls `OnAudioFilterRead` on the listener's object, is not measured here:
the bridge refuses play mode and edit mode renders no audio. The callback contract is Unity's documented behaviour for a
script on an `AudioListener`; issue #46 carries the play-mode probe.
