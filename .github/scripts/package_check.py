#!/usr/bin/env python3
"""Check what npm would publish, and pack the tarball a release publishes.

npm never takes a version back, so everything a bad release could ship is checked before it ships:

- the tarball holds the package only, not CI, the .NET test harness or editor settings;
- every asset and folder in it has its .meta, and every .meta its asset (a package installed from a
  registry is read-only, so Unity ignores an asset without one);
- every assembly an asmdef references is either local, supplied by Unity for tests, or guarded by the optional DxMessaging package.
  The runtime dependency map is empty;
- with --tag, the tag names package.json's version and CHANGELOG.md has a section for it.

    python3 .github/scripts/package_check.py                      # check the packed file list
    python3 .github/scripts/package_check.py --tag v1.2.3 --out dist  # also pack and write notes

With --out, it writes the tarball, its .sha256 and the release notes into that folder, and when
GITHUB_OUTPUT is set it records version, tarball and prerelease for later steps.
"""

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent.parent.parent
NAME = "com.wallstop-studios.dxaudio"
PACKAGE_DIRS = ("Runtime", "Editor", "Tests")
ROOT_FILES = ("package.json", "README.md", "CHANGELOG.md", "LICENSE.md")
# Lowercase prerelease names only: GitHub compares concurrency groups without case, so two tags that
# differ only in case would share the release workflow's group.
TAG = re.compile(r"^v(\d+\.\d+\.\d+(?:-[0-9a-z.-]+)?)$")
DXMESSAGING = "com.wallstop-studios.dxmessaging"
DXMESSAGING_ASSEMBLY = "WallstopStudios.DxMessaging"
DXMESSAGING_DEFINE = "DXMESSAGING_PRESENT"
TOOLING_SUFFIXES = frozenset({
    ".rsp", ".ruleset", ".editorconfig", ".globalconfig", ".dll", ".exe", ".pdb",
    ".so", ".dylib", ".bundle", ".a", ".aar", ".jar", ".nupkg",
})
# What a test assembly may use from com.unity.test-framework, which the project that runs the tests has.
TEST_ASSEMBLIES = {"UnityEngine.TestRunner", "UnityEditor.TestRunner"}
TEST_PRECOMPILED = {"nunit.framework.dll"}


def pack(destination: Path | None) -> dict:
    command = ["npm", "pack", "--json"]
    command += ["--pack-destination", str(destination)] if destination else ["--dry-run"]
    result = subprocess.run(command, cwd=ROOT, check=True, capture_output=True, text=True)
    packed = json.loads(result.stdout)
    # npm 10 and 11 print a list of packages, npm 12 an object keyed by package name.
    return packed[0] if isinstance(packed, list) else packed[NAME]


def unity_visible(path: str) -> bool:
    return not any(part.startswith(".") or part.endswith("~") for part in PurePosixPath(path).parts)


def tooling_problems(paths: set[str], *, unity_visible_only: bool = False) -> list[str]:
    problems = []
    for path in sorted(paths):
        if unity_visible_only and not unity_visible(path):
            continue
        asset = PurePosixPath(path.lower().removesuffix(".meta"))
        if any(
            PurePosixPath(part).suffix in TOOLING_SUFFIXES or part in TOOLING_SUFFIXES
            for part in asset.parts
        ):
            problems.append(f"development tooling in consumer content: {path}")
    return problems


def tracked_paths() -> set[str]:
    result = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT, check=True, capture_output=True)
    return {path for path in result.stdout.decode("utf-8").split("\0") if path}


def payload_problems(paths: set[str]) -> list[str]:
    allowed = set(ROOT_FILES) | {f"{name}.meta" for name in ROOT_FILES + PACKAGE_DIRS}
    stray = {path for path in paths if path not in allowed and PurePosixPath(path).parts[0] not in PACKAGE_DIRS}
    problems = [f"not package content: {path}" for path in sorted(stray)]
    problems += tooling_problems(paths)
    # Unity imports neither a dot path nor anything under a folder ending in ~.
    paths = {path for path in paths - stray if unity_visible(path)}
    folders = {str(parent) for path in paths for parent in PurePosixPath(path).parents if str(parent) != "."}
    assets = {path for path in paths if not path.endswith(".meta")} | folders
    for asset in sorted(assets):
        if f"{asset}.meta" not in paths:
            problems.append(f"no .meta in the tarball: {asset}")
    for meta in sorted(path for path in paths if path.endswith(".meta")):
        if meta.removesuffix(".meta") not in assets:
            problems.append(f"orphan .meta in the tarball: {meta}")
    for name in ROOT_FILES:
        if name not in paths:
            problems.append(f"missing from the tarball: {name}")
    return problems


