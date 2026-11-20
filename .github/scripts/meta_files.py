#!/usr/bin/env python3
"""Check, or write, the .meta file Unity needs beside every file and folder of the package.

A package installed from git is read-only, so Unity cannot write a missing .meta itself: it warns and
ignores the asset, and a script without one never compiles. An orphan .meta (its asset deleted) makes
Unity warn on every import. Both are easy to commit by accident, so CI runs this with --check.

New metas get a GUID derived from the path, so writing them twice gives the same result.

    python3 .github/scripts/meta_files.py          # write missing metas
    python3 .github/scripts/meta_files.py --check  # fail on a missing or orphan meta
"""

import hashlib
import sys
from pathlib import Path

sys.dont_write_bytecode = True

from package_check import unity_visible

ROOT = Path(__file__).resolve().parent.parent.parent
PACKAGE_DIRS = ("Runtime", "Editor", "Tests")
PACKAGE_FILES = ("package.json", "README.md", "CHANGELOG.md", "LICENSE.md")
# scripts/sync_compiler_policy.py writes these beside each assembly definition for local and CI compiles.
# They never ship, so they carry no committed .meta and CI must not demand one. Any other response file or
# ruleset is a stray that scripts/lint_assembly_warnings.py rejects.
GENERATED_POLICY_NAMES = ("csc.rsp", "WarningsAsErrors.ruleset")


def importer_for(path: Path) -> str:
    if path.is_dir():
        return "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    suffix = path.suffix.lower()
    if suffix == ".cs":
        return (
            "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
            "  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        )
    if suffix == ".asmdef":
        return "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    if path.name == "package.json":
        return "PackageManifestImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    return "TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def is_generated_policy(path: Path) -> bool:
    """Return whether a path is a compiler-policy file written by the sync script."""
    return path.name.removesuffix(".meta") in GENERATED_POLICY_NAMES


def assets() -> list[Path]:
    found = [ROOT / name for name in PACKAGE_FILES if (ROOT / name).exists()]
    for directory in PACKAGE_DIRS:
        top = ROOT / directory
        if not top.exists():
            continue
        found.append(top)
        found.extend(
            p
            for p in sorted(top.rglob("*"))
            if p.suffix != ".meta" and unity_visible(p.relative_to(ROOT).as_posix()) and not is_generated_policy(p)
        )
    return found


def meta_text(path: Path) -> str:
    relative = path.relative_to(ROOT).as_posix()
    guid = hashlib.md5(f"com.wallstop-studios.dxaudio/{relative}".encode()).hexdigest()
    return f"fileFormatVersion: 2\nguid: {guid}\n{importer_for(path)}"


def main() -> int:
    check = "--check" in sys.argv[1:]
    expected = assets()
    problems = []
    for asset in expected:
        meta = asset.with_name(asset.name + ".meta")
        if meta.exists():
            continue
        if check:
            problems.append(f"missing: {meta.relative_to(ROOT)}")
        else:
            with meta.open("w", encoding="utf-8", newline="\n") as output:
                output.write(meta_text(asset))
            print(f"wrote {meta.relative_to(ROOT)}")

    expected_metas = {asset.with_name(asset.name + ".meta") for asset in expected}
    for directory in PACKAGE_DIRS:
        for meta in (ROOT / directory).rglob("*.meta"):
            if (
                unity_visible(meta.relative_to(ROOT).as_posix())
                and meta not in expected_metas
                and not is_generated_policy(meta)
            ):
                problems.append(f"orphan: {meta.relative_to(ROOT)}")

    for problem in problems:
        print(problem, file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
