# Verification host environment

The host that runs the Unity editor for this repository is unusual, and it has lost its editor four times in three
days. Record it before blaming a change: a host crash costs the session its evidence, because Unity overwrites
`Logs/Editor.log` on the next start.

## Hardware and software

| Item       | Value                                                                    |
| ---------- | ------------------------------------------------------------------------ |
| CPU        | Snapdragon X Elite X1E80100 (ARM64), 12 cores, 65 GB RAM                 |
| GPU        | Qualcomm Adreno X1-85, Direct3D 12 level 12.1                            |
| OS         | Windows 11 10.0.26300                                                    |
| Editor     | Unity 6000.6.7f1, native ARM64                                           |
| Audio      | Stereo output, 48 kHz                                                    |
| CI editors | 6000.5.2f1 on pull requests and main; 2021.3.45f1 and 6000.5.2f1 nightly |

The editor version on this host is not one any CI lane proves. A local result can therefore differ from a lane result
for a reason that is not the change, and a host crash cannot be a package defect until it reproduces elsewhere.

## Crash record

Read from the minidumps under `%LOCALAPPDATA%\Temp\Unity\Editor\Crashes\`:

| Crash (UTC)         | Code         | Faulting instruction          | Detail                        |
| ------------------- | ------------ | ----------------------------- | ----------------------------- |
| 2026-11-15 05:51:12 | `0xC0000005` | `Unity.dll+0x2F27E54`         | read at `0xA4`                |
| 2026-11-15 18:53:57 | `0xC0000005` | `mono-2.0-bdwgc.dll+0x26D12F` | read at `0xC2A00000C2A00000`  |
| 2026-11-16 20:37:50 | `0xC0000005` | `Unity.dll+0x2F27E54`         | same instruction as the first |
| 2026-11-17 06:05:48 | `0x40000015` | `KERNELBASE.dll` (raise)      | abort, no parameters          |

Three are access violations, and two of those are the same instruction in Unity's own code on different days. One is
Boehm GC reading a wild pointer. The abort is the only one near play mode, and the log that named it rotated away. The
Qualcomm D3D12 modules `qcdx12arm64xum.dll` and `qcgpuarm64xcompilercore.dll` are loaded in every dump but fault in
none, so the record names Unity and Mono, not the driver.

Session 014 recorded its host editor closing mid-session while it rewrote clip import settings, with no play mode
involved; the 2026-11-16 dump sits in that window. Issue #55 owns attribution.

## Reading a dump

`crash.dmp` is a minidump. The three fields that matter are cheap to read with any dump parser:

- **Exception code** in the exception stream. `0xC0000005` is a memory fault; `0x40000015` is `STATUS_FATAL_APP_EXIT`,
  the editor's own abort; `0xC0000409` is a stack buffer overrun.
- **Exception address** resolved against the module list names the faulting module and offset.
- **Exception parameters** for `0xC0000005` give the access kind (0 read, 1 write) and the address, which tells a null
  dereference (`0xA4`) from a wild pointer (`0xC2A00000C2A00000`).

The bridge helps here: `bridge_diagnostics` returns `editorLogTail`, the last lines of the pinned project's
`Logs/Editor.log` with credentials redacted, and `npm run unity:mcp:doctor` prints them beside the crash warning. The
bridge outlives the editor, so that is the one copy that survives a restart.

## What to do

1. Copy the log tail and the newest `Crash_*` folder before restarting the editor.
2. Match the crash against the table above. An access violation in `Unity.dll` or `mono-2.0-bdwgc.dll` is a host
   fault until proven otherwise; do not attribute it to a package change.
3. Re-run the measurement on an editor version CI proves, or let the lane carry it. Batchmode runs a separate editor
   process and never touches the bridge.
4. File the finding on issue #55 with the code, the faulting module, and the access address, so the record grows
   instead of restarting from zero.
