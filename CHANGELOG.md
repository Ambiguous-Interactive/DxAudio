# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- An **Audio Diagnostics** overlay component lists busy voices with priorities and loop leases, active ducks and their
  holders, and dropped-cue counts by reason. `SfxPlayer.DropCounts` and `SfxPlayer.TryGetVoiceState` expose the same
  data. Sampling allocates only while the overlay is enabled
  ([#14](https://github.com/Ambiguous-Interactive/DxAudio/issues/14)).
- Cue inspector with take start offsets and audition, duck inspector with a composed preview, and a library validation
  window (**Tools > DxAudio > Validate Libraries**) that reports missing clips, duplicate keys or contexts, and
  unreachable tracks ([#12](https://github.com/Ambiguous-Interactive/DxAudio/issues/12)).

### Changed

- Local development harness, gates, and MCP tooling for this repository. No package behavior changes.

### Fixed

- Free a voice whose clip's audio data was unloaded. `AudioClip.UnloadAudioData` and `Resources.UnloadAsset` leave the
  clip assigned, so a held loop kept its voice and its looping slot, and music reported playback it did not make
  ([#32](https://github.com/Ambiguous-Interactive/DxAudio/issues/32)).
- Free a voice whose clip was destroyed. Unity keeps the source reporting playback with no clip, so a released loop held
  its voice and its looping slot for the session. A stranded voice hid idle ones, and music reported playback it did not
  make ([#15](https://github.com/Ambiguous-Interactive/DxAudio/issues/15)).
- Start music again when a request names the track that a released clip stopped. The selection had not changed, so the
  request started nothing ([#15](https://github.com/Ambiguous-Interactive/DxAudio/issues/15)).

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
