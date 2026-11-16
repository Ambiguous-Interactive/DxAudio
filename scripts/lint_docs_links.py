#!/usr/bin/env python3
"""Fail when a documentation link or a sample scene reference does not resolve.

The package ships two ignored trees: ``Documentation~`` (the manual) and
``Samples~`` (importable example content). Unity never imports them, so a
renamed page or a deleted script breaks nothing at compile time; the reader
only finds out when a link leads nowhere or an imported scene loses the
script on a GameObject. This lint closes both gaps:

- Relative links in ``Documentation~``, ``Samples~`` and ``README.md`` must
  point at a path the repository commits. Membership comes from
  ``git ls-files``, so a link to an ignored or untracked scratch file fails
  here even though it resolves on the author's machine; without a usable
  git, the working tree stands in for the committed one. A link with a
  fragment into a Markdown file must also match a heading there, using
  GitHub's anchor rules, so ``[Clip lifetime](../README.md#clip-lifetime)``
  fails when that section is renamed. External URLs and site-absolute paths
  are not fetched; they belong to the network, not to this checkout.
- Every GUID a sample scene references must match a committed ``.meta``, so
  the scene's scripts survive the sample import, which keeps those GUIDs.
  Unity's built-in resources (a GUID whose first 16 digits are zero) are
  allowed because no ``.meta`` in the repository can name them.

Links inside fenced code are examples, not references, and are skipped.

Launch with ``uv run scripts/lint_docs_links.py``; any Python 3.9+
interpreter works. Exit codes: 0 = every reference resolves, 1 = at least
one does not, 2 = the arguments are wrong.
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Set, Tuple

REPO_ROOT = Path(__file__).resolve().parent.parent
DOCUMENTATION_DIRECTORY = "Documentation~"
SAMPLES_DIRECTORY = "Samples~"
ROOT_MARKDOWN = ("README.md",)

INLINE_LINK = re.compile(r"\[[^\[\]]*\]\(\s*([^()\s]+)(?:\s+\"[^\"]*\")?\s*\)")
HEADING = re.compile(r"^(#{1,6})\s+(.*?)\s*#*\s*$")
FENCE = re.compile(r"^\s*(?:```|~~~)")
SCENE_GUID = re.compile(r"guid: ([0-9a-fA-F]{32})")
META_GUID = re.compile(r"^guid: ([0-9a-fA-F]{32})\s*$", re.MULTILINE)
EXTERNAL_SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.\-]*:")
# Unity's built-in resources live outside every package, so no .meta can name them.
BUILT_IN_GUID_PREFIX = "0" * 16


def tracked_paths(root: Path) -> Optional[Set[str]]:
    """Return the repository's committed paths, or None when git cannot answer.

    ``None`` keeps the working-tree behavior: on a host without git the lint
    still catches a missing file, and a stale checkout cannot fail a link
    that git would accept.
    """
    try:
        completed = subprocess.run(
            ["git", "-C", str(root), "ls-files", "-z"],
            capture_output=True,
            check=False,
        )
    except OSError:
        return None
    if completed.returncode != 0:
        return None
    return {
        entry.decode("utf-8", errors="replace").replace("\\", "/")
        for entry in completed.stdout.split(b"\0")
        if entry
    }


def display(path: Path, root: Path) -> str:
    """Show a path relative to the scanned root with forward slashes."""
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return str(path)


def markdown_files(root: Path) -> List[Path]:
    """List the Markdown the manual and samples own, in stable order."""
    files = [root / name for name in ROOT_MARKDOWN if (root / name).is_file()]
    for directory in (DOCUMENTATION_DIRECTORY, SAMPLES_DIRECTORY):
        base = root / directory
        if base.is_dir():
            files.extend(base.rglob("*.md"))
    return sorted({path.resolve() for path in files if path.is_file()})


def scene_files(root: Path) -> List[Path]:
    """List the committed sample scenes."""
    base = root / SAMPLES_DIRECTORY
    return sorted(base.rglob("*.unity")) if base.is_dir() else []


def prose_lines(lines: Iterable[str]) -> List[str]:
    """Drop fenced code so an example link is never checked as a reference."""
    kept: List[str] = []
    fenced = False
    for line in lines:
        if FENCE.match(line):
            fenced = not fenced
            continue
        if not fenced:
            kept.append(line)
    return kept


def slug(text: str) -> str:
    """Turn a heading into GitHub's anchor form: lower case, punctuation dropped."""
    lowered = text.strip().lower()
    without_markup = re.sub(r"[`*_~]", "", lowered)
    without_punctuation = re.sub(r"[^\w\- ]", "", without_markup)
    return without_punctuation.replace(" ", "-")


