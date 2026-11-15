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

**Decision:** no `AudioMixer` or snapshot requirement. Ducks compose in `MusicDuckStack`.

**Reason:** zero dependencies, testable on plain .NET, and composition is order-independent.

**Reopen if:** milestone 3 measures a mixer approach that is materially simpler for users at no dependency cost.

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
