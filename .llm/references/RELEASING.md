# Releasing

DxAudio releases through `.github/workflows/release.yml` on a version tag. Publishing uses npm trusted publishing (GitHub OIDC); no `NPM_TOKEN` exists.

## Preconditions

1. `package.json` version is the release version.
2. `CHANGELOG.md` has the dated section for that version.
3. The commit is on `main`.
4. The npm trust entry exists for this repository, workflow file, and `npm` environment.

## Trust setup

A maintainer with npm 2FA runs, for an existing package:

```bash
npx --yes npm@12.2.2 trust github com.wallstop-studios.dxaudio \
  --repo Ambiguous-Interactive/DxAudio --file release.yml --env npm --allow-publish
```

A trust entry expires unless a publish uses it within 2 days. Run it just before the tag.

## Tag and publish

```bash
git tag vX.Y.Z origin/main
git push origin vX.Y.Z
```

The workflow:

1. Verifies the tag is an ancestor of `main`.
2. Runs `dotnet test .net-tests`.
3. Packs the tarball and checks the npm payload.
4. Creates or updates the GitHub Release with the tarball and its `.sha256`.
5. Publishes the same tarball to npm.

Every step skips work already done. Re-run the workflow, or `gh workflow run release.yml --ref vX.Y.Z`, to finish a partial release.

## Dist-tags

- `-rc`, `-alpha`, `-beta`, `-preview` (case-insensitive) publish under `next`.
- Everything else publishes under `latest`.

## Recovery

| State                                     | Action                                                                                                        |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| GitHub Release exists, npm publish failed | Re-run the tag workflow; the release step skips, publish retries                                              |
| npm publish refused the OIDC exchange     | Check the trust entry, then re-run                                                                            |
| Package name is not on npm yet            | A maintainer publishes once with `npx npm@12.2.2 publish <tarball> --access public`, then trusts the workflow |
| Wrong version published                   | Publish a patch bump; do not unpublish                                                                        |

## Secrets

<!-- release-secrets:begin -->

No custom secrets are required.
<!-- release-secrets:end -->

The release workflow uses `GITHUB_TOKEN` for the Release and an OIDC token for npm. Any change that adds a secret must document it in this block and in the workflow.
