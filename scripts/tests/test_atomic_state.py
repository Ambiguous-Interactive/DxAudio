#!/usr/bin/env python3
"""64-bit atomic-state lint self-tests.

Covers the shipped policy: a ``long``, ``ulong`` or ``double`` field in
``Runtime/`` reads and writes through ``Interlocked``, while an ``int`` or a
reference field may use ``Volatile``. The cases pin the positive control,
the negative control with line numbers, comment and string masking, and the
``--paths`` fail-closed behavior.
"""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Sequence

sys.dont_write_bytecode = True

from _test_helpers import (  # noqa: E402 - helpers are imported after the bytecode policy is set.
    assert_exit_code,
    assert_true,
    failure_count,
    find_python,
    new_temp_root,
    remove_temp_root,
    run_case,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
LINT_SCRIPT = SCRIPTS_DIR / "lint_atomic_state.py"

CLEAN_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class Compliant
    {
        private long _shared;
        private int _count;
        private object _window;

        internal long ReadShared()
        {
            return Interlocked.Read(ref _shared);
        }

        internal void WriteShared(long value)
        {
            Interlocked.Exchange(ref _shared, value);
            Volatile.Write(ref _count, 1);
            Volatile.Write(ref _window, new object());
        }
    }
}
"""

VIOLATING_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class Violating
    {
        private long _format;
        private readonly long[] _peaks = new long[4];
        private double _gain;

        internal long ReadFormat()
        {
            return Volatile.Read(ref _format);
        }

        internal void WriteFormat(long value)
        {
            Volatile.Write(ref _format, value);
            Volatile.Write(ref _peaks[0], value);
            _ = Volatile.Read(ref _gain);
        }
    }
}
"""

MASKED_FIXTURE = """namespace Demo
{
    // Volatile.Read(ref _format) is banned in code.
    internal sealed class Masked
    {
        private long _format;
        private const string Usage = "Volatile.Write(ref _format, 1)";

        internal void Run()
        {
            System.Console.WriteLine($"Volatile.Read(ref _format)");
        }
    }
}
"""

