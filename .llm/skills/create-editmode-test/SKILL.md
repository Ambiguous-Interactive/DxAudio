---
name: create-editmode-test
description: Write EditMode tests in Tests/Editor for DxAudio - assembly references, engine-free versus Unity-object split, naming style, TestCaseData cases, cleanup scopes, and how to run the suites. Use when adding or modifying a Unity test.
metadata:
  category: Feature
---

# Create EditMode Tests

## Assembly facts

- Tests live in `Tests/Editor` against `WallstopStudios.DxAudio.Tests.Editor.asmdef`.
- References: `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `WallstopStudios.DxAudio`.
- `includePlatforms: ["Editor"]`, `precompiledReferences: ["nunit.framework.dll"]`, `defineConstraints: ["UNITY_INCLUDE_TESTS"]`.
- Namespace is `WallstopStudios.DxAudio.Tests.Editor`; `using` directives go inside the namespace block.
- The host Unity project must list `com.wallstop-studios.dxaudio` in `Packages/manifest.json` `testables`.

## Split by dependency

| Folder              | Use when                                                                 |
| ------------------- | ------------------------------------------------------------------------ |
| `Tests/Editor/Core` | Only `Runtime/Core` types. Runs in Unity **and** `dotnet test .net-tests`. |
| `Tests/Editor/Unity`| Needs `AudioSource`, `GameObject`, `ScriptableObject`, or a player.        |

A behavior in `Core` needs its test in `Core`; otherwise the plain .NET lane silently stops covering it.

## Naming

Methods state the behavior as a sentence, matching the existing suite: `ALoopHoldsItsVoiceUntilItsOwnHandleStopsIt`, `DucksComposeRampAndRelease`. Classes end in `Tests` and group by
feature. Do not use `Should_When_` prefixes.

Data-driven cases use `[TestCase(...)]` plus `TestName` so failures read as scenarios:

```csharp
[TestCase(0f, 0.04f, false, TestName = "Inside the window is refused")]
[TestCase(0f, 0.05f, true, TestName = "The window's end is allowed")]
```

## Structure

- Build inputs through small private static helpers, not inline constructors in assertions.
- One assertion cluster per behavior. Cover null, empty, stale-handle, and default cases explicitly.
- Use block comments for multi-line explanations; one-line notes may use `//`; API docs use `///`.

## Cleanup

- Create temporary `GameObject`, `AudioSource`, and `ScriptableObject` instances inside a scope that destroys them; Unity objects survive a test otherwise.
- Stop every loop handle a test starts. A leaked loop keeps its voice and fails later tests for the wrong reason.
- Restore `AudioListener.volume`, `Time.timeScale`, and player mute state when a test changes them.

## Running

```bash
dotnet test .net-tests --configuration Release        # engine-free lane
npm run unity:tests                                   # EditMode through the host bridge
unity -batchmode -projectPath <host> -runTests -testPlatform EditMode \
  -testResults <path> -logFile <log>                  # no -quit: it exits before tests run
```

The batchmode command deliberately omits `-quit`; with it, Unity shuts down before the runner starts.

## Related

- [measure-audio-behaviour](./measure-audio-behaviour/SKILL.md) - allocation and timing probes
- [voice-arbitration](./voice-arbitration/SKILL.md) - the policy these tests pin
