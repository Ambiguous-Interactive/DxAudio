# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[Semantic Versioning](https://semver.org/).

## [0.1.1] - 2026-11-14

### Fixed

- Voices are `DontSave` only in edit mode, so `FindObjectsOfType<AudioSource>` still finds them in
  Play mode and a game's own pause-all reaches them.
- `MusicPlayer` destroys its voices with a deferred `Destroy` only.
- The `SfxCue` interval tooltip says loops are never rate-limited.

## [0.1.0] - 2026-11-14

### Added

- `SfxCue`, `SfxVariant` and `SfxLibrary` assets: takes with start offsets, volume, pitch jitter, rate
  limits, priority, looping, pause behaviour, mixer routing and 3D falloff.
- `SfxPlayer`: a pooled voice player with priority stealing, per-cue rate limits, no-repeat take
  selection, matched takes (`FollowsTakeOf`), loop handles, a looping voice limit, mute and drop reports.
- `MusicTrack`, `MusicLibrary` and `MusicDuck` assets.
- `MusicPlayer`: music per context with crossfades, reclaiming music that is fading out, overrides by
  lease, composing ducks with low- and high-pass filters, abandoned-holder sweeps and mute.
- An engine-free core (`SfxVoiceArbiter`, `SfxThrottle`, `SfxSelection`, `MusicDuckStack`,
  `MusicAttenuation`, `MusicRamp`, `MusicEasing`, `MusicHandOff`, `MusicOverride`, `AudioLoudness`,
  `AudioMath`) with tests that run on .NET and in Unity.