MULTI_FIELD_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class Declarations
    {
        private long _first, _shared;
        private volatile long _direct;

        internal long ReadBoth()
        {
            return Volatile.Read(ref _shared) + Volatile.Read(ref _first);
        }
    }
}
"""


def invoke_atomic_state_lint(
    root: Path, arguments: Sequence[str] = (), roots: Path | None = None
) -> tuple[str, int]:
    """Run the lint with the roots override set to the fixture root, then restore it."""
    had_previous = "VOLATILE_ATOMICITY_ROOTS" in os.environ
    previous = os.environ.get("VOLATILE_ATOMICITY_ROOTS")
    try:
        os.environ["VOLATILE_ATOMICITY_ROOTS"] = str(roots if roots is not None else root)
        return run_tool_output([find_python(), str(LINT_SCRIPT), *arguments])
    finally:
        if had_previous:
            os.environ["VOLATILE_ATOMICITY_ROOTS"] = previous or ""
        else:
            os.environ.pop("VOLATILE_ATOMICITY_ROOTS", None)


def case_passes_interlocked_and_32_bit_volatile() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Clean.cs", CLEAN_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root, ("--verbose",))
        assert_exit_code(0, f"the clean fixture should pass: {output}", exit_code)
        assert_true(
            re.search(r"Atomic-state policy clean: \d+ 64-bit field", output) is not None,
            f"the clean run should count its fields: {output}",
        )
    finally:
        remove_temp_root(root)


def case_fails_on_each_64_bit_form_with_locations() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Banned.cs", VIOLATING_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"the violating fixture should fail: {output}", exit_code)
        for line in (13, 18, 19, 20):
            assert_true(
                re.search(rf"Banned\.cs:{line}:", output) is not None,
                f"must report the 64-bit access on line {line}: {output}",
            )
        assert_true("4 non-atomic access(es)" in output, f"must count every access: {output}")
        assert_true(
            "Interlocked.Read" in output and "Interlocked.Exchange" in output,
            f"must name the replacement: {output}",
        )
    finally:
        remove_temp_root(root)


def case_ignores_comments_and_strings() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Masked.cs", MASKED_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(0, f"masked occurrences must pass: {output}", exit_code)
    finally:
        remove_temp_root(root)


def case_reads_every_declarator_and_rejects_volatile_long() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Declarations.cs", MULTI_FIELD_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"the multi-field fixture should fail: {output}", exit_code)
        assert_true(
            "Declarations.cs:7:" not in output,
            f"a plain multi-field declaration is allowed: {output}",
        )
        assert_true(
            "Declarations.cs:8:" in output and "volatile 64-bit" in output,
            f"a volatile long field must fail: {output}",
        )
        assert_true(
            output.count("Declarations.cs:12:") == 2,
            f"both declarators must be enforced: {output}",
        )
        assert_true(
            "'_first'" in output and "'_shared'" in output,
            f"both declarators must be named: {output}",
        )
    finally:
        remove_temp_root(root)


def case_paths_scans_only_runtime_files_and_fails_closed() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Clean.cs", CLEAN_FIXTURE)
        write_fixture_file(root, "Runtime/Banned.cs", VIOLATING_FIXTURE)
        write_fixture_file(root, "Editor/Banned.cs", VIOLATING_FIXTURE)

        output, exit_code = invoke_atomic_state_lint(
            root, ("--paths", str(root / "Runtime/Clean.cs"), "--verbose"), roots=root / "Runtime"
        )
        assert_exit_code(0, f"the named clean file must pass: {output}", exit_code)
        assert_true(
            "across 1 file(s)" in output,
            f"the scoped scan must count only the named file: {output}",
        )

        output, exit_code = invoke_atomic_state_lint(
            root, ("--paths", str(root / "Runtime/Banned.cs")), roots=root / "Runtime"
        )
        assert_exit_code(1, f"the named violating file must fail: {output}", exit_code)
        assert_true(
            re.search(r"Banned\.cs:\d+", output) is not None,
            f"the scoped scan must report: {output}",
        )

        output, exit_code = invoke_atomic_state_lint(
            root, ("--paths", str(root / "Editor/Banned.cs"), "--verbose"), roots=root / "Runtime"
        )
        assert_exit_code(
            0, f"an out-of-root entry must be skipped like the walk does: {output}", exit_code
        )
        assert_true(
            "across 0 file(s)" in output, f"the skipped entry must not be counted: {output}"
        )

        output, exit_code = invoke_atomic_state_lint(root, ("--paths", str(root / "Missing.cs")))
        assert_exit_code(2, f"a missing --paths entry must fail closed: {output}", exit_code)

        notes = root / "notes.md"
        notes.write_text("Volatile.Read(ref _format);", encoding="utf-8")
        output, exit_code = invoke_atomic_state_lint(root, ("--paths", str(notes)))
        assert_exit_code(2, f"a non-C# --paths entry must fail closed: {output}", exit_code)

        output, exit_code = invoke_atomic_state_lint(root, ("--fix",))
        assert_exit_code(2, f"an unknown option must fail closed: {output}", exit_code)
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== C# atomic-state self-tests ==")

    run_case("Passes_InterlockedAnd32BitVolatile", case_passes_interlocked_and_32_bit_volatile)
    run_case("Fails_OnEach64BitFormWithLocations", case_fails_on_each_64_bit_form_with_locations)
    run_case("Ignores_CommentsAndStrings", case_ignores_comments_and_strings)
    run_case(
        "Reads_EveryDeclaratorAndRejectsVolatileLong",
        case_reads_every_declarator_and_rejects_volatile_long,
    )
    run_case(
        "Paths_ScansOnlyRuntimeFilesAndFailsClosed",
        case_paths_scans_only_runtime_files_and_fails_closed,
    )

    failures = failure_count()
    if failures > 0:
        print(f"atomic-state self-tests: {failures} failed")
        return 1
    print("atomic-state self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
