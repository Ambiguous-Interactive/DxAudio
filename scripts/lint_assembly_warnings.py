#!/usr/bin/env python3
"""Enforce the assembly warning policy (issue #37).

A compiler warning the package hides is a defect no CI lane can see, so the
package keeps every warning visible and fails the build where the repository
controls the compiler:

1. No compiler response file (``.rsp``) or ruleset (``.ruleset``) ships in the
   repository. Unity feeds a response file beside an assembly definition, and
   a ruleset through it, into that assembly's compilation, so either file can
   silence a warning for every consumer without a reviewable C# change.
2. No ``#pragma warning disable`` in the shipped ``Runtime/`` and ``Editor/``
   sources. Fix the warning or version-guard the API that raises it, per the
   unity-version-guards skill.
3. The engine-free ``.net-tests`` project keeps ``TreatWarningsAsErrors``, so
   the Core assembly fails on a warning on plain .NET before Unity sees it.

The ``Tests/`` tree stays out of rule 2: a Unity test fixture may pin one
editor's obsolete API and suppress the warning the other editor raises.
Unity itself compiles at the editor's warning level, so the warnings for the
assemblies this repository does not build stay in the CI job log.

The step runs in the fast check on assembly-configuration changes, in the
gate, and in CI's lint ladder, so all three drivers apply one policy.

Launch with ``uv run scripts/lint_assembly_warnings.py``; any Python 3.9+
interpreter works. ``--verbose`` prints the scanned counts when the policy
holds. Exit codes: 0 = policy holds, 1 = a violation or an unreadable input.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path
from typing import List, Optional, Sequence, Tuple

REPO_ROOT = Path(__file__).resolve().parent.parent
SHIPPED_SOURCE_ROOTS: Tuple[str, ...] = ("Runtime", "Editor")
FORBIDDEN_CONFIGURATION_SUFFIXES: Tuple[str, ...] = (".rsp", ".ruleset")
ENGINE_FREE_PROJECT = Path(".net-tests") / "DxAudio.Tests.csproj"
WARNING_DISABLE_PATTERN = re.compile(r"#\s*pragma\s+warning\s+disable\b", re.IGNORECASE)
WARNINGS_AS_ERRORS_PATTERN = re.compile(
    r"<TreatWarningsAsErrors>\s*true\s*</TreatWarningsAsErrors>",
    re.IGNORECASE,
)


def parse_arguments(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="lint-assembly-warnings",
        description="Fail when the package hides a compiler warning.",
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=REPO_ROOT,
        help="repository root to scan (default: the checkout holding this script)",
    )
    parser.add_argument("--verbose", action="store_true", help="print the scanned counts when the policy holds")
    return parser.parse_args(argv)


def display_path(path: Path, repo_root: Path) -> str:
    """Show a checked path as a repository-relative, forward-slash path."""
    return str(path)[len(str(repo_root)):].replace(os.sep, "/").lstrip("/")


def _visible_walk(repo_root: Path) -> Tuple[List[Path], List[Path]]:
    """Return (compiler configuration files, shipped C# sources) in one walk.

    Dot directories hold the tool configuration, the git metadata and the
    engine-free project, none of which Unity compiles, so the walk prunes
    them the way Unity ignores them.
    """
    configuration: List[Path] = []
    sources: List[Path] = []
    for directory, subdirectories, names in os.walk(repo_root):
        subdirectories[:] = sorted(name for name in subdirectories if not name.startswith("."))
        current = Path(directory)
        relative = display_path(current, repo_root)
        shipped = relative.split("/")[0] in SHIPPED_SOURCE_ROOTS if relative else False
        for name in names:
            path = current / name
            if name.endswith(FORBIDDEN_CONFIGURATION_SUFFIXES):
                configuration.append(path)
            elif shipped and name.endswith(".cs"):
                sources.append(path)
    return sorted(configuration, key=str), sorted(sources, key=str)


def find_policy_violations(repo_root: Path) -> Tuple[List[str], int, int]:
    """Return the policy violations, the configuration count and the source count."""
    configuration, sources = _visible_walk(repo_root)
    violations: List[str] = []

    for path in configuration:
        violations.append(
            f"{display_path(path, repo_root)} configures the package compiler; "
            "remove it, because a response file or ruleset can hide a warning"
        )

    for path in sources:
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError as error:
            violations.append(f"{display_path(path, repo_root)} cannot be read: {error}")
            continue
        for number, line in enumerate(text.splitlines(), start=1):
            if WARNING_DISABLE_PATTERN.search(line):
                violations.append(
                    f"{display_path(path, repo_root)}:{number} disables a compiler warning; "
                    "fix the warning or version-guard the API"
                )

    project = repo_root / ENGINE_FREE_PROJECT
    try:
        project_text = project.read_text(encoding="utf-8")
    except OSError:
        violations.append(f"{ENGINE_FREE_PROJECT.as_posix()} is missing; the engine-free build must fail on a warning")
    else:
        if not WARNINGS_AS_ERRORS_PATTERN.search(project_text):
            violations.append(
                f"{ENGINE_FREE_PROJECT.as_posix()} must set "
                "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>"
            )

    return violations, len(configuration), len(sources)


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = parse_arguments(argv)
    repo_root = Path(os.path.abspath(options.repo_root))
    if not repo_root.is_dir():
        print(f"[assembly-warnings] error: repository root does not exist: {repo_root}")
        return 1

    violations, configuration, sources = find_policy_violations(repo_root)
    if violations:
        for violation in violations:
            print(f"[assembly-warnings] error: {violation}")
        print(f"[assembly-warnings] failed: {len(violations)} assembly warning policy violation(s)")
        return 1

    if options.verbose:
        print(
            f"[assembly-warnings] ok: {configuration} compiler configuration file(s), "
            f"{sources} shipped source file(s), and the engine-free warnings-as-errors setting hold"
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
