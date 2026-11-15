---
name: unity-version-guards
description: Choose correct UNITY_X_Y_OR_NEWER guards for Unity APIs in DxAudio - verify the true introduction and deprecation versions, keep fallbacks compiling on the 2021.3 floor, and sweep for guard mismatches. Use when adding an #if UNITY_ guard, calling a newer Unity API, or fixing a compile break or CS0618 warning on a supported version.
metadata:
  category: Feature
---

# Unity Version Guards

## Rule

A `#if UNITY_X_Y_OR_NEWER` constant is the API's true introduction version, verified against evidence. Never use the version of the editor you happen to run. Too low breaks older
editors. Too high silently degrades behavior or emits deprecation warnings on editors that already have the API.

## Verify before guarding

1. Per-version docs bisect: `https://docs.unity3d.com/{version}/Documentation/ScriptReference/{Page}.html`. A 404 means the API is absent in that version. The surviving page's
   "Added in Version" marker confirms.
2. Deprecation facts on the host: reflect over the method and read `ObsoleteAttribute` (`IsError == false` means a compile warning).
3. Check the .NET profile too. DxAudio targets C# 9 and Unity 2021.3, so do not use APIs that require a newer BCL.

## Sweep

- Grep the guard constant and the guarded API name together. Every deprecated call site must sit inside the `#else` of a guard at or above the deprecation version.
- Every reference to a guarded-API type, including a private field declaration, must sit inside a matching guard. An unguarded field of a newer type breaks older editors even when
  every use is guarded.
- Reject inconsistent constants across files guarding the same API.

## This repository

- The package floor is Unity 2021.3. `Runtime/Core` uses no Unity types, so it needs no guards; guards belong in `Runtime/Assets`, `Runtime/Players`, and `UnityObjects.cs`.
- `.net-tests` compiles `Runtime/Core` with `LangVersion 9.0` and warnings as errors. A C# feature newer than 9 fails the fast lane before Unity sees it.
- The CI matrix runs the Unity suite on the oldest and newest supported editors. A version-specific compile break shows up there, not on the development host.
- Verify a deprecation claim on the pinned host before adding a fallback; a shim compilation cannot prove an overload exists in an older editor.

## Tripwires

- `dotnet test .net-tests` catches engine-free C# errors.
- The Unity EditMode lane on the 2021.3 leg catches type-level leaks below the floor.
- Zero warnings is the rule; any CS0618 is a failure.
