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

- `GC.GetAllocatedBytesForCurrentThread` is exact on plain .NET but reports 0 for every call under Unity's Mono, so in
  Unity it can only ever prove nothing.
- `GC.GetTotalMemory` reads current heap use, not cumulative allocation: a collection during the loop reclaims what the
  loop made. Unity's Mono also reports nothing for a few hundred KB of new strings - 500 new 225-character strings moved
  it by 4 KB, 40,000 moved it by 9 MB. A flat heap delta therefore never proves that a path allocates nothing.
- To measure an allocating path with the heap, keep every sample alive, read `GC.GetTotalMemory(forceFullCollection:
  true)` at both ends, and run enough samples that the live growth is over about a MB.
  `Tests/Editor/Unity/DiagnosticsTests.cs` (`HeapGrowth`) is the worked example.
- To prove a path does nothing at all, assert the observable state it must not change, such as an empty report or an idle
  voice. That check fails deterministically; a byte ceiling only fails once its slack is spent.
- For a path that must allocate nothing per call, run 100,000 calls and read `GC.GetTotalMemory(forceFullCollection:
  false)` at both ends, taking the smallest of three windows. Measured on 6000.6.7f1: `SfxPlayer.Play` grew the heap by
  0 to 29 KB, a planted 16-byte allocation per call grew it by 3.4 MB, and one window in a full-suite run committed a
  segment of about 300 KB. A 256 KB ceiling sits between them. `Tests/Editor/Unity/AllocationTests.cs` is the example.
- Unity's NUnit (3.5) has no `Is.AllocatingGCMemory` constraint; it does not compile. Do not use it in `Tests/Editor`.
- In Unity EditMode, warm the subject and the probe path first. Unwarmed probes false-positive on first-call JIT for
  string comparisons, dictionary adds, and other BCL paths.
- Warm at least 8 iterations. 32 is safer for paths that touch more BCL code.
- Run the same probe on a deliberate allocation to prove the probe can see one: 20 MB of arrays moved the heap by 21 MB
  on this host, while 500 small strings moved it by 4 KB. A probe that cannot fail is not evidence.

## Timing

- Collect before measuring: `GC.Collect(2, GCCollectionMode.Forced, blocking: true)`, `GC.WaitForPendingFinalizers()`, then collect again.
- Interleave variants and alternate which runs first; otherwise the first variant absorbs warmup.
- Report the median and p95, the sample count, and the host. A p95 over fewer than about 100 samples behaves like a maximum; assert the median there.
- Never relax a budget to make a run pass. Investigate the tail first.

## Audio-specific measurements

| Claim                          | Probe                                                       |
| ------------------------------ | ----------------------------------------------------------- |
| Play allocates nothing         | 100,000 plays on a full pool, heap growth, smallest of 3    |
| Stealing allocates nothing     | Same, with priority inversion on every play                 |
| Duck compose allocates nothing | Hold 1 and 8 ducks, compose per frame for 1000 frames       |
| Music crossfade cost           | 100 context changes, measure the per-change allocation and time |
| Loop handle safety             | Start, stop, reuse the voice, stop the stale handle         |
| Released-clip sweep allocation | Destroy a playing clip, run 500 frame sweeps, read heap growth |

## Recording

- Put measured numbers in `progress/session-NNN-*.md` with host, Unity version, and sample count.
- Put durable budgets and invariants in `.llm/references/audio-design.md`.
- Do not paste a single dramatic run as a finding. A stable p95 with a large single-run delta is usually a tiering artifact.

## Related

- [allocation-free-audio](./allocation-free-audio/SKILL.md) - what to fix
- [create-editmode-test](./create-editmode-test/SKILL.md) - where the test lives
