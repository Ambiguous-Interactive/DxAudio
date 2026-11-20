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
- Put it on an object with no `AudioSource`. The engine runs a filter on the listener's object for a source there too,
  so one chain would shape that sound and then the mix. The inspector names that case.
- For category processing, author effects on [category mixer groups](#category-mixer-groups).
- Nothing registers with a mix bus and nothing is buffered. Unity has summed each block before the filter runs, so a
  source that starts or stops mid-block is simply present or absent in the next block; there is no source list to keep,
  no crossfade the bus owes, and no stolen voice to report.
- Ducks are untouched. `MusicDuckStack` composes a gain and a filter pair per music voice, and the mix bus runs after
  that, so a ducked voice arrives ducked. A "duck the whole mix" reason would be a new owner on the bus, not a change to
  how ducks compose today.

## Measured

On the 6000.6.7f1 host editor at 48 kHz stereo, Mono with no optimizations:

| Probe                                                  | Result                                          |
| ------------------------------------------------------ | ----------------------------------------------- |
| Two voices, a -6 dB limiter on each                    | peaks 0.5012, sum 1.0024                        |
| The same sum through a mix bus limiter at -6 dB        | peak 0.5012                                     |
| Mix bus with a 5 ms lookahead limiter                  | 240 samples of delay, no more                   |
| Limiter, compressor, limiter; 1,024-frame stereo block | 778 us per block, 27x realtime                  |
| A mix bus block, 64-frame mono, limiter and compressor | 0 managed allocation events over 100,000 blocks |

Two voices that each leave their own bus under the ceiling still sum 6 dB above it, which is the case no per-voice stage
can fix. The mix bus runs once per block, so its cost does not grow with the pool; the chain above is the same work a
per-voice bus would do for every voice.

The play-mode path, where the engine actually calls `OnAudioFilterRead` on the listener's object, is not measured here:
the bridge refuses play mode and edit mode renders no audio. The callback contract is Unity's documented behaviour for a
script on an `AudioListener`; issue #46 carries the play-mode probe.

## Category mixer groups

1. Create Music and SFX children under Master in one Unity Audio Mixer.
2. Assign each child in the matching library's `MixerGroup` field. Use `SfxCue.MixerGroupOverride` for cue exceptions.
3. Add Unity effects to each group's stack. A Music compressor then sees the music sum, including both crossfade voices.
4. Use the mixer Master stack for that mixer's output. For a scene-wide chain, put `AudioMixBus` on a listener object without an `AudioSource`.

Unity mixes each group's inputs and processes its effect stack before sending the result to its parent.
[Unity Audio Mixer overview](https://docs.unity3d.com/2021.3/Documentation/Manual/AudioMixerOverview.html)

The library fields provide the routing. Unity accepts native DSP plug-ins as custom group effects. Those plug-ins need
platform-specific builds. The managed filter callback has no group target. The recording sink is internal in Unity
2021.3 and 6000.6, and starting `AudioRenderer` capture mutes normal output.
[Native audio plug-ins](https://docs.unity3d.com/2021.3/Documentation/Manual/AudioMixerNativeAudioPlugin.html)
[Unity 6000.6 recording API source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.6/Modules/Audio/Public/ScriptBindings/AudioRenderer.bindings.cs#L30)
[2021.3 recording API source](https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Modules/Audio/Public/ScriptBindings/AudioRenderer.bindings.cs#L29)
[AudioRenderer.Start](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AudioRenderer.Start.html)

`MusicDuckStack` composes gain and low- and high-pass cutoffs on each music voice before the group sum. A group Duck Volume effect, if
authored, applies independently at its position in the mixer stack. Its gain can change a later compressor's input.

### Managed stage cost

Unity 6000.6.7f1 EditMode on the development host. Each input was 1,024 stereo frames at 48 kHz.
The probe used the same -6 dB limiter asset with zero lookahead and checked its output at the input peak.
It warmed each variant for 128 blocks, then collected 120 timings of 16 blocks. It alternated variant order and reports time per block.

| Stage                                          | Median (us) | p95 (us) |
| ---------------------------------------------- | ----------: | -------: |
| One block copy                                 |        0.44 |     0.58 |
| One voice via `AudioFilterHost`                |      243.49 |   270.49 |
| Eight independent voices via `AudioFilterHost` |    1,940.68 | 2,238.40 |
| One pre-summed block via `AudioMixBus`         |      241.87 |   293.84 |

The probe copies a fresh input for every pass and calls `ProcessBuffer` directly. The pass count scales with active groups. Two categories and a master would need three chain passes per block. These timings cover the managed processing only. They exclude summing,
native mixer effects, audio-thread scheduling, and playback latency.
