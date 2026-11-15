---
name: ship-changes
description: Validate and ship DxAudio changes - CSharpier, the Python lint gate, .meta companions, .NET core tests, Unity EditMode tests, commit subjects, and PR handoff. Use before committing, pushing, or opening a pull request.
metadata:
  category: Core
---

# Ship Changes

## Validation ladder

| Layer             | Scope             | Command                                  |
| ----------------- | ----------------- | ---------------------------------------- |
| Fast check        | changed files     | `npm run check:fast`                     |
| Full local gate   | every check       | `npm run check:gate`                     |
| Engine-free tests | Runtime/Core      | `dotnet test .net-tests --configuration Release` |
| Unity EditMode    | assets + players  | `npm run unity:tests`                    |
| CI                | ubuntu + windows  | `.github/workflows/llm-lint.yml`         |

Escalate on failure. Never relax a budget, skip a lane, or delete a test to get green.

## Pre-commit checklist

1. Format touched `.cs` files: `dotnet tool run csharpier format <paths>`.
2. Format touched Markdown: `npm run format:md`; verify with `npm run format:md:check`.
3. `.llm` changed: `npm run lint:llm` (use `lint:llm:fix` for index or version drift, then review the diff).
4. Added any visible file or folder: commit its `.meta` companion; `npm run lint:meta-companions` fails otherwise.
5. `scripts/**` or `.devcontainer/**` changed: `uv run scripts/tests/run_all.py`.
6. `package.json`, `files`, or shipped assets changed: `npm run package:check` (or `npm pack --dry-run`) and confirm the tarball ships only `Runtime`, `Tests`, and metadata.
7. Unity suites: `npm run unity:tests` when runtime, asset, or player behavior changed.

## Commit shape

- Imperative subject, at most 72 characters, subsystem first. Body explains why, then what, then how it was verified.
- Reference the issue ID in the body. One logical change per commit.
- Never commit generated configs, `.env.local`, `progress/`, `PLAN.md`, or `GOAL.md`.

## CHANGELOG

`CHANGELOG.md` is user-facing only: features, fixes, visible behavior, serialized-data changes. No tests, CI, tooling, or release mechanics. One or two sentences, at most 300
rendered characters, verb-first, issue reference at the end. Never edit a released section.

## Pull requests

```markdown
DISCLOSURE: LLM-GENERATED TEXT

**Why:** <one sentence>

**What:**

- <one change, one line, up to 12 words>
- <two to five bullets>

Fixes #N
```

Title at most 50 characters, naming the user-visible effect. No validation transcripts, file tours, or review narration in the body. Never request a review from a person, team,
bot, or automation.

## Close the planning record

Move completed work and evidence to `progress/`. Remove landed plan entries; leave `No task selected.` when the task ends. See [plan-hygiene](./plan-hygiene/SKILL.md).

## Related

- [bump-version-release](./bump-version-release/SKILL.md) - release flow
- [create-editmode-test](./create-editmode-test/SKILL.md) - test conventions
