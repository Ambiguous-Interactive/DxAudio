#!/usr/bin/env python3
"""Cross-thread 64-bit state uses ``Interlocked``, never ``Volatile``.

``Volatile.Read`` and ``Volatile.Write`` of a 64-bit field are atomic on
64-bit players, but a 32-bit player performs two 32-bit accesses with a
memory barrier. IL2CPP implements ``volatile`` as a plain access plus
``il2cpp_codegen_memory_barrier()``; on ARMv7 that is two loads or two
stores. The audio callback and the main thread share packed 64-bit fields in
``Runtime/Core``: cutoff and reset generation, sample rate and revision,
gain pairs, a selected node and generation, and meter pairs. A torn read can
apply half of one update and half of another.

``Interlocked.Read`` and ``Interlocked.Exchange`` are atomic on every player.
This lint scans ``Runtime/`` and rejects ``Volatile.Read`` or
``Volatile.Write`` on a field declared ``long``, ``ulong`` or ``double``,
and rejects a ``volatile long`` or ``volatile ulong`` field outright. Every
declarator in a multi-field declaration is read.

Comments, character literals and every string shape are masked before the
scan, so prose that names a banned form cannot fail it. ``--paths`` scans
only the named C# files, and a path outside the scan roots is skipped just
as the whole-tree walk never enters it. ``VOLATILE_ATOMICITY_ROOTS``
overrides the roots for the self-test. Exit codes: 0 = clean, 1 = a 64-bit
``Volatile`` access remains, 2 = usage error.
"""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Sequence

REPO_ROOT = Path(__file__).resolve().parent.parent

SCAN_ROOTS = (
    [root for root in os.environ["VOLATILE_ATOMICITY_ROOTS"].split(os.pathsep) if root]
    if os.environ.get("VOLATILE_ATOMICITY_ROOTS")
    else ["Runtime"]
)

FIELD_START = re.compile(
    r"^[ \t]*(?:public|internal|protected|private)(?:[ \t]+(?:internal|protected))?"
    r"[ \t]+(?:(?:static|readonly|volatile|new)[ \t]+)*"
    r"(long|ulong|double)[ \t]*(?:\[\s*\])?[ \t]+",
    re.MULTILINE,
)
USAGE = re.compile(r"\bVolatile\s*\.\s*(Read|Write)\s*\(\s*ref\s+([^,)]+)")
IDENTIFIER = re.compile(r"[A-Za-z_]\w*")
DECLARATOR = re.compile(r"^[ \t]*([A-Za-z_]\w*)")


def mask(source: str) -> str:
    """Blank comments, character literals and string bodies, keeping line numbers stable."""
    kept: list[str] = []
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
            verbatim = index > 0 and source[index - 1] == "@"
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


def declaration_end(masked: str, start: int) -> int:
    """Return the offset of the semicolon that ends the declaration starting at ``start``."""
    depth = 0
    for index in range(start, len(masked)):
        character = masked[index]
        if character in "([{":
            depth += 1
        elif character in ")]}":
            depth = max(0, depth - 1)
        elif character == ";" and depth == 0:
            return index
    return len(masked)


def shared_fields(masked: str) -> tuple[set[str], int, list[int]]:
    """Return 64-bit field names, their count, and the lines of ``volatile`` declarations."""
    names: set[str] = set()
    declarations = 0
    volatile_lines: list[int] = []
    for match in FIELD_START.finditer(masked):
        if "volatile" in match.group(0).split():
            volatile_lines.append(masked.count("\n", 0, match.start()) + 1)
        end = declaration_end(masked, match.end())
        declaration = masked[match.end() : end]
        depth = 0
        segment_start = 0
        segments: list[str] = []
        for offset, character in enumerate(declaration):
            if character in "([{":
                depth += 1
            elif character in ")]}":
                depth = max(0, depth - 1)
            elif character == "," and depth == 0:
                segments.append(declaration[segment_start:offset])
                segment_start = offset + 1
        segments.append(declaration[segment_start:])
        for segment in segments:
            name = DECLARATOR.match(segment)
            if name is not None:
                names.add(name.group(1))
                declarations += 1
    return names, declarations, volatile_lines


