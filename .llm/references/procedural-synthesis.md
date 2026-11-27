# Procedural Synthesis

The decision record for DxAudio's composable synthesis effort: a graph asset renders sound, modules
are reused inside other graphs, vibes drive constrained randomization, and runtime control works on
every platform, especially Web. The session plan and step list live in `PLAN.md` (local-only); the
spec documents live in `progress/synthesis/` (local-only). This page holds what is decided and why,
so future sessions do not relitigate it.

## Decisions

### Four sinks

**Decision:** sound reaches the player through four sinks: a baked clip asset, a clip rendered at
warm-up, a live native voice, and a live Web stream.

| Sink                     | Platforms | Notes                                                                         |
| ------------------------ | --------- | ----------------------------------------------------------------------------- |
| Baked clip asset         | all       | zero runtime cost; build-time or editor command                               |
| Rendered clip at warm-up | all       | `AudioClip.Create(stream: false)` plus `SetData`; parameters frozen at render |
| Live voice               | native    | render-ahead blocks through `OnAudioFilterRead`                               |
| Live stream              | Web       | `.jslib` Web Audio bridge or `PlayScheduled` chaining                         |

**Reason:** on Web, `OnAudioFilterRead` and the Scriptable Audio Pipeline are unsupported, streaming
clips are unsupported, non-streaming `AudioClip.Create` with `SetData` is partially supported, and
`PlayScheduled` is supported. Mixer support on Web is volume only. Verified against Unity
documentation; the first two sinks are therefore the only paths that work everywhere today. Sources:
[OnAudioFilterRead](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html),
[AudioClip.Create](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AudioClip.Create.html), and
[PlayScheduled](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AudioSource.PlayScheduled.html)
in the 2021.3 scripting reference. The Scriptable Audio Pipeline and Web streaming limits, the volume-only mixer
support, and the complete source list are recorded with citations in the local `progress/synthesis/01-web-platform.md`.

**Reopen if:** Unity ships a managed audio path on Web, or a measured game needs a sink this table
cannot express.

### Clip-first

**Decision:** prefer clips. Bake at build time, or render into a clip at warm-up and play it like any
clip. Fall to live voices only where a parameter must keep moving at runtime, and treat that as a
per-platform capability the package reports rather than assumes.

**Reason:** a clip is data: it plays through the existing pooled voice player, costs nothing per
frame, and behaves identically on every platform. Live synthesis spends main-thread time per frame on
Web, where the render happens on the same thread as game code. The warm cache keeps frequently
retriggered cues at zero render cost after warm-up.

**Reopen if:** a sound needs parameter response that a frozen clip cannot give and its render cost
measures cheap enough to run live on the target platforms.

### Own schema

**Decision:** the graph interchange format is DxAudio's own JSON schema (`format:
"dxaudio-graph"`, `version: 1`) with export and import, not Unity's serialized object form.

**Reason:** one node registry then drives the editor menu, validation, and the generated
specification. The schema is diffable, versionable, and readable outside Unity; a refused file can
name the exact node and reason.

**Reopen if:** Unity ships a serialized-data path that meets those needs without a second format to
keep in sync.

### Modules by reference

**Decision:** a module is a graph asset that other graphs reference as an element and that flattens
at build or render time. Flattening refuses cycles and depth over budget, naming the path. A module
never ships as a pasted copy.

**Reason:** one fix to a module lands in every graph that uses it. Pasted copies drift the moment
the source module changes. Per-instance seed derivation keeps two instances of one module
decorrelated: two rain layers must not sound identical.

**Reopen if:** a measured use needs runtime module structure that flattening cannot express, such as
swapping modules mid-play, and the flatten-then-swap design costs more than a live structure.

### Vibe set

**Decision:** a vibe asset holds ranges and locks over a graph's exposed knobs and offers exactly
three random amounts: mutate (small change), explore (broad search), and roll (fresh candidate set).
Generation renders candidates, measures them with the acoustic kit, rejects invalid ones, and keeps
the best per grid cell.

**Reason:** three amounts separate the three intents a designer has: nudge what exists, search
around it, or start over. Measurement gates keep a vibe from returning a broken sound, and the grid
keeps a batch diverse instead of returning near-duplicates.

**Reopen if:** measured authoring sessions need a fourth intent the three cannot express.

## Invariants every step keeps

Each synthesis step keeps these design contract points, and each has a test:

1. **Bake equals live.** The same graph with the same parameter timeline produces the same audio
   offline and live: bit-identical on one runtime, within 0.1 dB RMS across runtimes.
2. **Same seed, same sound.** Every node derives its own seed from the graph seed, its path and its
   instance index. Editing one module never changes another's output.
3. **Control within two frames.** Parameter targets arrive per frame; the graph smooths per sample.
4. **No allocation, lock or log** on the audio path once warmed.

The random foundation for invariant 2 is [product decisions](./product-decisions.md#composable-synthesis-four-sinks-clip-first)
and the `SfxRandom*` types in `Runtime/Core`. Acoustic measurement lives in the DSP Core kit once
step R2 lands.

## Spec documents

The working specifications live in the local-only `progress/synthesis/` tree and load on demand:

| Document                       | Covers                                                                                |
| ------------------------------ | ------------------------------------------------------------------------------------- |
| `01-web-platform.md`           | Web facts and sources, CI lane design, both live-control designs, measurements, risks |
| `02-randomness-spec.md`        | seed derivation, struct RNG, draw rules, distributions, chance modes, adapter         |
| `03-renderer-and-sinks.md`     | source contract, renderer, clip sink, warm cache, capability report                   |
| `04-sources-impacts-plucks.md` | per-source DSP specs, licences, acoustic tests                                        |
| `05-modules-spec.md`           | module data model, flattening, budgets, editor actions                                |
| `06-vibes-spec.md`             | vibe assets, three random amounts, generate-measure-select                            |
| `07-clock-patterns-loops.md`   | clock, step sequencer, drums, loop render, sync                                       |

## Risks

- Web: browsers require a user gesture before audio; iOS silent mode may mute runtime-created clips (manual
  check); `PlayScheduled` can fire early when a tab suspends; baked clips import as AAC on Web, so parity
  compares before import.
- Web rendering runs on the main thread: budget per frame; long beds bake at build time.
- Flattened modules exceed today's 16-node and 32-edge budgets: raise `DspGraph.MaximumNodes` and
  `MaximumEdges` only after R8 measures memory and CPU.
- The pinned `unity-helpers` commit may differ from `main`: verify the R1b adapter against the pin.

## Relationship to the shipped package

Synthesis extends the existing assets and players without replacing them: a rendered clip plays
through the same pooled `SfxPlayer` path as an imported clip, and `ISfxRandom` keeps its single
method so existing implementers are untouched. Public API stays additive. See
[audio design](./audio-design.md) for the shipped package behavior.
