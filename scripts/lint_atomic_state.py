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
This lint scans ``Runtime/`` and rejects ``Volatile.Read``, ``Volatile.Write``,
``Thread.VolatileRead``, and ``Thread.VolatileWrite`` on a field declared
``long``, ``ulong``, ``double``, ``System.Int64``, ``System.UInt64``, or
``System.Double``, plus a bare ``volatile`` 64-bit field. A bare array field
is a 32-bit object reference and is allowed; only its elements are 64-bit.
Every declarator in a multi-field declaration is read, a type alias of a
64-bit type is resolved, and a ``ref`` argument is read with balanced
brackets so a method call, cast, or multidimensional element stays whole.

Comments, character literals and the literal text of every string shape are
masked before the scan, while interpolation holes stay visible as code: a
read inside ``$"{...}"`` is caught, but prose that only names the call is
not. A static import or alias of ``System.Threading.Volatile`` or
``System.Threading.Thread`` is rejected outright, because a bare ``Read`` or
``Write`` would hide from the scan. The scan is textual: a field declared in
another partial file and a parameter or local that shadows a 64-bit field by
name are not resolved; such a shadow is already a naming violation in this
repository. ``--paths`` scans only the named C# files, and a path outside
the scan roots is skipped just as the whole-tree walk never enters it.
``VOLATILE_ATOMICITY_ROOTS`` overrides the roots for the self-test. Exit
codes: 0 = clean, 1 = a 64-bit ``Volatile`` access remains, 2 = usage error.
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
    r"(?:(?:global\s*::\s*)?System\s*\.\s*)?(long|ulong|double|Int64|UInt64|Double)[ \t]*((?:\[\s*,*\s*\])*)[ \t]+"
    r"(?=[A-Za-z_]\w*[ \t]*(?:[;=,\[]))",
    re.MULTILINE,
)
USAGE = re.compile(
    r"\b(?:Volatile\s*\.\s*(?P<direct>Read|Write)|Thread\s*\.\s*(?P<thread>VolatileRead|VolatileWrite))"
    r"\s*\(\s*ref\s+"
)
IDENTIFIER = re.compile(r"[A-Za-z_]\w*")
DECLARATOR = re.compile(r"^[ \t]*([A-Za-z_]\w*)")
TYPE_ALIASES = {"Int64": "long", "UInt64": "ulong", "Double": "double"}
TYPE_ALIAS_IMPORT = re.compile(
    r"^[ \t]*(?:global[ \t]+)?using[ \t]+(?P<alias>[A-Za-z_]\w*)[ \t\n]*=[ \t\n]*"
    r"(?:global\s*::\s*)?(?:System\s*\.\s*)?(?P<type>long|ulong|double|Int64|UInt64|Double)[ \t]*;",
    re.MULTILINE,
)
ALIAS_FIELD = re.compile(
    r"^[ \t]*(?:public|internal|protected|private)(?:[ \t]+(?:internal|protected))?"
    r"[ \t]+(?:(?:static|readonly|volatile|new)[ \t]+)*"
    r"(?P<alias>[A-Za-z_]\w*)[ \t]*((?:\[\s*,*\s*\])*)[ \t]+"
    r"(?=[A-Za-z_]\w*[ \t]*(?:[;=,\[]))",
    re.MULTILINE,
)
VOLATILE_IMPORT = re.compile(
    r"^[ \t]*(?:global[ \t]+)?using[ \t\n]+"
    r"(?:static[ \t\n]+(?:global\s*::\s*)?System\s*\.\s*Threading\s*\.\s*(?:Volatile|Thread)|"
    r"[A-Za-z_]\w*[ \t\n]*=[ \t\n]*(?:global\s*::\s*)?System\s*\.\s*Threading(?:\s*\.\s*(?:Volatile|Thread))?)"
    r"[ \t\n]*;",
    re.MULTILINE,
)


def _char_end(source: str, start: int) -> int:
    """Return the index just past the character literal at ``start``."""
    index = start + 1
    while index < len(source):
        if source[index] == "\\":
            index += 2
            continue
        if source[index] == "'":
            return index + 1
        index += 1
    return len(source)


