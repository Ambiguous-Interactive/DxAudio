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
| C# analysis      | Runtime/Core      | `npm run check:analysis`                         |
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
rendered characters, verb-first, issue reference at the end. Never edit a released section. It describes the package
since the last release: a `Changed`, `Fixed`, or `Security` entry must name behavior a release shipped, and a fix or
change to an unreleased feature folds into that feature's `Added` entry. See
[changelog](../../references/changelog.md).

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
bot, or automation that has not reviewed the pull request. Asking an existing reviewer to re-review after its threads are
resolved is allowed, and is the way out of a stale blocking review.

For any visual change, follow the [visual evidence rule](../../context.md#visual-evidence) and attach the comparison in the description.

## Tooling

Use the MCP servers first, and the shell only for what they cannot do.

| Task                                    | Use                                       | Shell fallback            |
| --------------------------------------- | ----------------------------------------- | ------------------------- |
| PR state, files, reviews, review threads | GitHub MCP `pull_request_read`            | `gh pr view --json`       |
| Check and job status, job logs           | GitHub MCP `actions_list`, `get_job_logs` | `gh pr checks`, `gh run`  |
| Open, update, or merge a PR              | GitHub MCP `create_pull_request`, `merge_pull_request` | `gh pr merge` |
| Issues and comments                      | GitHub MCP issue and comment tools        | `gh issue`, `gh api`      |
| Status, diff, log, show, branch, stage, commit | git MCP `git_status`, `git_diff`, `git_log`, `git_show`, `git_checkout`, `git_add`, `git_commit` | `git ...` |
| Fetch, push, worktree, conflicting merge | shell                                     | —                         |

## Landing

An agent with write access lands its own pull requests; it does not wait for a human to merge.

1. Wait until every check on the head commit passes, including `Unity CI Success`. That aggregate treats a skipped
   Unity matrix job as passing, so fork pull requests pass without waiting on jobs that never run. Read the result with
   the GitHub MCP server (`actions_list` for the run and its jobs, `get_job_logs` for a failure).
2. Merge the older pull request first. If a newer branch conflicts, update it from `main`, resolve, and let CI rerun.
3. Merge with the method the repository allows. This repository permits squash merges only, so a session's commits land
   as one; the pull request body and the review threads are the record of how they were built.
4. Delete the branch after the merge, and close the issue the body names when the merged code meets its acceptance.
5. Never force-push a branch under review, and never merge over a failing or queued check.

A stale review that blocks merge after every thread is resolved is a state to fix, not to wait on: ask the reviewer that
already reviewed to re-review, and when it stays stale, record who merged and why in the pull request.

## Close the planning record

Move completed work and evidence to `progress/`. Remove landed plan entries; leave `No task selected.` when the task ends. See [plan-hygiene](../plan-hygiene/SKILL.md).

## Related

- [bump-version-release](../bump-version-release/SKILL.md) - release flow
- [create-editmode-test](../create-editmode-test/SKILL.md) - test conventions
