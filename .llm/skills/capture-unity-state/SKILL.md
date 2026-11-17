---
name: capture-unity-state
description: Read editor and game state safely over the Unity MCP bridge and store captures in .artifacts - refusal preflights, read-only eval, scene hygiene, screenshots, and dirty-scene rules. Use when capturing evidence for a fix, taking a screenshot, or inspecting a host editor.
metadata:
  category: Feature
---

# Capture Unity State

## Refusal preflight

Before anything that swaps, saves, reimports, compiles, or enters play mode, run one read-only eval:

- `EditorApplication.isPlaying` and `EditorApplication.isPlayingOrWillChangePlaymode`
- `EditorApplication.isCompiling`
- every open scene's `path` and `isDirty`

A dirty or playing editor means **refuse and name the scenes**. A modal dialog blocks the editor main thread and every bridge call times out, which reads as a hang.

## Safe reads

- `tools.unity.eval` compiles C# statements. End with `return (...);`. Keep return values simple strings or numbers; heavier reflective results are unreliable.
- Bound every host file scan. A recursive `Directory.GetFiles` over `%USERPROFILE%\AppData` blocks the editor main thread for minutes, and every bridge call times out until it returns. Name exact paths, cap the file count, and skip broad trees; nothing else in the session can run meanwhile.
- `tools.unity.run_tests` with a fixture-name `filter` returns per-test results in seconds.
- `EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave)` creates an object that belongs to no scene and does not dirty it. Plain `new GameObject` lands in the
  active scene and dirties it.
- Use `EditorSceneManager.NewPreviewScene()` and `ClosePreviewScene()` for isolated edit-mode work. Never `NewSceneMode.Single` over a dirty scene.

## Writes

- Never write or `DeleteAsset` under `Assets/` while the editor has the file open. Reimport prompts and the delete dirties the open scene.
- Re-read the open-scene list immediately before a write.
- Restore the original scenes before deleting drill assets. End with zero dirty scenes and no drill asset open.

## Play mode

The bridge refuses play-mode calls, and that is the policy rather than a limitation: entering play mode over the bridge
stops the editor answering, and a PlayMode test run aborted the pinned editor on 2026-11-17 (`STATUS_FATAL_APP_EXIT` in
its crash dump). A PlayMode suite runs as its own batchmode editor through `scripts/run_unity_validation.py --suite
playmode`, never through this bridge. See [unity-mcp](../unity-mcp/SKILL.md#play-mode).

## Evidence before you clear anything

`console_clear` deletes the only record of a compile failure or a crash breadcrumb, and the console cannot be replayed.
Read `console_get` first and keep the text. The same caution applies to restarting or reloading the editor: copy the log
tail before it rotates, because Unity keeps one `Logs/Editor-prev.log` and overwrites it on the next start.

## Artifacts

- Write captures to `.artifacts/` (gitignored). Never commit captures or generated config.
- A screenshot or state capture that backs a PR goes in the PR body only when it is user-facing; otherwise link the issue and keep the file local.

## Related

- [unity-mcp](./unity-mcp/SKILL.md) - bridge commands and recovery