def anchors(text: str) -> Set[str]:
    """Collect every anchor a Markdown file declares, duplicates numbered as GitHub does."""
    found: Set[str] = set()
    seen: Dict[str, int] = {}
    for line in prose_lines(text.splitlines()):
        match = HEADING.match(line)
        if match is None:
            continue
        base = slug(match.group(2))
        count = seen.get(base, 0)
        seen[base] = count + 1
        found.add(base if count == 0 else f"{base}-{count}")
    return found


def is_committed(destination: Path, root: Path, tracked: Optional[Set[str]]) -> bool:
    """Return whether a resolved path is a committed file (or holds committed files)."""
    if tracked is None:
        return True
    try:
        relative = destination.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return True  # outside the repository, so git cannot speak for it
    if relative in tracked:
        return True
    # A link to a folder resolves to the files under it, which are the entries git knows.
    return destination.is_dir() and any(path.startswith(relative + "/") for path in tracked)


def check_link(
    source: Path,
    target: str,
    root: Path,
    anchor_cache: Dict[Path, Set[str]],
    tracked: Optional[Set[str]],
) -> Optional[str]:
    """Return a failure message for one link, or None when it resolves."""
    if not target or EXTERNAL_SCHEME.match(target) or target.startswith(("//", "/")):
        return None
    path_text, _, fragment = target.partition("#")
    destination = (source.parent / path_text).resolve() if path_text else source.resolve()
    if not destination.exists():
        return f"'{target}' does not resolve: {display(destination, root)} is not committed"
    if not is_committed(destination, root, tracked):
        return f"'{target}' does not resolve: {display(destination, root)} is not committed"
    if fragment and destination.suffix.lower() == ".md" and destination.is_file():
        if destination not in anchor_cache:
            anchor_cache[destination] = anchors(destination.read_text(encoding="utf-8-sig", errors="replace"))
        if fragment not in anchor_cache[destination]:
            return f"'{target}' does not resolve: {display(destination, root)} has no '#{fragment}' heading"
    return None


def committed_guids(root: Path, tracked: Optional[Set[str]] = None) -> Set[str]:
    """Collect every asset GUID the repository commits."""
    if tracked is None:
        metas: Iterable[Path] = root.rglob("*.meta")
    else:
        metas = (root / path for path in sorted(tracked) if path.endswith(".meta"))
    guids: Set[str] = set()
    for meta in metas:
        try:
            text = meta.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        match = META_GUID.search(text)
        if match is not None:
            guids.add(match.group(1).lower())
    return guids


def check_scene(scene: Path, root: Path, guids: Set[str]) -> List[str]:
    """Return one failure per referenced GUID that no committed asset owns."""
    try:
        text = scene.read_text(encoding="utf-8-sig", errors="replace")
    except OSError as error:
        return [f"cannot read the scene: {error}"]
    missing: Set[str] = set()
    for guid in SCENE_GUID.findall(text):
        lowered = guid.lower()
        if lowered.startswith(BUILT_IN_GUID_PREFIX) or lowered in guids:
            continue
        missing.add(guid)
    return [
        f"references GUID {guid}, which no committed .meta owns; the imported scene loses that asset"
        for guid in sorted(missing)
    ]


def parse_arguments(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="lint-docs-links",
        description="Fail when a documentation link or a sample scene reference does not resolve.",
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=REPO_ROOT,
        help="repository root to scan (default: the checkout holding this script)",
    )
    parser.add_argument("--verbose", action="store_true", help="print every checked file")
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = parse_arguments(argv)
    root = options.repo_root.resolve()
    problems: List[str] = []
    checked_files = markdown_files(root)
    tracked = tracked_paths(root)
    anchor_cache: Dict[Path, Set[str]] = {}
    links = 0
    for source in checked_files:
        if options.verbose:
            print(f"[docs-links] checking {display(source, root)}")
        text = source.read_text(encoding="utf-8-sig", errors="replace")
        for line in prose_lines(text.splitlines()):
            for target in INLINE_LINK.findall(line):
                links += 1
                failure = check_link(source, target, root, anchor_cache, tracked)
                if failure is not None:
                    problems.append(f"{display(source, root)}: {failure}")

    guids = committed_guids(root, tracked)
    scenes = scene_files(root)
    for scene in scenes:
        for failure in check_scene(scene, root, guids):
            problems.append(f"{display(scene, root)}: {failure}")

    if problems:
        for problem in problems:
            print(f"::error::{problem}", file=sys.stderr)
        print(f"::error::documentation and sample references disagree in {len(problems)} place(s)")
        return 1

    print(
        f"[docs-links] ok: {links} link(s) in {len(checked_files)} file(s) resolve, "
        f"and {len(scenes)} sample scene(s) reference committed assets"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
