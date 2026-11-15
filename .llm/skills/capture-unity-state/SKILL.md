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
- `tools.unity.run_tests` with a fixture-name `filter` returns per-test results in seconds.
- `EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave)` creates an object that belongs to no scene and does not dirty it. Plain `new GameObject` lands in the
  active scene and dirties it.
- Use `EditorSceneManager.NewPreviewScene()` and `ClosePreviewScene()` for isolated edit-mode work. Never `NewSceneMode.Single` over a dirty scene.

## Writes

- Never write or `DeleteAsset` under `Assets/` while the editor has the file open. Reimport prompts and the delete dirties the open scene.
- Re-read the open-scene list immediately before a write.
- Restore the original scenes before deleting drill assets. End with zero dirty scenes and no drill asset open.

## Play mode

Entering play mode over MCP stops the bridge answering until a human leaves play mode. Only start a play-mode run when a human is present, and never as the last operation of a
session.

## Artifacts

- Write captures to `.artifacts/` (gitignored). Never commit captures or generated config.
- A screenshot or state capture that backs a PR goes in the PR body only when it is user-facing; otherwise link the issue and keep the file local.

## Related

- [unity-mcp](./unity-mcp/SKILL.md) - bridge commands and recovery
