# Roadmap

Candidate milestones, ordered by user impact. Each needs an issue before work starts. This file is a scope reference,
not a fixed queue.

## 1. Run the Unity suites in CI

**Problem:** CI runs only the engine-free core plus format and `.meta` checks. `Tests/Editor/Unity` has never run on a
pull request.

**Scope:** an EditMode job on the oldest and newest supported editors (2021.3 and 6000.x), behind the organization build
lock.

**Acceptance:** a planted failure in `PlayerTests` fails the job. Workflow guards match the organization audit.

**Reference:** issue #1.

## 2. Editor authoring UX

**Problem:** cues, tracks, and libraries are authored through Unity's default inspector. Nothing previews a cue,
validates a library, or shows a falloff.

**Scope:**

- Cue inspector with an audition button, variant list, and start-offset preview.
- Library validation: duplicate keys, missing clips, unreachable tracks.
- Duck inspector showing composed attenuation at a chosen set of holders.

**Acceptance:** a new `Editor/` assembly with EditMode tests; no runtime behavior change; package payload whitelist
updated.

## 3. Mixer routing, measured

**Problem:** users expect volume groups (Master, Music, SFX) and snapshot-style ducking. DxAudio composes attenuation
itself.

**Scope:** benchmark per-source volume fading versus an `AudioMixer` group and snapshots for the same duck scenario.
Decide with numbers, not preference.

**Acceptance:** a decision in `.llm/references/product-decisions.md`, a measurement table, and (if adopted) an optional
integration that does not break the zero-dependency install.

## 4. Runtime diagnostics

**Problem:** when a cue is dropped or a duck sticks, the only signal is `CueDropped` and `DescribeDucks()`.

**Scope:** a diagnostics overlay showing live voices, priorities, loop handles, active ducks and holders, and
dropped-cue counters.

**Acceptance:** sampling allocates only when the overlay is enabled; tests cover the disabled path.

## 5. Addressables and streaming

**Problem:** long tracks and large cue sets need async loading and unloading.

**Scope:** document clip lifetime rules; add a load handle or a release-safe play path. Define what happens when a clip
is released while a voice holds it.

**Acceptance:** a test that releases a clip during playback and observes a defined outcome.

## 6. Documentation site and sample scene

**Scope:** `Documentation~/` pages for install, quickstart, cues, music, ducks, and troubleshooting; one sample scene
that demonstrates the README flows.

**Acceptance:** links resolve, the sample compiles, and the npm payload excludes sample and documentation folders.

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
