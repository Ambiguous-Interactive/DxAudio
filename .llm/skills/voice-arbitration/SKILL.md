---
name: voice-arbitration
description: The DxAudio voice-pool policy - idle voices first, priority decides only when the pool is full, the least important and then oldest voice is stolen, and a held loop is never taken. Use when changing SfxPlayer, SfxVoiceArbiter, SfxVoiceState, loop handles, or the reasons a cue is dropped.
metadata:
  category: Feature
---

# Voice Arbitration

## Policy

| Situation                     | Result                                                        |
| ----------------------------- | ------------------------------------------------------------- |
| A voice is idle               | The cue takes it, searching round-robin from `roundRobinStart`.|
| Every voice is busy           | The cue takes the least important voice of **strictly lower** priority, oldest first. |
| Only equal or higher priority | The cue is dropped with reason `NoVoice`.                     |
| A voice holds a loop          | It is never taken.                                            |

Never stealing between equals is deliberate: cutting an audible sound is worse than one that never starts. Priority exists so a burst of footsteps cannot drop a beat.

## Invariants

1. `SfxVoiceArbiter.TryChoose` is pure and engine-free. Keep it that way; the .NET lane is the fast feedback loop.
2. `IsFree` means not playing. `CanBeTakenFrom` excludes loops and voices already past their end.
3. Ordering inside a priority tier is by `startedAtUnscaledTime`, oldest first. Do not replace it with voice index; index order would repeatedly cut the same slot.
4. A looping cue holds its voice until its own handle stops it. `StopLoop` with a stale handle must stop nothing; handles carry voice generation.
5. Dropped plays report through `SfxPlayer.CueDropped` with a `SfxPriority`-independent reason (`Throttled`, `NoVoice`, `LoopLimit`). Do not log per drop in the steady state.

## Changing the policy

1. Write the failing test first in `Tests/Editor/Core/SfxVoiceArbiterTests.cs` (engine-free) or `Tests/Editor/Unity/PlayerTests.cs` (player).
2. Update the table in `README.md` if user-visible behavior changes.
3. Add a `CHANGELOG.md` entry; this is observable behavior.
4. Re-measure the play path with [measure-audio-behaviour](./measure-audio-behaviour/SKILL.md); stealing must not allocate.

## Related

- [allocation-free-audio](./allocation-free-audio/SKILL.md) - zero-allocation play path
- [duck-composition](./duck-composition/SKILL.md) - the music-side equivalent