def _string_bounds(source: str, start: int) -> tuple[int, int, int, bool]:
    """Return a string's end, content start, content end, and interpolated flag."""
    verbatim = (start > 0 and source[start - 1] == "@") or (start > 1 and source[start - 2] == "@")
    raw = source.startswith('"""', start)
    interpolated = (start > 0 and source[start - 1] == "$") or (
        start > 1 and source[start - 2] == "$"
    )
    quote = 3 if raw else 1
    index = start + quote
    while index < len(source):
        if interpolated and source.startswith("{{", index):
            index += 2
            continue
        if interpolated and source[index] == "{":
            hole = _hole_end(source, index + 1)
            if hole < 0:
                break
            index = hole + 1
            continue
        if raw and source.startswith('"""', index):
            return index + 3, start + quote, index, interpolated
        if not raw and verbatim and source.startswith('""', index):
            index += 2
            continue
        if not raw and not verbatim and source[index] == "\\":
            index += 2
            continue
        if not raw and source[index] == '"':
            return index + 1, start + quote, index, interpolated
        index += 1
    return len(source), start + quote, len(source), interpolated


def _hole_end(body: str, start: int) -> int:
    """Return the brace that closes the interpolation hole starting at ``start``, or -1."""
    depth = 1
    index = start
    while index < len(body):
        if body.startswith("//", index):
            newline = body.find("\n", index)
            index = len(body) if newline < 0 else newline
            continue
        if body.startswith("/*", index):
            close = body.find("*/", index + 2)
            index = len(body) if close < 0 else close + 2
            continue
        character = body[index]
        if character == '"':
            index = _string_bounds(body, index)[0]
            continue
        if character == "'":
            index = _char_end(body, index)
            continue
        if character == "{":
            depth += 1
        elif character == "}":
            depth -= 1
            if depth == 0:
                return index
        index += 1
    return -1


def _mask_interpolated(body: str) -> str:
    """Blank literal text but keep each interpolation hole visible as code."""
    kept: list[str] = []
    index = 0
    while index < len(body):
        character = body[index]
        if character in "{}" and index + 1 < len(body) and body[index + 1] == character:
            kept.append("  ")
            index += 2
            continue
        if character == "{":
            end = _hole_end(body, index + 1)
            if end < 0:
                kept.append(" ")
                index += 1
                continue
            kept.append("{")
            kept.append(mask(body[index + 1 : end]))
            kept.append("}")
            index = end + 1
            continue
        kept.append("\n" if character == "\n" else " ")
        index += 1
    return "".join(kept)


def mask(source: str) -> str:
    """Blank comments and literal text, keeping interpolation holes and line numbers intact."""
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
            end = _char_end(source, index)
            kept.append(" " * min(end - index, length - index))
            index = end
            continue
        if character == '"':
            end, content_start, content_end, interpolated = _string_bounds(source, index)
            quote = content_start - index
            kept.append(" " * quote)
            if interpolated:
                kept.append(_mask_interpolated(source[content_start:content_end]))
            else:
                body = source[content_start:content_end]
                kept.append("".join("\n" if item == "\n" else " " for item in body))
            kept.append(" " * quote)
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


def declarators(masked: str, start: int) -> list[str]:
    """Return every declarator name in the declaration that starts at ``start``."""
    end = declaration_end(masked, start)
    declaration = masked[start:end]
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
    names: list[str] = []
    for segment in segments:
        name = DECLARATOR.match(segment)
        if name is not None:
            names.append(name.group(1))
    return names


