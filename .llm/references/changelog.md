# Changelog

`CHANGELOG.md` tells a consumer what changed since the last release. It is a release record, not a commit log or a list
of everything that changed since the last push.

## Describe the release, not the branch

The `[Unreleased]` section describes the next release. Every `Changed`, `Fixed`, or `Security` entry in it must name
behavior that a released version actually shipped. A fix or behavior change for a feature that has never shipped is
not a `Fixed` or `Changed` entry: from the consumer's side the feature does not exist yet, so it ships correct. Fold
the user-visible guarantee into that feature's `Added` entry and drop the commit narration.

This follows the sibling `unity-helpers` rule "Unreleased Features: Edit In Place" and the Keep a Changelog principle
that a changelog is written for people, not generated from a diff.

| Change                                                           | Correct entry                                             |
| ---------------------------------------------------------------- | --------------------------------------------------------- |
| Fix a defect in a feature first added in `[Unreleased]`          | Extend that feature's `Added` entry. No `Fixed` entry.    |
| Change behavior of a feature in `[Unreleased]`                   | Extend that feature's `Added` entry. No `Changed` entry.  |
| Fix a security defect in a feature first added in `[Unreleased]` | Extend that feature's `Added` entry. No `Security` entry. |
| Fix a defect in behavior a release shipped                       | `Fixed` entry.                                            |
| Change behavior a release shipped                                | `Changed` entry.                                          |
| Performance work on an `[Unreleased]` feature                    | Fold the final cost into that feature's `Added` entry.    |

A release candidate reads `[Unreleased]` to choose the version bump. An unreleased feature filed only under `Changed`,
`Fixed`, or `Security` hides the minor bump its `Added` entry would earn, and it tells a consumer to expect a patch.

## Decide with git and the released behavior

Use the newest release tag that actually shipped, not memory and not a tag alone. A failed or incomplete publication
can leave a tag behind; confirm that the tag has a published GitHub Release before you use it as the baseline.

```bash
git tag --sort=-v:refname | head -5
gh release list --exclude-drafts --limit 5
```

File and symbol history is a fast first check, not proof: a release can move behavior into a new file or symbol, or
keep an old file while changing only part of it. The decisive test is whether the behavior itself shipped. Read the
tagged implementation, tests, or docs, and use history when a symbol moved:

```bash
git show <tag>:Runtime/Core/Dsp/DspReverb.cs
git log --oneline -S '<symbol>' <tag> -- <path>
```

If the behavior is absent at the tag, the feature is unreleased and its fixes and changes belong in `Added`. If
consumers received the behavior from that release, a `Fixed`, `Changed`, or `Security` entry is accurate. When the
file check is ambiguous, read the tagged code, tests, or docs before choosing the section. A moved or renamed file
still holds the same shipped behavior.

## Example

Wrong: a new effect and its pre-release optimization are split into two sections.

```markdown
### Added

- Add a Phaser effect for DSP chains and graphs with adjustable sweep rate and depth.

### Changed

- Reduce CPU time for Phaser when several voices play at once.
```

Right: the effect ships once, with its final cost in the `Added` entry.

```markdown
### Added

- Add a Phaser effect for DSP chains and graphs with adjustable sweep rate, depth, and low CPU cost across several voices.
```

## Released sections are immutable

Never edit a released section. A defect that a release shipped stays a `Fixed` entry even when the fix lands in a
later minor release. The release history is the record a consumer uses to decide whether to upgrade.

## Related

- [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
- [Ship changes](../skills/ship-changes/SKILL.md) - the changelog checklist before a commit
- [Bump version and release](../skills/bump-version-release/SKILL.md) - how the release candidate reads this section
