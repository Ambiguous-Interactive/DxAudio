#!/usr/bin/env python3
"""Check what npm would publish, and pack the tarball a release publishes.

npm never takes a version back, so everything a bad release could ship is checked before it ships:

- the tarball holds the package only, not CI, the .NET test harness or editor settings;
- every asset and folder in it has its .meta, and every .meta its asset (a package installed from a
  registry is read-only, so Unity ignores an asset without one);
- every assembly an asmdef references is either local, supplied by Unity for tests, or guarded by the optional DxMessaging package.
  The runtime dependency map is empty;
- with --tag, the tag names package.json's version and CHANGELOG.md has a section for it.

    python3 .github/scripts/package_check.py                      # check the actual npm archive
    python3 .github/scripts/package_check.py --tag v1.2.3 --out dist  # also pack and write notes

With --out, it writes the tarball, its .sha256 and the release notes into that folder, and when
GITHUB_OUTPUT is set it records version, tarball and prerelease for later steps.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
from pathlib import Path, PurePosixPath
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parent.parent.parent
NAME = "com.wallstop-studios.dxaudio"
PACKAGE_DIRS = ("Runtime", "Editor", "Tests")
ROOT_FILES = ("package.json", "README.md", "CHANGELOG.md", "LICENSE.md")
# Lowercase prerelease names only: GitHub compares concurrency groups without case, so two tags that
# differ only in case would share the release workflow's group.
NUMERIC_IDENTIFIER = r"(?:0|[1-9][0-9]*)"
PRERELEASE_IDENTIFIER = rf"(?:{NUMERIC_IDENTIFIER}|[0-9a-z-]*[a-z-][0-9a-z-]*)"
TAG = re.compile(rf"v{NUMERIC_IDENTIFIER}\.{NUMERIC_IDENTIFIER}\.{NUMERIC_IDENTIFIER}(?:-{PRERELEASE_IDENTIFIER}(?:\.{PRERELEASE_IDENTIFIER})*)?")
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


def pack(destination: Path) -> dict:
    npm = shutil.which("npm.cmd" if os.name == "nt" else "npm")
    if npm is None:
        raise FileNotFoundError("npm is required to verify the consumer archive")
    command = [npm, "pack", "--json", "--ignore-scripts", "--pack-destination", str(destination)]
    result = subprocess.run(command, cwd=ROOT, check=True, capture_output=True, text=True)
    packed = json.loads(result.stdout)
    # npm 10 and 11 print a list of packages, npm 12 an object keyed by package name.
    return packed[0] if isinstance(packed, list) else packed[NAME]


WINDOWS_RESERVED = re.compile(r"(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³]|CONIN\$|CONOUT\$)", re.IGNORECASE)


def nonlocal_part(part: str) -> bool:
    return (
        part in {"", ".", ".."}
        or part.endswith((" ", "."))
        or re.search(r'[<>:"|?*\\\x00-\x1f]', part) is not None
        or WINDOWS_RESERVED.fullmatch(part.partition(".")[0].rstrip()) is not None
    )


def archive_paths(tarball: Path) -> tuple[set[str], list[str]]:
    files: set[str] = set()
    directories: set[str] = set()
    seen: set[str] = set()
    problems: list[str] = []
    with tarfile.open(tarball, "r:gz") as archive:
        for entry in archive:
            name = entry.name.rstrip("/")
            parts = name.split("/")
            if parts[0] != "package" or any(nonlocal_part(part) for part in parts):
                problems.append(f"nonlocal npm archive member: {entry.name}")
                continue
            relative = "/".join(parts[1:])
            if name.casefold() in seen:
                problems.append(f"duplicate npm archive member: {entry.name}")
                continue
            seen.add(name.casefold())
            if entry.isdir():
                directories.add(relative)
            elif len(parts) > 1 and entry.isfile():
                files.add(relative)
            else:
                problems.append(f"unsupported npm archive member: {entry.name}")
    file_names = {name.casefold() for name in files}
    for name in files | directories:
        if any(str(parent).casefold() in file_names for parent in PurePosixPath(name).parents):
            problems.append(f"npm archive member is nested under a file: {name}")
    if not files:
        problems.append("npm archive contains no files")
    return files, problems


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


def git_package_problems() -> list[str]:
    result = subprocess.run(["git", "ls-files", "--stage", "-z"], cwd=ROOT, check=True, capture_output=True)
    paths = set()
    problems = []
    for record in result.stdout.decode("utf-8").split("\0"):
        if not record:
            continue
        metadata, _, path = record.partition("\t")
        paths.add(path)
        if not unity_visible(path):
            continue
        if metadata.startswith(("120000 ", "160000 ")):
            problems.append(f"Unity-visible Git content contains a link or submodule: {path}")
        asset = PurePosixPath(path.removesuffix(".meta"))
        if asset.suffix.lower() in {".cs", ".asmdef", ".asmref"} and asset.parts[0] not in PACKAGE_DIRS:
            problems.append(f"Unity compilation input is outside the package assembly trees: {path}")
    return problems + tooling_problems(paths, unity_visible_only=True)


def assembly_paths(root: Path, pattern: str) -> list[Path]:
    return sorted(
        path
        for folder in PACKAGE_DIRS
        for path in (root / folder).rglob(pattern)
        if unity_visible(path.relative_to(root).as_posix())
    )


def payload_problems(paths: set[str], root: Path = ROOT) -> list[str]:
    allowed = set(ROOT_FILES) | {f"{name}.meta" for name in ROOT_FILES + PACKAGE_DIRS}
    stray = {path for path in paths if path not in allowed and PurePosixPath(path).parts[0] not in PACKAGE_DIRS}
    problems = [f"not package content: {path}" for path in sorted(stray)]
    problems += tooling_problems(paths)
    shipped_assemblies = {path.relative_to(root).as_posix() for path in assembly_paths(root, "*.asmdef")}
    if not any(path.startswith("Runtime/") for path in shipped_assemblies):
        problems.append("the package must have a Runtime assembly")
    for path in sorted(shipped_assemblies - paths):
        problems.append(f"assembly definition missing from the archive: {path}")
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
    for key in ("optionalDependencies", "peerDependencies"):
        if manifest.get(key, {}) != {}:
            problems.append(f"package.json must keep {key} empty")
    for key in ("bundleDependencies", "bundledDependencies"):
        if manifest.get(key, []) != [] and manifest[key] is not False:
            problems.append(f"package.json must keep {key} empty")
    scripts = manifest.get("scripts", {})
    if not isinstance(scripts, dict):
        problems.append("package.json scripts must be an object")
    else:
        for name in ("preinstall", "install", "postinstall", "prepare"):
            if name in scripts:
                problems.append(f"package.json must not run {name} in consumer installs")
    for path in assembly_paths(root, "*.asmref"):
        relative = path.relative_to(root).as_posix()
        problems.append(f"{relative} bypasses the checked assembly definition graph")
    paths = assembly_paths(root, "*.asmdef")
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
                target = own[reference]
                if dxmessaging_guarded(target) and not guarded:
                    problems.append(f"{where} references optional assembly {reference} without its DxMessaging guard")
                if "UNITY_INCLUDE_TESTS" in target.get("defineConstraints", []) and not is_test:
                    problems.append(f"{where} references test-only assembly {reference}")
                if target.get("includePlatforms") == ["Editor"] and asmdef.get("includePlatforms") != ["Editor"]:
                    problems.append(f"{where} references Editor-only assembly {reference}")
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


def manifest_object(pairs: list[tuple[str, object]]) -> dict:
    fields = {}
    for name, value in pairs:
        if name in fields:
            raise ValueError(f"package.json repeats {name!r}")
        fields[name] = value
    return fields


def version_problems(version: object, tag: str | None) -> list[str]:
    if not isinstance(version, str) or TAG.fullmatch(f"v{version}") is None:
        return ["package.json version must be a complete release version"]
    if tag is None or tag == f"v{version}":
        return []
    if TAG.fullmatch(tag) is None:
        return [f"tag {tag!r} is not vX.Y.Z"]
    return [f"tag {tag!r} does not name package.json's version {version}"]


def changelog_problems(url: object, version: str, changelog: str) -> list[str]:
    problems = []
    try:
        parsed = urlsplit(url) if isinstance(url, str) else None
    except ValueError:
        parsed = None
    if (
        parsed is None
        or parsed.scheme != "https"
        or parsed.hostname in (None, "raw.githubusercontent.com")
        or "raw" in PurePosixPath(parsed.path).parts
        or PurePosixPath(parsed.path).stem.casefold() != "changelog"
    ):
        problems.append("package.json changelogUrl must point to a rendered HTTPS changelog")
    if not changelog.startswith("# ") or not re.search(rf"(?m)^## \[(?:Unreleased|{re.escape(version)})\](?: -[^\r\n]*)?$", changelog):
        problems.append("CHANGELOG.md must have a title and an Unreleased or current version heading")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--tag", help="the release tag, vX.Y.Z")
    parser.add_argument("--out", type=Path, help="pack into this folder and write the release notes")
    args = parser.parse_args()
    if args.out:
        args.out = args.out.resolve()  # npm pack runs in ROOT, not in the caller's folder

    try:
        manifest = json.loads((ROOT / "package.json").read_text(encoding="utf-8"), object_pairs_hook=manifest_object)
    except (OSError, ValueError) as error:
        print(f"::error::cannot read package.json: {error}", file=sys.stderr)
        return 1
    if not isinstance(manifest, dict):
        print("::error::package.json must contain a JSON object", file=sys.stderr)
        return 1
    version = manifest.get("version")
    problems = [] if manifest.get("name") == NAME else [f"package.json must name {NAME}"]
    problems += version_problems(version, args.tag)
    problems += dependency_problems(manifest)

    notes = ""
    if not problems:
        problems += changelog_problems(manifest.get("changelogUrl"), version, (ROOT / "CHANGELOG.md").read_text(encoding="utf-8"))
    if args.tag is not None and not problems:
        notes = release_notes(version)
        if not notes:
            problems.append(f"CHANGELOG.md has no '## [{version}]' section with notes")
    if problems:
        for problem in problems:
            print(f"::error::{problem}", file=sys.stderr)
        return 1

    with tempfile.TemporaryDirectory(prefix="dxaudio-package-") as directory:
        scratch = Path(directory)
        packed = pack(scratch)
        filename = f"{NAME}-{version}.tgz"
        if packed["filename"] != filename:
            raise ValueError(f"npm did not return the expected archive name: {filename}")
        tarball = scratch / filename
        paths, archive_problems = archive_paths(tarball)
        problems += archive_problems
        if paths != {entry["path"] for entry in packed["files"]}:
            problems.append("npm pack metadata disagrees with the actual archive")
        problems += payload_problems(paths)
        problems += git_package_problems()

        for problem in problems:
            print(f"::error::{problem}", file=sys.stderr)
        if problems:
            return 1
        if args.out:
            args.out.mkdir(parents=True, exist_ok=True)
            output_archive = args.out / filename
            output_archive.write_bytes(tarball.read_bytes())
    dependencies = manifest.get("dependencies", {})
    listed = ", ".join(f"{name}@{v}" for name, v in sorted(dependencies.items())) or "none"
    print(f"{NAME}@{version}: {len(paths)} files, {packed['size']} bytes packed, dependencies: {listed}")

    if args.out:
        tarball = output_archive
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
