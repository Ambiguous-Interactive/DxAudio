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
measured the snapshot alternative on the development host.

Measured on Windows 11, Snapdragon X Elite, Unity 6000.6.7f1, three music voices, one duck from 0 dB to -6 dB with a 1.2
kHz low-pass, applied over 0.4 s and released over 0.8 s. Each sample is one duck cycle of 102 frames; n = 900 cycles
per row (91,800 frames), three interleaved rounds. Figures are time per frame, median across cycles:

| Per frame, three music voices                          | Median |     p95 | Allocated |
| ------------------------------------------------------ | -----: | ------: | --------: |
| Composed per voice: gain, low-pass, high-pass          | 926 ns | 1186 ns |       0 B |
| Composed per voice: gain only                          | 485 ns |  567 ns |       0 B |
| Composed, duck held steady (shipped writes each frame) | 591 ns |  680 ns |       0 B |
| Mixer exposed parameter write, computed decibels       | 265 ns |  273 ns |       0 B |
| Mixer snapshot transition, nothing per frame           |  78 ns |   91 ns |       0 B |
| Mixer, duck held steady                                |  90 ns |   92 ns |       0 B |

The last three rows issue no native call per frame; 78 ns and 90 ns are the remaining ramp math and the probe loop. The
widest gap, 926 ns against 78 ns, is 0.005% of a 16.7 ms frame at 60 Hz. Performance does not decide this.

Three measured properties do:

- **Composition.** A mixer parameter holds one number. Two live ducks still need `min` in package code before one write,
  so a mixer removes the per-voice write and nothing else.
- **Read-back.** Every `AudioMixer.GetFloat` during and after a 0.4 s transition into the -6 dB snapshot returned the
  value last passed to `SetFloat` (0 dB), so package code cannot observe the applied gain. A diagnostics view of the
  real level would break (issue #14).
- **Granularity.** The per-voice ramp steps once per frame: at most 0.36 dB per step at 60 Hz and 1.08 dB at 20 Hz for
  this duck, because the end of an `OutQuad` ramp is steepest. A snapshot blends on the audio thread. This is the one
  measured advantage of the mixer, and only below about 30 Hz.

**Reopen if:** a game ducking below 30 Hz needs smoother steps than a per-frame ramp gives, or a measured play pattern
needs the mixer's audio-thread blend for a reason the table does not capture. The measured alternative is one snapshot
per composed duck level, not one mixer per duck.

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
