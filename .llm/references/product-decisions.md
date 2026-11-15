# Product Decisions

Decisions that constrain future work. Each entry names the decision, the reason, and what would reopen it.

## Dependency-free by design

**Decision:** the package depends on no other UPM package.

**Reason:** npm, OpenUPM, git, and tarball installs all produce the same files. CI fails when an assembly references a
package the manifest does not list.

**Reopen if:** a measured integration (for example an `AudioMixer` binding) is worth the install and versioning cost.
That integration must be optional.

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
gap above, 926 ns against 265 ns, is about 0.005% of a 16.7 ms frame at 60 Hz, so cost does not decide this.

Three differences do:

- **Composition, from the stack design.** A mixer parameter holds one number per group. Composed ducking takes the
  lowest gain and the lowest low-pass of every live duck, and the highest high-pass. That arithmetic still belongs in
  package code before any mixer write, so a mixer replaces the per-voice write and nothing else.
- **Read-back, measured.** Every `AudioMixer.GetFloat` during a 0.4 s transition into the -6 dB snapshot, and 1 s after
  it, returned the value last passed to `SetFloat` (0 dB) rather than the snapshot's value. A snapshot-driven duck would
  leave `MusicPlayer.Attenuation` holding whatever was last written, not the applied level, and the diagnostics overlay
  (issue #14) without a number.
- **Granularity, computed from the shipped ramp.** The per-voice ramp steps once per frame: at most 0.36 dB per step at
  60 fps and 1.08 dB at 20 fps for this duck, because the end of an `OutQuad` ramp is steepest. Unity runs a snapshot
  transition on its own timeline rather than the frame loop. This session did not measure the applied curve, and the
  group-level probe that would have shown it returned no usable level.

**Reopen if:** a game that renders below 30 fps hears those steps, and a step near 0.5 dB is audible in that game, or a
project wants snapshot authoring strongly enough to accept an editor-side asset dependency. Any adopted design must keep
the composed level readable for diagnostics.

## Mute preserves state

**Decision:** muting changes output, not timers or loops.

**Reason:** unmuting must resume in time rather than restart. A settings toggle is not a stop.

## Unity 2021.3 floor

**Decision:** support 2021.3 and newer. Use no API newer than that without a version guard.

**Reason:** the package installs in existing projects on older editors. The development host is 6000.6.7f1.

**Reopen if:** maintaining the floor blocks a measured, necessary API.

## MIT No Attribution

**Decision:** MIT-0.

**Reason:** maximum adoption with no attribution burden.
