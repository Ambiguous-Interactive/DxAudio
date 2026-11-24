# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Add a Play Mode Mix Monitor to inspect SFX voice history and clip details, and filter drop markers ([#133](https://github.com/Ambiguous-Interactive/DxAudio/issues/133)).
- Show music deck fades and applied duck gain in the Mix Monitor timeline ([#133](https://github.com/Ambiguous-Interactive/DxAudio/issues/133)).
- Show overlapping music duck holds and their releases in the Mix Monitor timeline ([#133](https://github.com/Ambiguous-Interactive/DxAudio/issues/133)).
- Show SFX waveforms at their recorded playback positions in the Mix Monitor ([#133](https://github.com/Ambiguous-Interactive/DxAudio/issues/133)).
- Mark SFX clips cut short by voice stealing with a red edge in the Mix Monitor ([#133](https://github.com/Ambiguous-Interactive/DxAudio/issues/133)).
- Show live stereo sample peaks with peak hold for the listener bus in the Mix Monitor ([#133](https://github.com/Ambiguous-Interactive/DxAudio/issues/133)).
- Show estimated master true peaks in dBTP in the Mix Monitor, including peaks between samples ([#133](https://github.com/Ambiguous-Interactive/DxAudio/issues/133)).
- Show short-term and integrated master loudness in LUFS on the Mix Monitor listener bus ([#133](https://github.com/Ambiguous-Interactive/DxAudio/issues/133)).
- Show the listener inserts and add a Master audition fader and mute in Mix Monitor ([#133](https://github.com/Ambiguous-Interactive/DxAudio/issues/133)).
- Add DxAudio Problems with named validation rules, project severities, and shared or personal silences ([#135](https://github.com/Ambiguous-Interactive/DxAudio/issues/135)).
- Add a Stereo Width effect for DSP chains and graphs to narrow or widen stereo signals ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add a room Reverb effect for DSP chains and graphs with adjustable decay and wet level ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add a Phaser effect for DSP chains and graphs with adjustable sweep rate and depth ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add a Tremolo effect for DSP chains and graphs with adjustable rate and depth ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add a Chorus effect for DSP chains and graphs to widen sounds with adjustable rate, depth, and mix ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add a Noise Gate for DSP chains and graphs to fade quiet tails while keeping stereo channels linked ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add a Delay effect for DSP chains and graphs, with adjustable repeat spacing, feedback, and echo level ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add independent sound and music volume settings that scale voices during playback ([#116](https://github.com/Ambiguous-Interactive/DxAudio/issues/116)).

### Changed

- Apply a shared dark and light theme to the authoring selection, DSP graph, and drawn EQ ([#130](https://github.com/Ambiguous-Interactive/DxAudio/issues/130)).
- Reduce CPU time for Phaser when several voices play at once ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Reduce CPU time for Chorus when several voices play at once ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Reduce CPU time for stereo Distortion when several voices play at once ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Reduce CPU time for Reverb when several voices play at once ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).

### Fixed

- Reject DSP graph layouts that would bypass the buffer limit through integer overflow ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).

## [0.4.0] - 2026-11-20

### Added

- Create balanced cues from selected clips, with shuffle, per-take level, low-pass variation, and seeded A/B audition ([#81](https://github.com/Ambiguous-Interactive/DxAudio/issues/81)).
- Add cue pitch and level variation, seeded series audition, and a visual path to the cue's DSP assets ([#81](https://github.com/Ambiguous-Interactive/DxAudio/issues/81)).
- Add a DxAudio authoring window for audio assets, with a DSP node canvas, ordered effects, repair tools, and live dry/wet audition ([#86](https://github.com/Ambiguous-Interactive/DxAudio/issues/86)).
- Reopen saved drawn EQ targets and apply new fits with Undo while keeping generated Bell references ([#89](https://github.com/Ambiguous-Interactive/DxAudio/issues/89)).
- Draw an EQ target in the DSP Chain inspector, compare the fitted response, and save a new chain of Bell filters ([#85](https://github.com/Ambiguous-Interactive/DxAudio/issues/85)).
- Add a `DspFilter` effect and its asset: one state-variable core gives low pass, high pass, band pass, notch, peak and symmetric bell modes, a knob move is ramped rather than stepped, and it allocates nothing per block ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add an `AudioMixBus` for the object that carries the `AudioListener`: Unity hands it the sum of every source, so a chain there holds a master ceiling that no per-voice bus can see ([#74](https://github.com/Ambiguous-Interactive/DxAudio/issues/74)).
- Add a `DspGraphAsset` and attach it to a cue or a music track: named nodes and edges author a parallel signal path, each voice builds its own graph, and the graph inspector shows each branch's delay ([#72](https://github.com/Ambiguous-Interactive/DxAudio/issues/72)).
- Add an engine-free `DspGraph`: each node holds a `DspChain`, an edge fans a node out and sums it back in, and the graph delays the shorter branch so parallel paths stay aligned. It refuses a cycle or an oversized graph before audio runs ([#69](https://github.com/Ambiguous-Interactive/DxAudio/issues/69)).
- Add a delay-line `DspPitchShifter` effect and its asset: it moves a sound from -24 to 24 semitones with two crossfaded read heads, reports its delay in samples, and allocates nothing per block ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add the DSP host: a cue or music track names a `DspChainAsset`, each pooled voice runs its own prepared copy, a player-wide bus runs after it, and a chain or cue inspector shows the live gain reduction of every stage ([#59](https://github.com/Ambiguous-Interactive/DxAudio/issues/59)).
- Add the engine-free DSP chain: an ordered `DspChain` of limiter, compressor, distortion and bit crusher effects that shape an interleaved block in place without allocating ([#27](https://github.com/Ambiguous-Interactive/DxAudio/issues/27)).
- Add a **Clip Load Type** to `MusicTrack`. **Auto** streams a clip longer than 30 seconds and decompresses the rest, the track inspector applies the choice to the clip's import settings, and the library check reports a clip that does not follow it ([#44](https://github.com/Ambiguous-Interactive/DxAudio/issues/44)).
- Add the user manual in `Documentation~/` (install, quickstart, cues, music, ducks and troubleshooting) and a `BasicUsage` sample scene that plays a cue, holds a loop, starts music and ducks it ([#16](https://github.com/Ambiguous-Interactive/DxAudio/issues/16)).
- `MusicPlayer` raises `TrackChanged` and `Stopped`, and both players take `Paused`, which holds every voice at its playhead so resuming continues rather than restarts. The DxMessaging integration adds music requests, duck ids and `AudioPauseRequest` ([#36](https://github.com/Ambiguous-Interactive/DxAudio/issues/36)).
- An **Audio Diagnostics** overlay component lists busy voices with priorities and loop leases, active ducks and their holders, and dropped-cue counts by reason. `SfxPlayer.DropCounts` and `SfxPlayer.TryGetVoiceState` expose the same data. Sampling allocates only while the overlay is enabled ([#14](https://github.com/Ambiguous-Interactive/DxAudio/issues/14)).
- An optional **DxMessaging integration** assembly drives `SfxPlayer` from `SfxPlayRequest`, `SfxLoopRequest`, `SfxLoopStopRequest`, `SfxStopAllRequest` and `SfxMuteRequest`, and broadcasts `SfxLoopStartedMessage` and `SfxCueDroppedMessage`. It compiles only with DxMessaging installed, so the package keeps no dependency ([#26](https://github.com/Ambiguous-Interactive/DxAudio/issues/26)).
- Cue inspector with take start offsets and audition, duck inspector with a composed preview, and a library validation window (**Tools > Wallstop Studios > DxAudio > Validate Libraries**) that reports missing clips, duplicate keys or contexts, and unreachable tracks ([#12](https://github.com/Ambiguous-Interactive/DxAudio/issues/12)).

### Changed

- Group DxAudio editor commands, asset creation, and components under **Wallstop Studios > DxAudio** ([#87](https://github.com/Ambiguous-Interactive/DxAudio/issues/87)).
- Make `DspEffect.Prepare` and `DspChain.Prepare` into `TryPrepare`, which answers a layout the stage cannot run with `false` instead of an exception, so game code reports a bad layout rather than catching one ([#70](https://github.com/Ambiguous-Interactive/DxAudio/issues/70)).
- Require `mode` `editor` on every bridged `test_run`, refuse play mode there, fail a PlayMode source that imports an asset, and name the crash record when the bridge outlives its editor ([#53](https://github.com/Ambiguous-Interactive/DxAudio/issues/53)).
- Compile every assembly with warnings as errors. `scripts/sync_compiler_policy.py` writes the response file and the ruleset beside each assembly definition before a local, gate, or CI build, and both files stay out of the repository and the npm payload ([#40](https://github.com/Ambiguous-Interactive/DxAudio/issues/40)).
- Move the cue, duck and music track inspectors, and the library window, to UI Toolkit with one shared style; audition, the composed duck preview and the clip import write work from the new tree ([#48](https://github.com/Ambiguous-Interactive/DxAudio/issues/48)).
- Require every enum to start with an obsolete `Unknown = 0`, so an unset serialized value is distinguishable from a chosen one; `MusicEase` values shift by one and `MusicClipLoadType` gains the sentinel and a stored ease migrates to its original curve ([#47](https://github.com/Ambiguous-Interactive/DxAudio/pull/47)).
- Format every source type at the 180-character line length the `.editorconfig` already declares, and keep each changelog entry on one line ([#47](https://github.com/Ambiguous-Interactive/DxAudio/pull/47)).
- Local development harness, gates, and MCP tooling for this repository. No package behavior changes.
- Unity CI runs 6000.5 on pull requests and main. The 2021.3 lane moved to a nightly matrix, which runs every supported editor in both states of the optional DxMessaging integration ([#25](https://github.com/Ambiguous-Interactive/DxAudio/issues/25)).

### Fixed

- Include the Basic Usage sample in packages installed from npm.
- Speed up drawn EQ fits with many Bells in the DSP Chain inspector while preserving the fitted response ([#100](https://github.com/Ambiguous-Interactive/DxAudio/issues/100)).
- Fit broad drawn EQ ramps closer to the preview edge at low sample rates with a small Bell budget ([#96](https://github.com/Ambiguous-Interactive/DxAudio/issues/96)).
- Improve drawn EQ cuts and boosts near the preview edge when the sample rate limits Bell centers ([#91](https://github.com/Ambiguous-Interactive/DxAudio/issues/91)).
- Fit the response between draw points to reduce overshoot on narrow EQ targets ([#88](https://github.com/Ambiguous-Interactive/DxAudio/issues/88)).
- Make `npm run check:fast` pass again, and run the assembly warning policy in the fast check, the gate, and CI. The policy fails a hidden warning: a `#pragma warning disable` in shipped code, an assembly response file or ruleset, or a missing warnings-as-errors setting in the engine-free build ([#37](https://github.com/Ambiguous-Interactive/DxAudio/issues/37)).
- Free a voice whose clip audio data was unloaded or failed to load. `AudioClip.UnloadAudioData` and `Resources.UnloadAsset` leave the clip assigned, so a held loop kept its voice and its looping slot, and music reported playback it did not make ([#32](https://github.com/Ambiguous-Interactive/DxAudio/issues/32)).
- Free a voice whose clip was destroyed. Unity keeps the source reporting playback with no clip, so a released loop held its voice and its looping slot for the session. A stranded voice hid idle ones, and music reported playback it did not make ([#15](https://github.com/Ambiguous-Interactive/DxAudio/issues/15)).
- Start music again when a request names the track that a released clip stopped. The selection had not changed, so the request started nothing ([#15](https://github.com/Ambiguous-Interactive/DxAudio/issues/15)).

## [0.3.0] - 2026-11-15

### Added

- `MusicDuckReasonTable<TKey>` hands one `IMusicDuckReason` per key, the same object on every call, for reasons that
  only code raises. `MusicDuckStack` keys a hold by reference, so a reason made anew per call could not be released
  ([#18](https://github.com/Ambiguous-Interactive/DxAudio/issues/18)).
- `MusicOverride<TTrack>` takes an optional `isPlayable` check, and `Take` refuses a track that the check rejects.
  `MusicPlayer` passes `MusicTrack.IsPlayable` ([#19](https://github.com/Ambiguous-Interactive/DxAudio/issues/19)).

## [0.2.1] - 2026-11-15

### Fixed

- `MusicOverride<TTrack>` sees a destroyed Unity track. `Current` gives the context's music while the override track is
  destroyed, `Take` refuses a destroyed track, and releasing the override announces the context's music again. Generic
  code compares by reference, so `??` and `== null` treated a destroyed track as music to play.

### Changed

- `package.json` lists its dependencies, which are none, and the runtime assembly takes no precompiled DLL that it does
  not name. CI fails when an assembly references a package that `package.json` does not list, or when a listed version
  is not exact ([#7](https://github.com/Ambiguous-Interactive/DxAudio/issues/7)).
- The README's scoped registry covers `com.wallstop-studios`, so other Wallstop Studios packages on npm resolve from the
  same entry. The package is on OpenUPM too, from 0.1.2.
- `SfxSelection<TKey>.TryFollow` documents that it compares keys for equality only.

## [0.2.0] - 2026-11-14

### Changed

- **Breaking:** `SfxThrottle` and `SfxSelection` are `SfxThrottle<TKey>` and `SfxSelection<TKey>`, with an optional
  `IEqualityComparer<TKey>`. A game that keys cues by a struct no longer boxes the key on every call: 1,000 calls on
  warmed struct keys allocated 120,000 bytes before and allocate none now. `SfxPlayer` keys both by `SfxCue`, as before.
  Replace `new SfxThrottle()` with `new SfxThrottle<YourKey>()`, and the same for `SfxSelection`.

## [0.1.2] - 2026-11-14

### Added

- Releases publish to npm as `com.wallstop-studios.dxaudio`, so Unity can install the package from a scoped registry.
  Each GitHub Release carries the same tarball and its SHA-256.

### Changed

- The package holds only `Runtime`, `Tests` and the docs. CI, the .NET test project and editor settings stay in the
  repository.

## [0.1.1] - 2026-11-14

### Fixed

- Voices are `DontSave` only in edit mode, so `FindObjectsOfType<AudioSource>` still finds them in Play mode and a
  game's own pause-all reaches them.
- `MusicPlayer` destroys its voices with a deferred `Destroy` only.
- The `SfxCue` interval tooltip says loops are never rate-limited.

## [0.1.0] - 2026-11-14

### Added

- `SfxCue`, `SfxVariant` and `SfxLibrary` assets: takes with start offsets, volume, pitch jitter, rate limits, priority,
  looping, pause behaviour, mixer routing and 3D falloff.
- `SfxPlayer`: a pooled voice player with priority stealing, per-cue rate limits, no-repeat take selection, matched
  takes (`FollowsTakeOf`), loop handles, a looping voice limit, mute and drop reports.
- `MusicTrack`, `MusicLibrary` and `MusicDuck` assets.
- `MusicPlayer`: music per context with crossfades, reclaiming music that is fading out, overrides by lease, composing
  ducks with low- and high-pass filters, abandoned-holder sweeps and mute.
- An engine-free core (`SfxVoiceArbiter`, `SfxThrottle`, `SfxSelection`, `MusicDuckStack`, `MusicAttenuation`,
  `MusicRamp`, `MusicEasing`, `MusicHandOff`, `MusicOverride`, `AudioLoudness`, `AudioMath`) with tests that run on .NET
  and in Unity.