def shared_fields(masked: str) -> tuple[dict[str, tuple[str, bool]], int, list[tuple[int, str]]]:
    """Return 64-bit field types, their count, and the ``volatile`` declaration lines and types."""
    fields: dict[str, tuple[str, bool]] = {}
    declarations = 0
    volatile_lines: list[tuple[int, str]] = []
    aliases = {
        match.group("alias"): TYPE_ALIASES.get(match.group("type"), match.group("type"))
        for match in TYPE_ALIAS_IMPORT.finditer(masked)
    }
    for match in FIELD_START.finditer(masked):
        field_type = TYPE_ALIASES.get(match.group(1), match.group(1))
        is_array = match.group(2) != ""
        if "volatile" in match.group(0).split() and not is_array:
            volatile_lines.append((masked.count("\n", 0, match.start()) + 1, field_type))
        names = declarators(masked, match.end())
        for name in names:
            fields[name] = (field_type, is_array)
        declarations += len(names)
    for match in ALIAS_FIELD.finditer(masked):
        alias = match.group("alias")
        if alias in TYPE_ALIASES or alias in ("long", "ulong", "double") or alias not in aliases:
            continue
        field_type = aliases[alias]
        is_array = match.group(2) != ""
        if "volatile" in match.group(0).split() and not is_array:
            volatile_lines.append((masked.count("\n", 0, match.start()) + 1, field_type))
        names = declarators(masked, match.end())
        for name in names:
            fields[name] = (field_type, is_array)
        declarations += len(names)
    return fields, declarations, volatile_lines


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


def _argument_end(masked: str, start: int) -> int:
    """Return the comma or closing parenthesis that ends a ``ref`` argument."""
    depth = 0
    for index in range(start, len(masked)):
        character = masked[index]
        if character in "([{":
            depth += 1
        elif character in ")]}":
            if depth == 0:
                return index
            depth -= 1
        elif character == "," and depth == 0:
            return index
    return len(masked)


def _storage_identifiers(expression: str) -> list[str]:
    """Return identifiers outside array indexers; an index is not the storage being read."""
    kept: list[str] = []
    depth = 0
    for character in expression:
        if character == "[":
            depth += 1
            kept.append(" ")
            continue
        if character == "]":
            depth = max(0, depth - 1)
            kept.append(" ")
            continue
        kept.append(" " if depth != 0 else character)
    return IDENTIFIER.findall("".join(kept))


def _replacement(types: set[str], expression: str, read: bool) -> str:
    """Name the supported atomic replacement for a matching field's type."""
    if "ulong" in types:
        return "a long field with Interlocked.Read or Interlocked.Exchange"
    if "double" in types:
        if read:
            return f"Interlocked.CompareExchange(ref {expression}, 0d, 0d)"
        return f"Interlocked.Exchange(ref {expression}, value)"
    if read:
        return f"Interlocked.Read(ref {expression})"
    return f"Interlocked.Exchange(ref {expression}, value)"


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
        f"{display}:{masked.count(chr(10), 0, match.start()) + 1}: static import or alias of "
        "System.Threading (or its Volatile/Thread type) is not allowed; name the atomic call explicitly."
        for match in VOLATILE_IMPORT.finditer(masked)
    ]
    failures.extend(
        f"{display}:{line}: volatile 64-bit field is not atomic on a 32-bit player; "
        + (
            "use a long field with Interlocked.Read or Interlocked.Exchange."
            if field_type == "ulong"
            else "remove volatile and use Interlocked.Read and Interlocked.Exchange."
        )
        for line, field_type in volatile_lines
    )
    for match in USAGE.finditer(masked):
        start = match.end()
        expression = masked[start : _argument_end(masked, start)].strip()
        matched = [
            (name, fields[name]) for name in _storage_identifiers(expression) if name in fields
        ]
        if not matched:
            continue
        # A bare array field is a 32-bit object reference; only its elements are 64-bit.
        matched = [
            (name, info)
            for name, info in matched
            if not info[1] or re.search(rf"\b{re.escape(name)}\s*\[", expression) is not None
        ]
        if not matched:
            continue
        direct = match.group("direct")
        call = f"Volatile.{direct}" if direct else f"Thread.{match.group('thread')}"
        read = direct == "Read" or match.group("thread") == "VolatileRead"
        replacement = _replacement({info[0] for _, info in matched}, expression, read)
        line = masked.count("\n", 0, match.start()) + 1
        failures.append(
            f"{display}:{line}: {call} on 64-bit '{expression}' is not atomic "
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
            "64-bit Volatile access is not atomic on 32-bit players. Use the replacement named for "
            "each access below so the audio thread never observes half an update:",
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
