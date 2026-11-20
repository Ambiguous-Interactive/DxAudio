# Releasing

The weekly workflow starts Sunday at 4 a.m. Pacific time. It reads the
`Unreleased` section in `CHANGELOG.md` and opens one release PR when changes
are ready. GitHub runs the required checks before merging the PR.

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

## Enable weekly publishing

1. Install a GitHub App on DxAudio with Contents and Pull requests read and
   write permissions. Enable squash merge and auto-merge. Keep both required
   status checks active.
2. Create the `auto-release` Actions environment for `main`. Set the App key
   listed below in that environment. Set `RELEASE_APP_ID` and
   `RELEASE_APP_SLUG` as repository Actions variables.
3. Keep the `npm` environment restricted to version tags. Set up npm trusted
   publishing if required. Authorize `release.yml` and the `npm` environment
   for this repository. The hosted release job uses npm OIDC for provenance.
4. Run `Weekly auto-release` with `preview` and review its decision and diff.
   Rehearse PR creation and CI in a test repository. Set
   `AUTO_RELEASE_VALIDATE_ONLY` to `true` there to stop before tagging.
   Verify the Unity Editor import on both supported versions. A manual
   `Unity Tests` run with `unitypackage_smoke` also checks both editors.
5. Set the repository variable `AUTO_RELEASE_ENABLED` to `true`. Remove the
   variable to pause automated writes.

A maintainer with npm 2FA can configure the trusted publisher for an existing
package. A new trust entry expires unless a publish uses it within two days.

```bash
npx --yes npm@12.2.2 trust github com.wallstop-studios.dxaudio \
  --repo Ambiguous-Interactive/DxAudio --file release.yml --env npm --allow-publish
```

## Validation and recovery

The merged release commit gets a `release-verify/vX.Y.Z` branch. CI, Unity
Tests, LLM Instructions Lint, and External documentation links run on that
commit. Unity checks both editors and validates both archive formats. OpenAI
Review runs on the PR as an advisory check.

After validation, the weekly workflow creates the tag and dispatches
`Release` on that tag. Release stages the UPM tarball, Unity import archive,
and their SHA-256 files on a draft. It verifies the tarball on npm before
publishing the draft. Stable versions use `latest`. Prereleases use `next`.

Use `prepare` to refresh a PR when its base advances. Use `resume` to check
the pending release, retry failed validation, or dispatch its tag again.
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

- `RELEASE_APP_PRIVATE_KEY` is the GitHub App key in the `auto-release`
  environment. The App is installed on DxAudio and can write Contents and
  Pull requests.

<!-- release-secrets:end -->

GitHub supplies the workflow token and npm OIDC.
