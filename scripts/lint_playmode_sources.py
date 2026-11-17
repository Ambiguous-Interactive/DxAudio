#!/usr/bin/env python3
"""Keep an asset import or a file write out of PlayMode test code (issue #53).

The editor died on a fatal abort during one PlayMode run, and this repository has
lost the editor twice more while it imported assets. An import or a write from
inside play mode runs a synchronous asset refresh in the running player loop,
which is the one place an asset change must never happen. A PlayMode suite
therefore carries no asset mutation at all: fixtures are authored before play
mode starts, or a player build measures the running player.

The scan covers the ``Tests/PlayMode`` tree, which holds every PlayMode test
assembly of a package that has no runtime scene of its own. ``Tests/`` outside
that folder stays out of scope, because an EditMode fixture owns its imports.

Launch with ``uv run scripts/lint_playmode_sources.py``; any Python 3.9+
interpreter works. ``--verbose`` prints the scanned count when the rule holds.
Exit codes: 0 = no PlayMode source mutates assets, 1 = a violation.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path
from typing import List, Optional, Sequence, Tuple

sys.dont_write_bytecode = True

REPO_ROOT = Path(__file__).resolve().parent.parent
PLAYMODE_ROOT = Path("Tests") / "PlayMode"
SKIPPED_DIRECTORY_NAMES = frozenset(("bin", "obj", "__pycache__"))
# An identifier that mutates the asset database, or a file write that lands in it.
FORBIDDEN_IDENTIFIERS = (
    "AssetDatabase",
    "AssetImporter",
    "SaveAndReimport",
    "ImportAsset",
    "File.Create",
    "File.WriteAll",
    "File.AppendAll",
    "File.OpenWrite",
)
FORBIDDEN_PATTERN = re.compile(
    r"\b(?:" + "|".join(re.escape(identifier) for identifier in FORBIDDEN_IDENTIFIERS) + r")",
)


def display_path(path: Path, repo_root: Path) -> str:
    """Show a checked path as a repository-relative, forward-slash path."""
    return str(path)[len(str(repo_root)) :].replace(os.sep, "/").lstrip("/")


def playmode_sources(repo_root: Path) -> List[Path]:
    """Return every C# source under the PlayMode test tree, sorted."""
    root = repo_root / PLAYMODE_ROOT
    if not root.is_dir():
        return []
    sources: List[Path] = []
    for directory, subdirectories, names in os.walk(root):
        subdirectories[:] = sorted(name for name in subdirectories if name not in SKIPPED_DIRECTORY_NAMES)
        for name in names:
            if name.endswith(".cs"):
                sources.append(Path(directory) / name)
    return sorted(sources, key=str)


def find_violations(repo_root: Path) -> Tuple[List[str], int]:
    """Return the violations and the scanned source count."""
    sources = playmode_sources(repo_root)
    violations: List[str] = []
    for path in sources:
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError as error:
            violations.append(f"{display_path(path, repo_root)} cannot be read: {error}")
            continue
        for number, line in enumerate(text.splitlines(), start=1):
            if line.lstrip().startswith("//"):
                continue
            match = FORBIDDEN_PATTERN.search(line)
            if match is not None:
                violations.append(
                    f"{display_path(path, repo_root)}:{number} uses {match.group(0)} in a PlayMode test; "
                    "an asset change inside play mode runs a refresh in the player loop. "
                    "Author the fixture before play mode, or measure in a player build"
                )
    return violations, len(sources)


def parse_arguments(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="lint-playmode-sources",
        description="Fail when a PlayMode test mutates assets or writes files.",
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=REPO_ROOT,
        help="repository root to scan (default: the checkout holding this script)",
    )
    parser.add_argument("--verbose", action="store_true", help="print the scanned count when the rule holds")
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = parse_arguments(argv)
    repo_root = Path(os.path.abspath(options.repo_root))
    if not repo_root.is_dir():
        print(f"[playmode-sources] error: repository root does not exist: {repo_root}")
        return 1

    violations, scanned = find_violations(repo_root)
    if violations:
        for violation in violations:
            print(f"[playmode-sources] error: {violation}")
        print(f"[playmode-sources] failed: {len(violations)} PlayMode asset mutation(s)")
        return 1

    if options.verbose:
        print(f"[playmode-sources] ok: {scanned} PlayMode source file(s) mutate no asset")
    return 0


if __name__ == "__main__":
    sys.exit(main())
