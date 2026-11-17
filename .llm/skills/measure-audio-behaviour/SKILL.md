---
name: measure-audio-behaviour
description: Measure DxAudio behavior with warmed allocation probes, timing windows and applied output gain - GC.GetAllocatedBytesForCurrentThread, NUnit AllocatingGCMemory, heap growth, sample counts, statistics, noise control, and the editor probe for voice and mixer group gain. Use when writing or debugging an allocation test, a benchmark, an output-gain or duck measurement, or a performance claim in an issue or PR.
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

## Output gain probe

Edit mode produces no audio samples: `AudioSource.time` stays `0.000`, `AudioSource.GetOutputData` and
`AudioListener.GetOutputData` return zeros, and `AudioRenderer` captures 0 frames, while `AudioSettings.dspTime` still
advances. Only play mode or a player build measures samples. The bridge refuses play mode, and a PlayMode source
never imports an asset, so a sample measurement runs as its own batchmode editor or a player build
([unity-mcp](../unity-mcp/SKILL.md#play-mode)). Everywhere else, measure the gain the editor has
scheduled the audio thread to apply, read from the objects the package writes. Paste this into `script_eval` with
`internals: true`; the editor mixer types are internal:

```csharp
float ToDb(float linear) { return 0f < linear ? 20f * Mathf.Log10(linear) : float.NegativeInfinity; }

float GroupChainDb(UnityEngine.Audio.AudioMixerGroup group) {
    UnityEditor.Audio.AudioMixerGroupController target = group as UnityEditor.Audio.AudioMixerGroupController;
    if (target == null) { return group == null ? 0f : float.NaN; }
    UnityEditor.Audio.AudioMixerController mixer = target.controller;
    UnityEditor.Audio.AudioMixerSnapshotController snapshot =
        mixer.TargetSnapshot as UnityEditor.Audio.AudioMixerSnapshotController;
    if (mixer.masterGroup == null || snapshot == null) { return float.NaN; }
    var pending = new System.Collections.Generic.Queue<(UnityEditor.Audio.AudioMixerGroupController group, float db)>();
    pending.Enqueue((mixer.masterGroup, 0f));
    while (0 < pending.Count) {
        (UnityEditor.Audio.AudioMixerGroupController current, float db) = pending.Dequeue();
        float fader;
        if (snapshot.GetValue(current.GetGUIDForVolume(), out fader)) { db += fader; }
        if (current == target) { return db; }
        UnityEditor.Audio.AudioMixerGroupController[] children = current.children;
        if (children != null) { for (int i = 0; i < children.Length; i++) { pending.Enqueue((children[i], db)); } }
    }
    return float.NaN;
}

float VoiceGainDb(AudioSource voice) {
    if (voice == null) { return float.NaN; }
    if (voice.mute) { return float.NegativeInfinity; }
    float listenerDb = voice.ignoreListenerVolume ? 0f : ToDb(AudioListener.volume);
    return ToDb(voice.volume) + listenerDb + GroupChainDb(voice.outputAudioMixerGroup);
}
```

- The voice term is `AudioSource.volume`. `SfxPlayer` writes the cue's volume once at play time and never rewrites it.
  `MusicPlayer.Tick` rewrites it every frame as `trim * fade * duck`, so a music voice carries all three and an SFX voice
  carries the cue volume only. `mute` reports silence as `-inf`, never as a gain.
- The group term adds the selected (`TargetSnapshot`) snapshot's fader dB for the source's group and every ancestor. That
  is a stored value: it is not the Start Snapshot the mixer begins with when those differ, and it does not show a
  transition's intermediate state. Read the snapshot, not `AudioMixerGroupController.GetValueForVolume`, which read
  `0.000` for a `-6 dB` write on a fresh mixer.
- Drive a player's frames by invoking its internal `Tick` with reflection (`1f / 60f`) when the editor is not in play mode;
  `Update` never runs there.
- Measured on 6000.6.7f1 on this Windows 11 host: an `SfxPlayer` cue authored at `volume: 0.5`, routed to a `Music` group
  at `0 dB`, read `-6.0206`; writing `-6` to that group's snapshot fader read `-12.0206`; `SfxPlayer.Muted = true` read
  `-inf`. With the fader back at `0` and a `MusicPlayer` voice left unrouted, a duck authored with `applySeconds: 0.25`
  and `volumeScale 0.5011872` (`-6.00 dB`) read `0`, `-2.82`, `-5.09` and `-6.00` dB after 0, 5, 10 and 15 simulated
  frames, then held `-6.00`.
- Pair every read with one change that must move it: an unwritten group fader reads `0.000 dB`, the same number a broken
  probe returns for everything. A probe that shows the same value before and after a known `-6 dB` write has proven
  nothing.
- Limits: the probe cannot see filter response, spatial attenuation, mixer effects, a mixer group's mute or solo state, or
  clipping, and it reads the editor's selected snapshot rather than a value observed from the audio thread. A muted group
  still reads its fader dB. The group term needs the editor controller type, so outside the editor it reads `NaN`. It
  stops at the source mixer's master, so a mixer routed into another mixer's group hides that downstream fader. An exposed
  parameter written with `AudioMixer.SetFloat` takes over from the snapshot for the rest of the session, so read
  `AudioMixer.GetFloat` for that parameter too when a game drives one.
- `AudioMixerController.GetGroupVUInfo` is not a substitute for this probe: on the same host it read `-80 dB` for a group
  at `0 dB`, still `-80 dB` after a `-12 dB` write, and `-3.6e25` on another call.

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
