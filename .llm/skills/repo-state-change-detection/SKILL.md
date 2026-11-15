---
name: repo-state-change-detection
description: Key caches and CI skip-gates on content and whole change ranges, not on change-state signals. Use when touching preflight caches, harness_scope mappings, workflow paths filters, or a conditional job's diff gate.
metadata:
  category: Core
---

# Repo-State Change Detection

A cache key or skip-gate answers "did the inputs change", so it must hash the inputs themselves, not a signal that correlates with change. A status line can stay byte-identical
across two rewrites; a committed edit is invisible to `git status`.

## Rules

1. Hash content, diff ranges.
2. A PR base is `github.event.pull_request.base.sha`; when the shallow clone lacks it, use the merge ref's first parent. A push base is `github.event.before`, fetched on demand.
   `HEAD~1` alone skips a gate when an earlier commit of a multi-commit push touched the input.
3. A workflow `paths:` filter must cover both halves of a lane's surface: every file the suite reads and every file its gates enforce.
4. When a self-test or gate starts reading a new file class, add that class to the filter in the same change.

## This repository

- `scripts/harness_scope.py` maps changed tooling paths to self-test files. Its verdict fails safe: an unmapped path selects the whole suite. Update the map when a script or test
  moves; a stale name only costs a full run, never a silent skip.
- `scripts/should_run_self_tests.py` resolves the CI scope. `.github/workflows/llm-lint.yml` must list every file its lanes read or enforce.
- Do not key a local gate on `git status` alone. The fast check diffs the working tree against the merge base and also scans the index.

## Pinned by

- `scripts/tests/test_harness_scope.py` and `scripts/tests/test_fast_check.py`.
