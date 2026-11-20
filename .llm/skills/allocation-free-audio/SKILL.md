---
name: allocation-free-audio
description: Keep the DxAudio play loop allocation-free on Unity's Mono - the facts about foreach, strings, sorted collections, closures, and pooling that change a per-play allocation, plus the sweep checklist for a new audio path. Use when optimizing a play, update, duck, or music path, or when an allocation test fails.
metadata:
  category: Core
---

# Allocation-Free Audio

The player contract is that `SfxPlayer.Play` does not allocate. Measure before and after; a claim without a probe is not a fix.

## What allocates on Unity's Mono

- `foreach` over a value typed `IEnumerable<T>` boxes a struct enumerator. Declare the concrete collection type, or use a counting loop.
- `foreach` over a `string` always allocates (`CharEnumerator` is a class). Use a counting loop in production; `foreach` is fine in tests.
- Every pass over a `SortedDictionary` or `SortedSet` allocates: `Keys`/`Values` build a fresh collection and the enumerator is a class.
- A captureless lambda is a cached singleton; adding one capture turns it into a per-call closure. Mark captureless lambda arguments `static` where the language version allows.
- `ConcurrentStack.Push` allocates a node. A pool built on it allocates on every return.
- `Array.Resize` allocates a replacement when the size changes. It is not in-place growth.
- `string` concatenation, interpolation in a loop, and `Split`/`Join` allocate. Build into a reused `StringBuilder` or write once.

## Free once warmed

`char.ToLowerInvariant`, ordinal `string` comparisons, and `HashSet`/`Dictionary` adds are allocation-free after warmup. Do not "optimize" them preemptively.

## Audio-specific rules

1. Pre-size the voice pool and every per-voice buffer from the library's voice count. Growth belongs in configuration, not in `Play`.
2. Resolve cue keys with an ordinal comparer and a `Dictionary<string, ...>` built at load.
3. Reuse the take-selection state; do not build a candidate list per play.
4. Duck composition writes into owner storage. It does not create a list of live ducks.
5. Diagnostics that iterate every voice are opt-in. `DescribeDucks()` may allocate; the per-frame path must not.
6. Do not log per drop, per duck, or per voice in the steady state.

## Sweep checklist

- New public method on a play path? Run the allocation probe.
- New `foreach` over an interface-typed value? Replace with a concrete type or a counted loop.
- New string building? Reuse a builder or write once.
- New lambda on a registration path? Declare it `static` if captureless.
- New pool? Prove rent, use, and return allocate nothing.
- New sorted collection read per frame? Snapshot with a version counter.

## Related

- [measure-audio-behaviour](../measure-audio-behaviour/SKILL.md) - how to prove it
