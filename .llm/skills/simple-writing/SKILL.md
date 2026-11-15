---
name: simple-writing
description: Write user-facing copy in Simplified Technical English - short, direct, active sentences for PRs, commits, issues, comments, and CHANGELOG entries. Use when writing or editing any text a human reads in this repo.
metadata:
  category: Core
---

# Simple Writing (STE)

All human-readable text uses Simplified Technical English. Short. Direct. No fluff.

## Rules

1. One idea per sentence. At most about 20 words. Active voice. Present tense.
2. Common words. No idioms, hype, or filler ("very", "in order to", "it should be noted").
3. Lists over prose. Links over restatement. Never restate a diff.
4. Write for a reader who knows Unity and C# but not this repo's history.
5. ASCII only. No em-dashes, smart quotes, or emoji.
6. Length budgets are hard limits. Cut before adding.
7. Revise a comment in place. Never stack a second comment above the stale one. State a rule once, at the point where it is not obvious.

## Structure by artifact

| Artifact        | Structure                                                                                                     | Budget        |
| --------------- | ------------------------------------------------------------------------------------------------------------- | ------------- |
| PR title        | Imperative, at most 50 characters. "Add X", "Fix Y".                                                          | 1 line        |
| PR body         | `**Why:**` one sentence, up to five `**What:**` one-line bullets, `Fixes #N`.                                  | about 10 lines|
| CHANGELOG entry | Verb first (`Add`, `Fix`, `Change`), user effect, issue reference. No root cause or mechanism tour.           | 1-2 sentences |
| Commit subject  | Imperative, at most 72 characters, no trailing period.                                                        | 1 line        |
| Commit body     | Why, then what, then one evidence line. Wrap near 72 characters.                                               | about 8 lines |
| Review comments | 1-3 sentences. Say what to change and why.                                                                    | 3 lines       |
| Code comments   | State only what the code cannot say: constraints, invariants, non-obvious why.                                | 4 lines       |
| Issues          | Problem, evidence, fix - one short paragraph each.                                                            | short         |

## Example

Bad: "This PR implements a comprehensive overhaul of the voice arbitration subsystem, leveraging modern priority-aware scheduling to dramatically improve audio quality."

Good: "A Low cue no longer holds a voice a High cue needs. Priority now decides only when no voice is idle."

## Related

- [llm-attribution](./llm-attribution/SKILL.md) - the disclosure line GitHub text needs