def dxmessaging_guarded(asmdef: dict) -> bool:
    producers = [entry["name"] for entry in asmdef.get("versionDefines", []) if entry["define"] == DXMESSAGING_DEFINE]
    return producers == [DXMESSAGING] and DXMESSAGING_DEFINE in asmdef.get("defineConstraints", [])


def dependency_problems(manifest: dict, root: Path = ROOT) -> list[str]:
    problems = []
    if manifest.get("dependencies") != {}:
        problems.append("package.json must declare an empty runtime dependency map")
    paths = sorted(path for folder in PACKAGE_DIRS for path in (root / folder).rglob("*.asmdef"))
    asmdefs = {path: json.loads(path.read_text(encoding="utf-8")) for path in paths}
    own = {asmdef["name"]: asmdef for asmdef in asmdefs.values()}
    if len(own) != len(asmdefs):
        problems.append("assembly names must be unique")
    for path, asmdef in asmdefs.items():
        where = path.relative_to(root).as_posix()
        constraints = set(asmdef.get("defineConstraints", []))
        is_test = "UNITY_INCLUDE_TESTS" in constraints
        guarded = dxmessaging_guarded(asmdef)
        for reference in asmdef.get("references", []):
            if reference in own:
                if dxmessaging_guarded(own[reference]) and not guarded:
                    problems.append(f"{where} references optional assembly {reference} without its DxMessaging guard")
            elif is_test and reference in TEST_ASSEMBLIES:
                continue
            elif reference != DXMESSAGING_ASSEMBLY or not guarded:
                problems.append(f"{where} references external assembly {reference} without a supported guard")
        if asmdef.get("allowUnsafeCode", False):
            problems.append(f"{where} enables unsafe code")
        if not asmdef.get("overrideReferences", False):
            problems.append(f"{where} sets no overrideReferences, so it takes every auto-referenced DLL in the project")
        for dll in asmdef.get("precompiledReferences", []):
            if not (is_test and dll in TEST_PRECOMPILED):
                problems.append(f"{where} references external binary {dll}")
    return problems


def release_notes(version: str) -> str:
    changelog = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
    heading = re.search(rf"^## \[{re.escape(version)}\][^\n]*\n", changelog, re.MULTILINE)
    if not heading:
        return ""
    rest = changelog[heading.end() :]
    following = re.search(r"^## \[", rest, re.MULTILINE)
    return (rest[: following.start()] if following else rest).strip()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--tag", help="the release tag, vX.Y.Z")
    parser.add_argument("--out", type=Path, help="pack into this folder and write the release notes")
    args = parser.parse_args()
    if args.out:
        args.out = args.out.resolve()  # npm pack runs in ROOT, not in the caller's folder

    manifest = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))
    version = manifest["version"]
    problems = [] if manifest["name"] == NAME else [f"package.json names {manifest['name']}, not {NAME}"]
    problems += dependency_problems(manifest)

    notes = ""
    if args.tag is not None:
        match = TAG.fullmatch(args.tag)
        if not match:
            problems.append(f"tag {args.tag!r} is not vX.Y.Z")
        elif match.group(1) != version:
            problems.append(f"tag {args.tag} does not name package.json's version {version}")
        notes = release_notes(version)
        if not notes:
            problems.append(f"CHANGELOG.md has no '## [{version}]' section with notes")

    if args.out:
        args.out.mkdir(parents=True, exist_ok=True)
    packed = pack(args.out if args.out and not problems else None)
    paths = {entry["path"] for entry in packed["files"]}
    problems += payload_problems(paths)
    problems += tooling_problems(tracked_paths(), unity_visible_only=True)

    for problem in problems:
        print(f"::error::{problem}", file=sys.stderr)
    if problems:
        return 1
    dependencies = manifest.get("dependencies", {})
    listed = ", ".join(f"{name}@{v}" for name, v in sorted(dependencies.items())) or "none"
    print(f"{NAME}@{version}: {len(paths)} files, {packed['size']} bytes packed, dependencies: {listed}")

    if args.out:
        tarball = args.out / packed["filename"]
        digest = hashlib.sha256(tarball.read_bytes()).hexdigest()
        tarball.with_name(tarball.name + ".sha256").write_text(f"{digest}  {tarball.name}\n", encoding="utf-8")
        (args.out / "notes.md").write_text(notes + "\n", encoding="utf-8")
        prerelease = "-" in version
        outputs = {
            "version": version,
            "tarball": tarball.as_posix(),
            "prerelease": str(prerelease).lower(),
        }
        if "GITHUB_OUTPUT" in os.environ:
            with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
                output.writelines(f"{key}={value}\n" for key, value in outputs.items())
        print(json.dumps(outputs))
    return 0


if __name__ == "__main__":
    sys.exit(main())
