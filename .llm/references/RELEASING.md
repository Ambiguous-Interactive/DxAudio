# Releasing

The weekly workflow starts Sunday at 4 a.m. Pacific time. It reads the
`Unreleased` section in `CHANGELOG.md` and prepares one release candidate.
The workflow uses GitHub's built-in token and the existing npm trusted publisher.

## Version rules

| Unreleased changes                           | Bump                                |
| -------------------------------------------- | ----------------------------------- |
| Breaking or removed API                      | Major from 1.0.0. Minor during 0.x. |
| Added or deprecated features                 | Minor                               |
| Compatible changes, fixes, or security fixes | Patch                               |
| No entries and no package changes            | Skip                                |

Use a `Breaking` section or start an entry with `**Breaking:**` and include
migration instructions. The largest applicable bump wins. Maintainers cut
the first 1.0.0 release through the manual workflow.

## Preview and rehearsal

Run `Weekly auto-release` with `preview` to see the decision and diff.
Set `AUTO_RELEASE_PAUSED` to `true` to pause automated writes.
In a test repository, set `AUTO_RELEASE_VALIDATE_ONLY` to `true` to run CI
and stop before advancing `main` or tagging. A manual `Unity Tests` run
with `unitypackage_smoke` also checks both editors.

Keep the `npm` environment restricted to version tags. The hosted release
job uses npm OIDC for provenance.

A maintainer with npm 2FA can configure the trusted publisher for an existing
package. A new trust entry expires unless a publish uses it within two days.

```bash
npx --yes npm@12.2.2 trust github com.wallstop-studios.dxaudio \
  --repo Ambiguous-Interactive/DxAudio --file release.yml --env npm --allow-publish
```

## Validation and recovery

The candidate gets a `release-verify/vX.Y.Z-<base SHA>` branch.
CI, Unity Tests, LLM Instructions Lint, and External documentation links
run on the candidate SHA. Unity checks both editors and both archive formats.

After validation, the workflow fast-forwards `main` to the checked commit.
The workflow verifies the Build and package safety and Unity CI Success
jobs on that SHA and reports their outcomes as GitHub Actions commit
statuses. GitHub enforces both statuses when the workflow updates `main`.
If `main` advances during CI, the workflow leaves that candidate untagged.
The next weekly run or manual `prepare` starts from the new base.

The workflow tags the validated commit and dispatches `Release` on the tag.
Release stages the UPM tarball, Unity import archive, and their SHA-256 files
on a draft. It verifies the tarball on npm before publishing the draft.
Stable versions use `latest`. Prereleases use `next`.

Use `resume` to retry failed validation or finish a pending publication.
The next weekly run also attempts recovery before preparing a new version.
Check run summaries for the exact commit, the validation run links, and
the archive digests. If a tag or digest disagrees, resolve the conflict
before retrying. A published version can be corrected with a new release.

## Manual release

Merge the manifest version, context version, and dated changelog section
to `main`. Validate that commit, then tag that exact commit.

```bash
git tag vX.Y.Z <validated-commit>
git push origin vX.Y.Z
```

Re-run Release or dispatch `gh workflow run release.yml --ref vX.Y.Z` to
finish a partial release. A package name that does not exist on npm needs a
maintainer to publish its first tarball with npm 2FA and configure trust.

## Secrets

<!-- release-secrets:begin -->

No custom secrets are required.

<!-- release-secrets:end -->

GitHub supplies the workflow token and npm OIDC.
