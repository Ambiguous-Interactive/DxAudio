---
name: create-unity-meta
description: Generate correct Unity .meta files for new files and folders in the DxAudio package, including which paths Unity ignores and which importer blocks to use. Use whenever creating, moving, renaming, or deleting any visible file or folder.
metadata:
  category: Core
---

# Create Unity Meta

Unity tracks every visible asset through a sibling `.meta` file with a stable GUID. Missing or regenerated metas churn diffs and break references.

## Rules

1. **Visible file or folder created:** add a sibling `.meta` with a fresh lowercase 32-hex GUID in the same change. Copy the importer block from the nearest matching asset.
2. **Dot paths are ignored by Unity:** `.llm/`, `.github/`, `.config/`, `.git/`, `.editorconfig`, `.gitignore`, `.pre-commit-config.yaml`, `.prettierrc.json`, `.env.local.example`. Never
   create `.meta` files for them.
3. **Never commit a Unity-generated stub.** Author the `.meta` beside the asset from the first commit.
4. **Move or rename:** move the `.meta` unchanged; the GUID travels with the file.
5. **Delete:** delete the `.meta` in the same change.
6. Unity also ignores any path segment ending in `~`. This package uses no `~` folders.
7. The compiler-policy files are generated locally by `scripts/sync_compiler_policy.py` and gitignored, so `csc.rsp` and `WarningsAsErrors.ruleset` never carry a committed `.meta`.

## Folder meta

```yaml
fileFormatVersion: 2
guid: <32-hex-lowercase>
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
```

## Text asset meta

```yaml
fileFormatVersion: 2
guid: <32-hex-lowercase>
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
```

## Verification

`npm run lint:meta-companions` checks tracked files only:

1. Every tracked `.meta` has a tracked target.
2. Every Unity-visible tracked file or directory has a tracked `.meta`.
3. Every `.meta` declares exactly one 32-hex `guid:`, unique across the package.

The lint and its contract test live in `scripts/lint_meta_companions.py` and `scripts/tests/test_meta_companions.py`.
