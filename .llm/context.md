# LLM Agent Instructions

Procedural skills live in the [skills/](./skills/) directory. See the generated [Skills Index](./skills/index.md) for
the full catalog with trigger conditions.

Every skill is a `SKILL.md` folder following the [Agent Skills](https://agentskills.io) format (`name` + `description`
frontmatter, metadata for grouping). Front-end pointer files (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`,
`.windsurfrules`, `.github/copilot-instructions.md`) are thin wrappers that delegate here; this file is the single
source of truth.

## Repository Overview

- **Package**: `com.wallstop-studios.dxaudio`
- **Version**: 0.3.0
- **Repository**: <https://github.com/Ambiguous-Interactive/DxAudio>
- **Root namespace**: `WallstopStudios.DxAudio`
- **Minimum Unity**: `2021.3` (development host: `6000.6.7f1`)
- **License**: MIT No Attribution

DxAudio is a data-driven sound-effects and music package: cue and track assets, a pooled voice player with priority and
rate limits, a music player with crossfades, overrides and composing ducks. It has no package dependencies. An
editor-only assembly holds authoring tools and is never referenced by the runtime. This folder is the git repo root,
nested under a Unity project's `Packages/`; open agent frontends with this folder as the workspace root.

## Project Structure

```text
Runtime/                              # The shipped assembly (WallstopStudios.DxAudio.asmdef)
  Core/                               # Engine-free logic: no UnityEngine types
    AudioMath.cs                      # Decibel/linear conversion, positive modulo
    AudioLoudness.cs                  # Loudness helpers
    MusicAttenuation.cs               # Fades, ramps and low/high-pass math
    MusicDuckStack.cs                 # Duck composition and holder bookkeeping
    MusicDuckLease.cs                 # Copy-safe lease structs
    MusicDuckHolders.cs               # Holder identity and release policy
    MusicEase.cs                      # Ease curves
    MusicHandOff.cs                   # Crossfade hand-off state
    MusicOverride.cs                  # Override stack
    MusicRamp.cs                      # Ramp state
    SfxPriority.cs                    # Priority ordering
    SfxSelection.cs                   # Round-robin, no-repeat take selection
    SfxThrottle.cs                    # Per-cue rate limiting
    SfxVoiceArbiter.cs                # Idle-first, priority, then oldest voice policy
    SfxVoiceState.cs                  # One pooled voice's observable state
    UnityObjects.cs                   # The only Unity-aware shim in Core
  Assets/                             # ScriptableObject data: SfxCue, SfxVariant, SfxLibrary,
                                      # MusicTrack, MusicLibrary, MusicDuck
  Players/                            # MonoBehaviours SfxPlayer and MusicPlayer, plus the VoiceClip helper
  Integrations/DxMessaging/           # Optional assembly: message-driven control for SfxPlayer. Guarded by a
                                      # version define, compiled only when com.wallstop-studios.dxmessaging is present
Editor/                               # Editor-only assembly: cue and duck inspectors, library validation
Tests/Editor/                          # EditMode tests, mirroring Core/ and Unity/
  Core/                               # Engine-free tests, shared with .net-tests
  Unity/                              # Tests that need Unity objects
  Integrations/DxMessaging/           # Tests that also need the optional package
.net-tests/                            # Plain .NET NUnit run of Runtime/Core + Tests/Editor/Core
scripts/                               # Typed Python harness: gates, lints, MCP sync, Unity bridge
.llm/                                  # This harness (context, skills, references, MCP config)
.devcontainer/                         # VS Code devcontainer: agent CLIs, MCP, Z.ai/OpenRouter
.github/                               # CI, release, meta and npm payload checks
```

## Audio Invariants

1. **`Runtime/Core` never references UnityEngine**, except `UnityObjects.cs`, the single Unity-aware shim. Core must
   compile and test on plain .NET through `.net-tests`.
2. **`SfxPlayer.Play` never allocates.** Voices, takes and selected variants are resolved from pre-sized storage. A new
   per-play allocation is a regression, not a style issue.
3. **A voice that holds a loop is never stolen.** Priorities order stealing only between non-looping voices; equal or
   higher priority drops the incoming cue.
4. **Ducks compose, they do not overwrite.** Every live `Many` duck contributes; the result is the most restrictive
   volume and filter set. A `One` duck asserts and releases as a single state. Released holders must never leave a stale
   contribution behind.
5. **Muted players keep state.** Muting detaches output, not voice or timer state, so loops and music resume in time.
6. **Serialized asset fields are a public contract.** Renaming one needs `[FormerlySerializedAs]` and a changelog entry.
   Published `0.2.x` assets must keep loading.
7. **Optional integrations are guarded, not declared.** An assembly under `Runtime/Integrations/` pairs a
   `versionDefines` entry for the other package with a `defineConstraints` entry naming that symbol, and `package.json`
   does not list the package. Anything else fails `package_check.py`.

## Skills Reference

See the generated [Skills Index](./skills/index.md). Regenerate it after adding or editing any skill:
`uv run --no-project scripts/uv_environment.py --repair && uv run scripts/generate_skills_index.py` (validated by
`scripts/lint_llm_instructions.py`).

### SKILL.md Contract

Each skill is a directory `.llm/skills/<skill-name>/SKILL.md` with frontmatter `name` (lowercase, hyphenated, matching
the directory), `description` (single-line ASCII, up to 1024 characters), and `metadata.category` in `Core`, `Workflow`,
or `Feature`. Bodies stay under 300 lines; bulk material moves to `.llm/references/`. See
[manage-skills](./skills/manage-skills/SKILL.md).

## Critical Rules

### C# and Unity

1. Target C# 9 (`LangVersion 9.0` in `.net-tests`); the package supports Unity 2021.3 and newer. Block-scoped
   namespaces; `using` directives go inside the namespace.
2. Format with CSharpier (`dotnet tool run csharpier format`). Tools are pinned in `.config/dotnet-tools.json`; never
   hand-format.
3. Explicit types and explicit access modifiers. Braces on every body. Prefer `sealed` and `readonly` where behavior
   allows.
4. Comparison operators read ascending: only `<`, `<=`, and `==` against the left operand. Write `0 < count`, not
   `count > 0`. Enforced by source review and the lint suite.
5. No LINQ in `Runtime/` or `Tests/`. Plain loops over concrete collection types keep audio paths allocation-free.
6. No `foreach` over a `string` in production: its enumerator is a class. Use a counting loop.
7. Unity object null checks use the Unity `==`/`!=` operators. Never use `?.`, `??`, `is null`, or an implicit bool on a
   `UnityEngine.Object`; those bypass the destroyed-object check. Tests assert `Assert.That(value == null)` /
   `Assert.That(value != null)`.
8. Public APIs validate inputs and prefer `TryXxx` plus `out` over exceptions for expected failure. Assign every `out`
   immediately before its return.
9. One top-level type per file. Unity object types match their file name.
10. `Runtime/Core` stays Unity-free. Engine types belong in `Runtime/Assets`, `Runtime/Players`, or the
    `UnityObjects.cs` shim.
11. Every visible file or folder carries a committed `.meta`. Dot paths (`.llm/`, `.github/`, `.config/`, `.git/`) never
    get one. See [create-unity-meta](./skills/create-unity-meta/SKILL.md).
12. Never ship development files in the npm tarball. `package.json` `files` lists `Runtime`, `Editor`, `Tests`, and
    package metadata only; `tooling~`-style paths are not used here.
13. Keep the public API additive where possible. Removing or renaming a public member is a breaking change and needs a
    major version plus a migration note.
14. Audio callbacks and per-frame code must not allocate, box, or log in the steady state. Measure with the allocation
    probe before claiming a fix.

### Testing

- EditMode tests live in `Tests/Editor`, grouped by feature. Name methods as behavior sentences
  (`ALoopHoldsItsVoiceUntilItsOwnHandleStopsIt`).
- Engine-free tests run twice: in Unity EditMode and on plain .NET via `dotnet test .net-tests`. Every new `Core`
  behavior needs a test in `Tests/Editor/Core`.
- Asset and player behavior belongs in `Tests/Editor/Unity` when it needs Unity objects.
- Data-driven `TestCaseData` cases beat repeated near-identical tests.
- Close every temporary `AudioSource`, `GameObject`, or `ScriptableObject` in a `using` cleanup scope; never leave
  pooled voices playing into the next test.
- A loop-voice test must stop its handle; a leaked loop keeps the pool busy and fails later tests for the wrong reason.

### Validation Ladder (Run After Each Change)

```bash
npm run check:fast     # changed files: format, lints, scoped self-tests
npm run check:gate     # full local gate before push
dotnet test .net-tests --configuration Release
npm run unity:tests    # EditMode through the host bridge, or batchmode when no bridge runs
```

Escalate on failure; never relax a budget or skip a lane to make a run pass. Record measured outcomes in `progress/`,
durable constraints here or in `.llm/references/`.

### Planning and session records

- `PLAN.md` holds the current task and selection rules only. Follow [plan-hygiene](./skills/plan-hygiene/SKILL.md).
- `GOAL.md` holds session scope and work style.
- Completed work, measurements and validation evidence go to `progress/session-NNN-brief-description.md`.
- `PLAN.md`, `GOAL.md`, `progress/`, and their Unity metas are local-only and gitignored. Never force-add them.

### User-Facing Copy

All human-readable text uses Simplified Technical English: short, direct, active. PR titles are at most 50 characters;
PR bodies carry `**Why:**`, up to five `**What:**` bullets, and `Fixes #N`. CHANGELOG entries are verb-first, one or two
sentences, at most 300 rendered characters. See [simple-writing](./skills/simple-writing/SKILL.md).

### Merging

An agent with write access merges its own pull requests once every check passes; it does not leave green work for a
human to merge. Merge the older pull request first, update a conflicting branch from `main` rather than stacking a new
one, and close the issue the work completes. See [ship-changes](./skills/ship-changes/SKILL.md#landing).

### LLM Attribution

Agent-written issues, PR bodies, reviews, and comments start with `DISCLOSURE: LLM-GENERATED TEXT` as the first line.
See [llm-attribution](./skills/llm-attribution/SKILL.md).

## Dev Container, MCP, and Agent Tooling

- Prefer the MCP servers over shell commands. Use the **GitHub MCP server** for every GitHub read or write: pull request
  state and files, checks and job logs, review threads, comments, issues, reviews, and merging. Use the **git MCP
  server** for local repository work: status, diffs, log, show, branches, staging, and commits. Reach for `gh`, `git`,
  or `curl` only for what a server does not expose, such as `git push`, `git fetch`, worktrees, or a merge that
  conflicts.
- Both servers return structured data, paginate predictably, and keep credentials out of the transcript. A shell
  pipeline over `gh api --jq` is the fallback, not the default.

- The devcontainer installs `nanocoder`, OpenCode v2, Codex, and Claude Code as the non-root user. Never use `sudo npm`;
  rerun `uv run .devcontainer/install_npm_tools.py` or rebuild.
- Secrets live in gitignored `.env.local` (template `.env.local.example`). Generated agent and MCP configs are
  gitignored and reference environment variables, never literals.
- `npm run mcp:sync` writes the six frontend configs; `npm run mcp:doctor` checks credentials and endpoints.
- Unity runs on the host. Container agents reach it through the host HTTP bridge: host `npm run unity:mcp:host`,
  container `npm run mcp:sync`. See [unity-mcp](./skills/unity-mcp/SKILL.md).
- Prefer the Unity bridge for tests (`npm run unity:tests`) over a cold batchmode import.

## Front-End Pointer Files

| File                              | Consumer                                       | Contract                          |
| --------------------------------- | ---------------------------------------------- | --------------------------------- |
| `AGENTS.md`                       | Codex, OpenCode, nanocoder, Goose, most agents | Delegates to `./.llm/context.md`  |
| `CLAUDE.md`                       | Claude Code                                    | Delegates to `./.llm/context.md`  |
| `.cursorrules`                    | Cursor                                         | Delegates to `./.llm/context.md`  |
| `.windsurfrules`                  | Windsurf                                       | Delegates to `./.llm/context.md`  |
| `.github/copilot-instructions.md` | GitHub Copilot                                 | Delegates to `../.llm/context.md` |
