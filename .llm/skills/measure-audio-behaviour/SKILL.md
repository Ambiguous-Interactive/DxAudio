---
name: measure-audio-behaviour
description: Measure DxAudio behavior with warmed allocation probes and timing windows - GC.GetAllocatedBytesForCurrentThread, NUnit AllocatingGCMemory, sample counts, statistics, and noise control. Use when writing or debugging an allocation test, a benchmark, or a performance claim in an issue or PR.
metadata:
  category: Feature
---

# Measure Audio Behaviour

## Allocation probes

- In `.net-tests` (plain .NET), measure with `GC.GetAllocatedBytesForCurrentThread()` around a warmed region:

```csharp
for (int i = 0; i < 8; i++)
{
    subject.Play(cue);
}

long before = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 1000; i++)
{
    subject.Play(cue);
}

long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
```

- In Unity EditMode, `Assert.That(() => subject.Play(cue), Is.Not.AllocatingGCMemory())` works, but **warm the subject and the probe path first**. Unwarmed probes false-positive on
  first-call JIT for string comparisons, dictionary adds, and other BCL paths.
- Warm at least 8 iterations. 32 is safer for paths that touch more BCL code.
- Run the same probe on a deliberate allocation (for example `new byte[16]`) to prove the probe can see an allocation. A probe that cannot fail is not evidence.

## Timing

- Collect before measuring: `GC.Collect(2, GCCollectionMode.Forced, blocking: true)`, `GC.WaitForPendingFinalizers()`, then collect again.
- Interleave variants and alternate which runs first; otherwise the first variant absorbs warmup.
- Report the median and p95, the sample count, and the host. A p95 over fewer than about 100 samples behaves like a maximum; assert the median there.
- Never relax a budget to make a run pass. Investigate the tail first.

## Audio-specific measurements

| Claim                          | Probe                                                       |
| ------------------------------ | ----------------------------------------------------------- |
| Play allocates nothing         | `Play` 1000 times on a full pool, one cue                   |
| Stealing allocates nothing     | Same, with priority inversion on every play                 |
| Duck compose allocates nothing | Hold 1 and 8 ducks, compose per frame for 1000 frames       |
| Music crossfade cost           | 100 context changes, measure the per-change allocation and time |
| Loop handle safety             | Start, stop, reuse the voice, stop the stale handle         |

## Recording

- Put measured numbers in `progress/session-NNN-*.md` with host, Unity version, and sample count.
- Put durable budgets and invariants in `.llm/references/audio-design.md`.
- Do not paste a single dramatic run as a finding. A stable p95 with a large single-run delta is usually a tiering artifact.

## Related

- [allocation-free-audio](./allocation-free-audio/SKILL.md) - what to fix
- [create-editmode-test](./create-editmode-test/SKILL.md) - where the test lives
