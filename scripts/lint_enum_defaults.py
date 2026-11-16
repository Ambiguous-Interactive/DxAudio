#!/usr/bin/env python3
"""Every enum's default member is an obsolete ``Unknown = 0``.

A serializer writes 0 into a field that never carried a value, so a zero
member that is also a real choice cannot tell "unset" from "chosen". Each
enum in ``Runtime/`` and ``Editor/`` therefore starts with ``Unknown = 0``
marked ``[Obsolete]``: naming it is a compile error, while the zero survives
as the unset sentinel. ``MusicDuckHolders``, ``MusicHandOffKind``,
``SfxDropReason`` and ``SfxPriority`` already read this way; ``MusicEase``
and ``MusicClipLoadType`` did not, so a missing value read as a chosen one.

The first declared member is the one checked: it holds the default a field
takes. '--paths' scans only the named C# files; the whole tree is the
default. '--verbose' prints the scanned enum count when clean.
``ENUM_DEFAULTS_ROOTS`` overrides the scan roots for the self-test. Exit
codes: 0 = clean, 1 = at least one enum breaks the convention, 2 = usage
error.
"""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path
from typing import List, Optional, Tuple

REPO_ROOT = Path(__file__).resolve().parent.parent

SCAN_ROOTS = (
    [root for root in os.environ["ENUM_DEFAULTS_ROOTS"].split(os.pathsep) if root]
    if os.environ.get("ENUM_DEFAULTS_ROOTS")
    else ["Runtime", "Editor"]
)

LINE_COMMENT = re.compile(r"//[^\n]*")
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.DOTALL)
ENUM_START = re.compile(r"\benum\s+(\w+)")
ATTRIBUTE = re.compile(r"^\s*\[")
MEMBER = re.compile(r"^\s*(\w+)\s*(?:=\s*(-?\d+))?\s*,?\s*$")


def mask(source: str) -> str:
    """Remove comments and string bodies so prose and messages cannot match."""
    source = BLOCK_COMMENT.sub(lambda match: "\n" * match.group(0).count("\n"), source)
    source = LINE_COMMENT.sub("", source)
    return re.sub(r'"(?:[^"\\\n]|\\.)*"', '""', source)


def enum_bodies(source: str) -> List[Tuple[str, int, List[str]]]:
    """Return each enum's name, its 1-based start line, and its body lines."""
    found: List[Tuple[str, int, List[str]]] = []
    for start in ENUM_START.finditer(source):
        open_index = source.find("{", start.end())
        if open_index < 0:
            continue
        depth = 0
        end_index = -1
        for index in range(open_index, len(source)):
            character = source[index]
            if character == "{":
                depth += 1
            elif character == "}":
                depth -= 1
                if depth == 0:
                    end_index = index
                    break
        if end_index < 0:
            continue
        body = [line for line in source[open_index + 1 : end_index].split("\n") if line.strip()]
        line_number = source.count("\n", 0, start.start()) + 1
        found.append((start.group(1), line_number, body))
    return found


def first_member(body: List[str]) -> Tuple[int, bool, Optional[re.Match]]:
    """Return the first member's body index, whether an attribute precedes it, and its match."""
    attributed = False
    for index, line in enumerate(body):
        if not line.strip():
            continue
        if ATTRIBUTE.match(line):
            attributed = True
            continue
        return index, attributed, MEMBER.match(line)
    return -1, attributed, None


def check(path: Path) -> List[str]:
    """Report every enum that does not start with an obsolete Unknown = 0."""
    try:
        source = path.read_text(encoding="utf-8-sig")
    except OSError:
        return [f"{path}: cannot read the file."]
    failures: List[str] = []
    for name, line, body in enum_bodies(mask(source)):
        index, attributed, member = first_member(body)
        where = f"{path}:{line}: enum '{name}'"
        if member is None:
            failures.append(f"{where} declares no members.")
            continue
        if member.group(1) != "Unknown":
            failures.append(f"{where} must declare 'Unknown = 0' first, not '{member.group(1)}'.")
            continue
        if member.group(2) != "0":
            failures.append(f"{where} must give Unknown the explicit value 0, not '{member.group(2)}'.")
            continue
        if not attributed:
            failures.append(f"{where} must mark Unknown [Obsolete] so naming it fails to compile.")
    return failures


def scan(paths: List[str]) -> Tuple[List[Path], int]:
    """Return the files to check and the enum count for the verbose line."""
    if paths:
        return [Path(path) for path in paths], 0
    files: List[Path] = []
    counted = 0
    for root in SCAN_ROOTS:
        for path in sorted((REPO_ROOT / root).rglob("*.cs")):
            files.append(path)
            counted += len(enum_bodies(mask(path.read_text(encoding="utf-8-sig"))))
    return files, counted


def main() -> int:
    arguments = sys.argv[1:]
    verbose = "--verbose" in arguments
    paths: List[str] = []
    if "--paths" in arguments:
        start = arguments.index("--paths") + 1
        paths = [value for value in arguments[start:] if not value.startswith("--")]
    elif arguments and arguments[0] not in ("--verbose",):
        print("usage: lint_enum_defaults.py [--paths FILE...] [--verbose]", file=sys.stderr)
        return 2

    files, counted = scan(paths)
    failures: List[str] = []
    for path in files:
        failures.extend(check(path))

    if failures:
        print("Every enum must start with an obsolete 'Unknown = 0' default member.", file=sys.stderr)
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        return 1

    if verbose:
        total = counted if not paths else sum(
            len(enum_bodies(mask(Path(path).read_text(encoding="utf-8-sig")))) for path in paths
        )
        print(f"Enum defaults in range: {total} enum(s) scanned.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:  # pragma: no cover - fail closed on any surprise
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
