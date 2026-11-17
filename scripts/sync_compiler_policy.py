#!/usr/bin/env python3
"""Write the strict compiler policy beside every assembly definition (issue #40).

Unity feeds a ``csc.rsp`` and a ruleset beside an assembly definition into that
assembly's compilation, so a checked-in pair would ship the policy to every
consumer and let a ruleset silence the package's warnings without a reviewable
C# change. The policy is therefore generated instead: this script is its single
source, it writes the pair into each assembly directory before a local or CI
compile, and ``.gitignore`` keeps the result out of the repository and the npm
payload.

``--check`` reports a missing or weakened file without writing, and
``scripts/lint_assembly_warnings.py`` fails the ladder on the same report.

Launch with ``uv run scripts/sync_compiler_policy.py``; any Python 3.9+
interpreter works. Exit codes: 0 = the policy is complete, 1 = a violation.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path
from typing import List, Optional, Sequence, Tuple

sys.dont_write_bytecode = True

REPO_ROOT = Path(__file__).resolve().parent.parent

# A response file may carry one switch: the maximum warning level. The ruleset
# already promotes every warning, and a second suppression switch here would
# give one policy two sources of truth.
POLICY_RESPONSE_FILE = "csc.rsp"
POLICY_RESPONSE_CONTENT = "-warn:5\n"
POLICY_RULESET_FILE = "WarningsAsErrors.ruleset"
POLICY_RULESET_CONTENT = (
    '<?xml version="1.0" encoding="utf-8"?>\n'
    '<RuleSet Name="Warnings as errors" Description="Treat every compiler and analyzer warning as an error."'
    ' ToolsVersion="10.0">\n'
    '  <IncludeAll Action="Error" />\n'
    "</RuleSet>\n"
)
GENERATED_POLICY: Tuple[Tuple[str, str], ...] = (
    (POLICY_RESPONSE_FILE, POLICY_RESPONSE_CONTENT),
    (POLICY_RULESET_FILE, POLICY_RULESET_CONTENT),
)

# Tool caches and build output hold no assembly, and Unity ignores a folder
# whose name ends in ``~``.
SKIPPED_DIRECTORY_NAMES = frozenset((".git", ".venv", "bin", "obj", "node_modules", "__pycache__"))


def display_path(path: Path, repo_root: Path) -> str:
    """Show a path as a repository-relative, forward-slash path."""
    return str(path)[len(str(repo_root)) :].replace(os.sep, "/").lstrip("/")


def assembly_directories(repo_root: Path) -> List[Path]:
    """Return every directory that holds an assembly definition, sorted."""
    directories: List[Path] = []
    for directory, subdirectories, names in os.walk(repo_root):
        subdirectories[:] = sorted(
            name for name in subdirectories if name not in SKIPPED_DIRECTORY_NAMES and not name.endswith("~")
        )
        for name in names:
            if name.lower().endswith(".asmdef"):
                directories.append(Path(directory))
                break
    return sorted(directories, key=str)


def stale_policy(repo_root: Path) -> List[str]:
    """Return one message per generated file that is missing or differs."""
    problems: List[str] = []
    for directory in assembly_directories(repo_root):
        for name, content in GENERATED_POLICY:
            path = directory / name
            try:
                present = path.read_bytes()
            except OSError:
                problems.append(f"{display_path(path, repo_root)} is missing; run scripts/sync_compiler_policy.py")
                continue
            if present != content.encode("utf-8"):
                problems.append(
                    f"{display_path(path, repo_root)} does not carry the generated policy; "
                    "run scripts/sync_compiler_policy.py"
                )
    return problems


def write_policy(repo_root: Path) -> List[Path]:
    """Write the policy into every assembly directory and return the changed files."""
    changed: List[Path] = []
    for directory in assembly_directories(repo_root):
        for name, content in GENERATED_POLICY:
            path = directory / name
            try:
                if path.read_bytes() == content.encode("utf-8"):
                    continue
            except OSError:
                pass
            path.write_bytes(content.encode("utf-8"))
            changed.append(path)
    return changed


def parse_arguments(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="sync-compiler-policy",
        description="Write the strict warnings-as-errors policy beside every assembly definition.",
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=REPO_ROOT,
        help="repository root to update (default: the checkout holding this script)",
    )
    parser.add_argument("--check", action="store_true", help="report a missing or weakened policy without writing")
    parser.add_argument("--verbose", action="store_true", help="print the assembly count when the policy holds")
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = parse_arguments(argv)
    repo_root = Path(os.path.abspath(options.repo_root))
    if not repo_root.is_dir():
        print(f"[compiler-policy] error: repository root does not exist: {repo_root}")
        return 1

    assemblies = assembly_directories(repo_root)
    if not assemblies:
        print(f"[compiler-policy] error: {display_path(repo_root, repo_root) or '.'} holds no assembly definition")
        return 1

    if options.check:
        problems = stale_policy(repo_root)
        for problem in problems:
            print(f"[compiler-policy] error: {problem}")
        if problems:
            print(f"[compiler-policy] failed: {len(problems)} generated policy file(s) are missing or weakened")
            return 1
    else:
        changed = write_policy(repo_root)
        if options.verbose:
            print(f"[compiler-policy] wrote {len(changed)} file(s) across {len(assemblies)} assembly folder(s)")

    if options.verbose:
        print(f"[compiler-policy] ok: {len(assemblies)} assembly folder(s) carry the strict policy")
    return 0


if __name__ == "__main__":
    sys.exit(main())
