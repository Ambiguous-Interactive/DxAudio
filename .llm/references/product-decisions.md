# Product Decisions

Decisions that constrain future work. Each entry names the decision, the reason, and what would reopen it.

## Dependency-free by design

**Decision:** the package depends on no other UPM package.

**Reason:** npm, OpenUPM, git, and tarball installs all produce the same files. CI fails when an assembly references a
package the manifest does not list.

**Reopen if:** a measured integration (for example an `AudioMixer` binding) is worth the install and versioning cost.
That integration must be optional.

## Optional integrations are guarded assemblies

**Decision:** an integration with another package lives in `Runtime/Integrations/<Package>/` as its own assembly, with a
`versionDefines` entry for that package and a `defineConstraints` entry naming the same symbol. It is not listed in
`package.json` dependencies.

**Reason:** Unity skips an assembly whose define constraints are unmet, so a project without the package never resolves
the reference and never sees an error. `package_check.py` accepts a reference to an unlisted package only when the
assembly is guarded exactly that way, and refuses every other unlisted reference. The package therefore stays
dependency-free while a consumer who installs the package gets the integration with no extra step.

**Evidence:** `Runtime/Integrations/DxMessaging` (issue #26) ships this way. Deleting its `defineConstraints` entry
makes `package_check.py` fail with "package.json does not list com.wallstop-studios.dxmessaging"; the Unity 2021.3 lane
of the nightly matrix installs no DxMessaging and still compiles and passes its suite.

**Reopen if:** Unity changes how guarded assemblies resolve references, or a package's API needs an editor-side asset
that cannot be guarded this way.

## ScriptableObject assets over code constants

**Decision:** cues, tracks, libraries, and ducks are assets.

**Reason:** designers author and diff them; code stays generic. Serialized fields are therefore a public contract.

## Pooled voices with priority stealing

**Decision:** a fixed pool, idle-first, then strictly lower priority, oldest first. Loops are never stolen.

**Reason:** never stealing between equals avoids clipping audible sound. Priority prevents frequent low-value cues from
starving important ones.

**Reopen if:** a measured play pattern needs preemption between equals or a reserved-voice tier.

## Music kept resident, effects pooled

**Decision:** one or two music sources fade and crossfade; effects share a configurable pool.

**Reason:** music is long-lived context, effects are bursts.

## Ducks compose in package code

**Decision:** no `AudioMixer` or snapshot requirement for ducking. Ducks compose in `MusicDuckStack`, and the composed
gain goes to each music voice. Mixer routing stays optional and already works: `MusicLibrary.MixerGroup`,
`SfxLibrary.MixerGroup`, and `SfxCue.MixerGroupOverride` place a voice in a group, so Master, Music, and SFX groups are
available without a snapshot.

**Reason:** zero dependencies, testable on plain .NET, and composition is order-independent. Milestone 3 (issue #13)
measured what a mixer would buy before deciding.

Measured in the Unity Editor 6000.6.7f1 through the MCP bridge, on Windows 11 and a Snapdragon X Elite: three music
voices, each with a low-pass and a high-pass, and one duck from 0 dB to -6 dB (low-pass 1.2 kHz, high-pass 10 Hz)
applied over 0.4 s, held 0.5 s, and released over 0.8 s, which is 102 frames at 60 Hz. Each sample is one duck cycle;
three rounds with the variant order alternated, 30 warm-up cycles before the first round, and `GC.Collect` before every
round; n = 900 cycles (91,800 frames) per row. Times are per frame, median across cycles, and each composed row includes
the ramp math shared by all three voices:

| Per frame, three music voices                          | Median |     p95 | Allocated |
| ------------------------------------------------------ | -----: | ------: | --------: |
| Composed per voice: gain, both cutoffs, both `enabled` | 926 ns | 1186 ns |       0 B |
| Composed per voice: gain only                          | 485 ns |  567 ns |       0 B |
| Composed, duck held steady (the player writes anyway)  | 591 ns |  680 ns |       0 B |
| Mixer exposed parameter write, decibels computed in C# | 265 ns |  273 ns |       0 B |

A snapshot transition is one call per duck change and nothing per frame; the call measured 206 ns once warm. The widest
gap above, 926 ns against 265 ns, is about 0.005% of a 16.7 ms frame at 60 Hz, so cost does not decide this. Reading an
exposed parameter during and after a 0.4 s transition into the -6 dB snapshot returned the value last passed to
`SetFloat` (0 dB), not the snapshot's value; the applied curve itself was not measurable here, so that read is recorded
as API behaviour, not as output evidence.

Three differences do:

- **Composition, from the stack design.** A mixer parameter holds one number per group. Composed ducking takes the
  lowest gain and the lowest low-pass of every live duck, and the highest high-pass. That arithmetic still belongs in
  package code before any mixer write, so a mixer replaces the per-voice write and nothing else.
- **Dependencies, from the design rule.** A mixer needs an editor-authored `.mixer` asset, which pulls ducking out of
  `Runtime/Core` and out of the plain .NET suite where it is tested today. The package promises no dependencies and no
  asset for ducking.
- **Granularity, computed from the shipped ramp.** The per-voice ramp steps once per frame: at most 0.36 dB per step at
  60 fps and 1.08 dB at 20 fps for this duck, because the end of an `OutQuad` ramp is steepest. Unity runs a snapshot
  transition on its own timeline rather than the frame loop, which is the one thing a mixer could buy here. Measure it
  before paying for it.

**Reopen if:** a game that renders below 30 fps hears those steps, and a step near 0.5 dB is audible in that game, or a
project wants snapshot authoring strongly enough to accept an editor-side asset dependency. Any adopted design must keep
the composed level readable for diagnostics.

## A master bus is a listener filter, not a mixer asset

**Decision:** a chain over several sources is an `AudioMixBus` on the object that carries the `AudioListener`. Unity sums
every source and hands that object the mix, so one chain shapes the sum with no routing asset and no cross-voice buffer.
The per-voice bus stays for shaping one player. This answers the mixer question in issue #74 with the smallest design
that holds a ceiling over the sum.

**Reason:** measured on the development host at 48 kHz: two voices each under a -6 dB limiter on their own sum to a
1.0024 peak, so each one passes and the pair does not, while the same sum through a mix bus limiter returns to 0.5012.
A listener filter costs one pass per block instead of one per voice, adds no delay of its own and allocates nothing. Two
alternatives were rejected: a graph with several inputs needs a cross-voice buffer, a master clock and a rule for a
source that stops mid-block, to reach the same result; an `AudioMixer` master group needs an editor-side asset and moves
the behavior out of the engine-free suite.

## Use Unity mixer effects on group sums

**Decision:** author category effects on Unity's Music and SFX mixer groups. Keep a listener chain for the scene-wide
sum. DxAudio does not provide a managed insert on those groups. This resolves
[#76](https://github.com/Ambiguous-Interactive/DxAudio/issues/76).

**Reason:** Unity owns the group sums and exposes a native plug-in interface for custom group DSP. A second
`AudioMixBus` has no group input. A managed routing tree would need to mix and schedule voices itself or buffer
intercepted audio. It would also need to preserve spatial output, streaming, and voice timing.

**Evidence:** in Unity EditMode, one prepared DxAudio limiter took 242 us per pre-summed stereo block. The same
limiter took 1,941 us across eight per-voice blocks. This measures the cost of the managed stages. See the
[method, scope, and authoring steps](./dsp-mix-bus.md#category-mixer-groups).

**Reopen if:** a game needs a specific DxAudio effect on a group sum and can validate an insertion path in a player.
Test group isolation, downstream master processing, latency, and streaming before adopting that path.

## Mute preserves state

**Decision:** muting changes output, not timers or loops.

**Reason:** unmuting must resume in time rather than restart. A settings toggle is not a stop.

## Unity 2021.3 floor

**Decision:** support 2021.3 and newer. Use no API newer than that without a version guard.

**Reason:** the package installs in existing projects on older editors. The development host is 6000.6.7f1.

**Reopen if:** maintaining the floor blocks a measured, necessary API.

## Composable synthesis: four sinks, clip-first

**Decision:** a DxAudio synthesis graph renders as data, through four sinks: a baked clip asset, a clip rendered
at warm-up, a live native voice, and a live Web stream. Clips come first: bake at build time or render at warm-up,
and go live only where a parameter must keep moving, as a reported capability rather than an assumption. The graph
interchange format is DxAudio's own JSON schema (`format: "dxaudio-graph"`), not Unity's serialized form. A module
is a graph asset referenced by other graphs and flattened at build or render time, never a pasted copy. Vibes hold
ranges and locks over exposed knobs with exactly three random amounts: mutate, explore, and roll.

**Reason:** Web is the constraint that sets the shape. `OnAudioFilterRead` and the Scriptable Audio Pipeline are
unsupported there, streaming clips are unsupported, and mixers expose volume only; non-streaming
`AudioClip.Create` with `SetData` and `PlayScheduled` are supported. Clips therefore run everywhere and live
synthesis does not. An own schema makes one node registry drive the editor menu, validation, and the generated
specification, and lets a refused file name the exact node and reason. Modules by reference mean one fix lands in
every graph that uses the module, while per-instance seed derivation keeps two instances decorrelated. The three
vibe amounts separate nudging what exists from searching around it and from starting over. Full record:
[procedural synthesis](./procedural-synthesis.md).

**Reopen if:** Unity ships a managed audio path on Web, a measured game needs live synthesis whose render cost
fits its platforms, or a serialized-data path replaces the schema without a second format to keep in sync.

## MIT No Attribution

**Decision:** MIT-0.

**Reason:** maximum adoption with no attribution burden.
