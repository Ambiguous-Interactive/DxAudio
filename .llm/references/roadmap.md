# Roadmap

Candidate milestones, ordered by user impact. Each needs an issue before work starts. This file is a scope reference,
not a fixed queue.

## 1. Run the Unity suites in CI (delivered)

One `Unity Tests` workflow runs the EditMode suite on 6000.5.2f1 for every pull request and push to `main`, and adds
2021.3.45f1 on the nightly schedule and a manual dispatch, behind the organization build lock. A planted test failure
fails its leg and the single `Unity CI Success` aggregate. See issues #1 and #62.

## 2. Editor authoring UX

**Issue:** [#12](https://github.com/Ambiguous-Interactive/DxAudio/issues/12)

**Problem:** cues, tracks, and libraries are authored through Unity's default inspector. Nothing previews a cue,
validates a library, or shows a falloff.

**Scope:**

- Cue inspector with an audition button, variant list, and start-offset preview.
- Library validation: duplicate keys, missing clips, unreachable tracks.
- Duck inspector showing composed attenuation at a chosen set of holders.

**Acceptance:** a new `Editor/` assembly with EditMode tests; no runtime behavior change; package payload whitelist
updated.

## 3. Mixer routing, measured

**Issue:** [#13](https://github.com/Ambiguous-Interactive/DxAudio/issues/13)

**Problem:** users expect volume groups (Master, Music, SFX) and snapshot-style ducking. DxAudio composes attenuation
itself.

**Scope:** benchmark per-source volume fading versus an `AudioMixer` group and snapshots for the same duck scenario.
Decide with numbers, not preference.

**Acceptance:** a decision in `.llm/references/product-decisions.md`, a measurement table, and (if adopted) an optional
integration that does not break the zero-dependency install.

## 4. Runtime diagnostics

**Issue:** [#14](https://github.com/Ambiguous-Interactive/DxAudio/issues/14)

**Problem:** when a cue is dropped or a duck sticks, the only signal is `CueDropped` and `DescribeDucks()`.

**Scope:** a diagnostics overlay showing live voices, priorities, loop handles, active ducks and holders, and
dropped-cue counters.

**Acceptance:** sampling allocates only when the overlay is enabled; tests cover the disabled path.

## 5. Addressables and streaming

**Issue:** [#15](https://github.com/Ambiguous-Interactive/DxAudio/issues/15)

**Problem:** long tracks and large cue sets need async loading and unloading.

**Scope:** clip lifetime is delivered (PR #31, unreleased): a destroyed clip frees its voice within a frame while the
player is enabled, a released cue is refused as `NotPlayable`, and music whose clip is gone stops and can start again. A
clip object that survives with its audio data unloaded is detected too (issue #32). What remains is streaming versus
decompressed clips for long tracks.

**Acceptance:** a measurement that decides streaming versus decompressed for a long track, recorded in
[audio-design](./audio-design.md).

## 6. Documentation site and sample scene

**Issue:** [#16](https://github.com/Ambiguous-Interactive/DxAudio/issues/16)

**Scope:** `Documentation~/` pages for install, quickstart, cues, music, ducks, and troubleshooting; one sample scene
that demonstrates the README flows.

**Acceptance:** links resolve, the sample compiles, and the npm payload excludes sample and documentation folders.

## 7. Composable synthesis

**Issue:** [#195](https://github.com/Ambiguous-Interactive/DxAudio/issues/195) records the decisions; steps land
one issue at a time.

**Problem:** sounds are authored as imported clips. A graph asset that renders sound at build time or at runtime,
with reusable modules, vibe-driven randomization, and runtime control that reaches Web, needs its foundations
decided and measured first.

**Scope:** engine-free random foundation and acoustic measurement kit, impact and pluck sources, the offline
renderer, clip sinks, inputs as knobs or wires, modules, vibes, clock and loops, bake and JSON interchange, and a
WebGL CI lane with live control. Steps and ordering live in the session plan; decisions in
[procedural synthesis](./procedural-synthesis.md).

**Acceptance:** every step keeps the design contract (bake equals live, same seed same sound, control within two
frames, no allocation on a warmed audio path) with a test; measurements land in `progress/`.

## Current baseline

Measured during bootstrap on Unity 6000.6.7f1:

- Engine-free core: 80 NUnit tests pass on .NET 9.
- Unity EditMode: 94 tests pass in about 0.5 s.
- Allocation probes (`Tests/Editor/Unity/AllocationTests.cs`): `Play` and keyed `Play` each allocated **0 bytes** over
  1,000 warmed calls on Unity's Mono.
- A fresh CI project built by `scripts/ci_prepare_project.py` resolves the package by `file:` reference and runs the
  suite.
- Coverage gaps: no test drives `SfxPlayer` through a real `AudioSource` completion callback; duck composition has no
  per-frame allocation probe yet.
