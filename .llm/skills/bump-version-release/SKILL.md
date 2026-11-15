---
name: bump-version-release
description: Bump the DxAudio package version and publish it to npm - changelog, context version sync, merge-to-main, version tag, trusted publishing, and dist-tag rules. Use when cutting any release.
metadata:
  category: Workflow
---

# Bump Version / Release

DxAudio publishes with npm trusted publishing through GitHub OIDC. There is no `NPM_TOKEN`.

## Steps

1. Add user-facing entries under `CHANGELOG.md` `## [Unreleased]`. Entries are verb-first, one or two sentences, at most 300 rendered characters.
2. Bump `package.json` `version`.
3. Sync the context version line: `npm run lint:llm:fix` rewrites the `**Version**:` line in `.llm/context.md`. Commit `package.json`, `.llm/context.md`, and the dated changelog
   together with subject `Bump version from X to Y`.
4. Merge to `main`. The tag must be an ancestor of `main`; the workflow enforces this.
5. Tag and push:

```bash
git tag vX.Y.Z origin/main
git push origin vX.Y.Z
```

6. The `Release` workflow tests the engine-free core, packs the tarball, creates the GitHub Release, and publishes the same tarball to npm. Every step skips work already done, so a
   failed run is finished by re-running it or by `gh workflow run release.yml --ref vX.Y.Z`.

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

- [ship-changes](./ship-changes/SKILL.md) - the general validation ladder
