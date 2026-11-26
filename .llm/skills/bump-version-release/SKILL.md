---
name: bump-version-release
description: Bump the DxAudio package version and publish it to npm - changelog, context version sync, merge-to-main, version tag, trusted publishing, and dist-tag rules. Use when cutting any release.
metadata:
  category: Workflow
---

# Bump Version / Release

DxAudio publishes with npm trusted publishing through GitHub OIDC. There is no `NPM_TOKEN`.

## Steps

Weekly releases use the [releasing runbook](../../references/RELEASING.md).
Add user-facing notes under `CHANGELOG.md` `## [Unreleased]`. The section
describes changes since the last release; the baseline is the newest tag with
a published GitHub Release, not the newest tag alone. Fold fixes and changes
to an unreleased feature into its `Added` entry
([changelog](../../references/changelog.md)). Mark public API breaks and
include migration steps. A breaking change bumps the minor version during 0.x
and the major version from 1.0.0.

For a manual release, bump `package.json`, sync the context version with
`npm run lint:llm:fix`, and date the changelog section. Merge the version
commit into `main` and validate it before tagging.

```bash
git tag vX.Y.Z <validated-commit>
git push origin vX.Y.Z
```

Release stages both archive formats and their checksums, verifies the npm
publication, then publishes the GitHub Release. Re-run the tag workflow to
finish a partial release.

## npm trust

Before the first release of a new package, a maintainer with npm 2FA runs:

```bash
npx --yes npm@12.2.2 trust github com.wallstop-studios.dxaudio \
  --repo Ambiguous-Interactive/DxAudio --file release.yml --env npm --allow-publish
```

A new trust configuration expires unless a publish uses it within 2 days. Run it just before pushing the tag.

## Dist-tags

- Versions matching `-rc`, `-alpha`, `-beta`, or `-preview` (case-insensitive) publish under `next`.
- Everything else publishes under `latest`.

## Safety

- The publish step is re-runnable: it skips a name@version already on the registry.
- npm publish is irreversible. Never publish with uncommitted changes to shipped assets.
- Rollback is a patch bump; do not attempt to unpublish.

## Related

- [ship-changes](../ship-changes/SKILL.md) - the general validation ladder
