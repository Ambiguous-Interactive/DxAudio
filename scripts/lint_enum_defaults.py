#!/usr/bin/env python3
"""Every enum's default member is an obsolete ``Unknown = 0``.

A serializer writes 0 into a field that never carried a value, so a zero
member that is also a real choice cannot tell "unset" from "chosen". Each
enum in ``Runtime/`` and ``Editor/`` therefore starts with ``Unknown = 0``
marked ``[Obsolete(..., true)]``: naming it fails the build, while the zero
survives as the unset sentinel. Every other member carries an explicit value,
so inserting one cannot silently renumber the rest.

``MusicDuckHolders``, ``MusicHandOffKind``, ``SfxDropReason`` and
``SfxPriority`` already read this way; ``MusicEase`` and ``MusicClipLoadType``
did not, so a missing value read as a chosen one.

Comments, character literals and every string shape (regular, verbatim, raw)
are masked before the scan, so prose or fixture text that spells an enum
cannot fail it. '--paths' scans only the named C# files; the whole tree is the
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

ENUM_START = re.compile(r"\benum\s+(\w+)")
ATTRIBUTE = re.compile(r"^\s*\[")
OBSOLETE = re.compile(r"\[\s*(?:System\s*\.\s*)?Obsolete\s*\(")
MEMBER = re.compile(r"^\s*(\w+)\s*(?:=\s*(-?\d+))?\s*,?\s*$")


def mask(source: str) -> str:
    """Blank comments, character literals and string bodies, keeping line numbers stable."""
    kept: List[str] = []
    index = 0
    length = len(source)
    while index < length:
        character = source[index]
        nxt = source[index + 1] if index + 1 < length else ""
        if character == "/" and nxt == "/":
            end = source.find("\n", index)
            index = length if end < 0 else end
            continue
        if character == "/" and nxt == "*":
            end = source.find("*/", index + 2)
            end = length if end < 0 else end + 2
            kept.append("".join("\n" if item == "\n" else " " for item in source[index:end]))
            index = end
            continue
        if character == "'":
            end = index + 1
            while end < length and source[end] != "'":
                end += 2 if source[end] == "\\" else 1
            kept.append(" " * min(end + 1 - index, length - index))
            index = end + 1
            continue
        if character == '"':
            verbatim = 0 < index and source[index - 1] == "@"
            raw = source.startswith('"""', index)
            end = index + (3 if raw else 1)
            while end < length:
                if raw and source.startswith('"""', end):
                    end += 3
                    break
                if not raw and verbatim and source.startswith('""', end):
                    end += 2
                    continue
                if not raw and not verbatim and source[end] == "\\":
                    end += 2
                    continue
                if not raw and source[end] == '"':
                    end += 1
                    break
                end += 1
            kept.append("".join("\n" if item == "\n" else " " for item in source[index:end]))
            index = end
            continue
        kept.append(character)
        index += 1
    return "".join(kept)


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


def members(body: List[str]) -> List[Tuple[List[str], Optional[re.Match]]]:
    """Pair each attribute block with the member that follows it."""
    paired: List[Tuple[List[str], Optional[re.Match]]] = []
    attributes: List[str] = []
    for line in body:
        if ATTRIBUTE.match(line):
            attributes.append(line)
            continue
        paired.append((attributes, MEMBER.match(line)))
        attributes = []
    return paired


def check(path: Path) -> List[str]:
    """Report every enum that breaks the sentinel or the explicit-value rule."""
    try:
        source = path.read_text(encoding="utf-8-sig")
    except OSError:
        return [f"{path}: cannot read the file."]
    failures: List[str] = []
    for name, line, body in enum_bodies(mask(source)):
        where = f"{path}:{line}: enum '{name}'"
        declared = members(body)
        if not declared:
            failures.append(f"{where} declares no members.")
            continue
        attributes, first = declared[0]
        if first is None:
            failures.append(f"{where} has a member this lint cannot read.")
            continue
        if first.group(1) != "Unknown":
            failures.append(f"{where} must declare 'Unknown = 0' first, not '{first.group(1)}'.")
        elif first.group(2) != "0":
            failures.append(f"{where} must give Unknown the explicit value 0, not '{first.group(2)}'.")
        elif not any(OBSOLETE.search(attribute) for attribute in attributes):
            failures.append(f"{where} must mark Unknown [Obsolete] so naming it fails the build.")
        for attributes_skipped, member in declared[1:]:
            if member is not None and member.group(2) is None:
                failures.append(
                    f"{where} must give every member an explicit value, but '{member.group(1)}' has none."
                )
    return failures


def scan(paths: List[str]) -> List[Path]:
    """Return the files to check; --paths keeps the per-edit loop to the diff."""
    if paths:
        return [Path(path) for path in paths if path.endswith(".cs")]
    files: List[Path] = []
    for root in SCAN_ROOTS:
        for path in sorted((REPO_ROOT / root).rglob("*.cs")):
            files.append(path)
    return files


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

    files = scan(paths)
    failures: List[str] = []
    counted = 0
    for path in files:
        failures.extend(check(path))
        counted += len(enum_bodies(mask(path.read_text(encoding="utf-8-sig"))))

    if failures:
        print("Every enum must start with an obsolete 'Unknown = 0' and value every member.", file=sys.stderr)
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        return 1

    if verbose:
        print(f"Enum defaults in range: {counted} enum(s) scanned.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:  # pragma: no cover - fail closed on any surprise
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
