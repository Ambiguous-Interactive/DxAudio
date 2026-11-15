---
name: duck-composition
description: How DxAudio ducks compose - One versus Many holders, most-restrictive volume and filter wins, copy-safe leases, destroyed holders, and override interaction. Use when changing MusicDuck, MusicDuckStack, MusicDuckLease, MusicDuckHolders, or MusicPlayer duck behavior.
metadata:
  category: Feature
---

# Duck Composition

## Model

- A `MusicDuck` asset holds volume, low-pass, high-pass, apply time, release time, and a holder policy.
- **One** means a state that is on or off. Asserting it again changes nothing; one release ends it.
- **Many** means a claim several things make at once. The music stays down until the last holder releases.
- Every live duck contributes. The result is the most restrictive volume (lowest) and filter set (lowest low-pass, highest high-pass).
- Ducks never know about each other. A system releases only its own lease.

## Invariants

1. Composition is order-independent. Two releases in either order produce the same attenuation.
2. A lease is a value struct with a generation. A stale, foreign, or default lease releases nothing.
3. Copying a lease must not double-release. `MusicDuckLease` is copy-safe by generation check.
4. A `MusicDuckHolders` entry whose Unity holder was destroyed is released on the next frame. Do not leave a contribution pinned by a dead object.
5. `MusicPlayer.DescribeDucks()` reports every live duck and holder; diagnostics must not allocate per frame unless gated.

## Tests

`Tests/Editor/Core/MusicDuckStackTests.cs` pins the composition algebra:
`HoldsComposeToTheMostRestrictiveAndReleaseIndependently`, `AOneReasonIsOneHoldHoweverOftenItIsAsserted`, `AManyReasonHoldsUntilTheLastHolderReleases`,
`StaleForeignAndDefaultLeasesReleaseNothing`. Add a case there before changing the stack.

`Tests/Editor/Unity/PlayerTests.cs` pins the player integration: `DucksComposeRampAndRelease`.

## Design checks

- Compare against a simple baseline: volume in decibels, filters in hertz. Record what "most restrictive" means for each field in the test name.
- A new field on a duck needs a composition rule, a test, and a README update.
- Do not add a Unity `AudioMixer` dependency here. The package is deliberately dependency-free; a mixer integration would be a separate, measured proposal.

## Related

- [voice-arbitration](./voice-arbitration/SKILL.md) - the sound-effects-side policy
- [measure-audio-behaviour](./measure-audio-behaviour/SKILL.md) - prove no per-frame allocation