def display_for(full_path: Path) -> str:
    """Show a repo-relative path, or an absolute one when it cannot be relativized."""
    try:
        relative = os.path.relpath(full_path, REPO_ROOT).replace(os.sep, "/")
    except ValueError:
        return str(full_path).replace(os.sep, "/")
    return str(full_path).replace(os.sep, "/") if relative.startswith("..") else relative


def list_csharp_files(root: str) -> list[Path]:
    """Walk one scan root and return its C# files in path order."""
    absolute_root = Path(root) if Path(root).is_absolute() else REPO_ROOT / root
    if not absolute_root.exists():
        return []
    return sorted((path for path in absolute_root.rglob("*.cs") if path.is_file()), key=str)


def in_scan_roots(full_path: Path) -> bool:
    """Whether a path sits under one of the roots the whole-tree walk reads."""
    resolved = full_path.resolve()
    for root in SCAN_ROOTS:
        absolute_root = Path(root) if Path(root).is_absolute() else REPO_ROOT / root
        try:
            resolved.relative_to(absolute_root.resolve())
        except ValueError:
            continue
        return True
    return False


def scoped_files(argv: Sequence[str]) -> list[Path] | None:
    """Resolve ``--paths``, or None for the whole-tree walk."""
    if "--paths" not in argv:
        return None
    names: list[str] = []
    for item in argv[list(argv).index("--paths") + 1 :]:
        if item.startswith("--"):
            break
        names.append(item)
    if not names:
        print("[atomic-state] --paths needs at least one file.", file=sys.stderr)
        raise SystemExit(2)
    files: list[Path] = []
    for name in names:
        full_path = Path(name) if Path(name).is_absolute() else REPO_ROOT / name
        if full_path.suffix != ".cs" or not full_path.is_file():
            print(
                f"[atomic-state] --paths entry is not a readable C# file: {name}", file=sys.stderr
            )
            raise SystemExit(2)
        if in_scan_roots(full_path):
            files.append(full_path)
    return sorted(files, key=str)


def check(path: Path) -> tuple[list[str], int]:
    """Return every 64-bit ``Volatile`` access in one file and its field count."""
    try:
        source = path.read_text(encoding="utf-8-sig")
    except OSError:
        return [f"{display_for(path)}: cannot read the file."], 0
    masked = mask(source)
    fields, declarations, volatile_lines = shared_fields(masked)
    display = display_for(path)
    failures: list[str] = [
        f"{display}:{line}: volatile 64-bit field is not atomic on a 32-bit player; "
        "remove volatile and use Interlocked.Read and Interlocked.Exchange."
        for line in volatile_lines
    ]
    for match in USAGE.finditer(masked):
        expression = match.group(2).strip()
        if not fields.intersection(IDENTIFIER.findall(expression)):
            continue
        line = masked.count("\n", 0, match.start()) + 1
        replacement = "Interlocked.Read" if match.group(1) == "Read" else "Interlocked.Exchange"
        failures.append(
            f"{display}:{line}: Volatile.{match.group(1)} on 64-bit '{expression}' is not atomic "
            f"on a 32-bit player; use {replacement}."
        )
    return failures, declarations


def main() -> int:
    arguments = list(sys.argv[1:])
    verbose = "--verbose" in arguments
    unknown = [
        argument
        for argument in arguments
        if argument.startswith("--") and argument not in ("--verbose", "--paths")
    ]
    if unknown:
        print("usage: lint_atomic_state.py [--paths FILE...] [--verbose]", file=sys.stderr)
        return 2
    files = scoped_files(arguments)

    scannable = (
        files
        if files is not None
        else [path for root in SCAN_ROOTS for path in list_csharp_files(root)]
    )
    failures: list[str] = []
    fields = 0
    for path in scannable:
        found, declarations = check(path)
        failures.extend(found)
        fields += declarations

    if failures:
        print(
            "64-bit Volatile access is not atomic on 32-bit players. Read with Interlocked.Read and "
            "write with Interlocked.Exchange so the audio thread never observes half an update:",
            file=sys.stderr,
        )
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        scope = "the --paths selection" if files is not None else ", ".join(SCAN_ROOTS)
        print(f"\n{len(failures)} non-atomic access(es) in {scope}.", file=sys.stderr)
        return 1

    if verbose:
        print(
            f"Atomic-state policy clean: {fields} 64-bit field(s) across {len(scannable)} file(s)."
        )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:  # pragma: no cover - fail closed on any surprise
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
