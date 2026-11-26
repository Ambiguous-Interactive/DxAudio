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
        private readonly long[] _sharedPeaks = new long[2];
        private volatile long[] _volatilePeaks = new long[2];

        internal long ReadShared()
        {
            return Interlocked.Read(ref _shared);
        }

        internal void WriteShared(long value)
        {
            Interlocked.Exchange(ref _shared, value);
            Volatile.Write(ref _count, 1);
            Volatile.Write(ref _window, new object());
            _ = Volatile.Read(ref _sharedPeaks);
            _ = Volatile.Read(ref _volatilePeaks);
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

INTERPOLATED_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class Interpolated
    {
        private long _state;

        internal string ReadState()
        {
            return $"count {Volatile.Read(ref _state)}";
        }

        internal string Prose()
        {
            return $"Volatile.Read(ref _state) is prose";
        }

        internal string Escaped()
        {
            return $"{{Volatile.Read(ref _state)}}";
        }

        internal string Nested()
        {
            return $"{Format("Volatile.Read(ref _state)")}";
        }
    }
}
"""

EXPRESSION_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class Expressions
    {
        private long[] _values;
        private long[,] _grid;
        private int _index;

        internal int Pick(int value)
        {
            return value;
        }

        internal long ReadCall()
        {
            return Volatile.Read(ref _values[Pick(_index)]);
        }

        internal long ReadCast()
        {
            return Volatile.Read(ref _values[(int)_index]);
        }

        internal long ReadGrid()
        {
            return Volatile.Read(ref _grid[0, 0]);
        }
    }
}
"""

MULTILINE_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class Multiline
    {
        private long _state;
        private const string Text = @"first
second
Volatile.Read(ref _state) is prose";

        internal long ReadState()
        {
            return Volatile.Read(ref _state);
        }
    }
}
"""

STATIC_IMPORT_FIXTURE = """using static System.Threading.Volatile;
using V = System.Threading.Volatile;

namespace Demo
{
    internal sealed class Imported
    {
        private long _state;

        internal long ReadState()
        {
            return Read(ref _state);
        }

        internal long ReadAlias()
        {
            return V.Read(ref _state);
        }
    }
}
"""

GLOBAL_IMPORT_FIXTURE = """using static global::System.Threading.Volatile;
using W = global::System.Threading.Volatile;
using static
    System.Threading.Thread;

namespace Demo
{
    internal sealed class QualifiedImport
    {
        private long _state;

        internal long ReadState()
        {
            return Read(ref _state);
        }
    }
}
"""

GLOBAL_FIELD_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class QualifiedField
    {
        private global::System.Int64 _state;

        internal long ReadState()
        {
            return Volatile.Read(ref _state);
        }
    }
}
"""

INDEX_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class Indexed
    {
        private int[] _values;
        private long[] _longs;
        private long _index;

        internal int ReadInt()
        {
            return Volatile.Read(ref _values[_index]);
        }

        internal long ReadLong()
        {
            return Volatile.Read(ref _longs[_index]);
        }
    }
}
"""

NAMESPACE_ALIAS_FIXTURE = """using Threading = System.Threading;

namespace Demo
{
    internal sealed class Alias
    {
        private long _state;

        internal long ReadState()
        {
            return Threading.Volatile.Read(ref _state);
        }
    }
}
"""

TYPE_ALIAS_FIXTURE = """using System.Threading;
using Wide = System.Int64;

namespace Demo
{
    internal sealed class TypeAlias
    {
        private Wide _state;

        internal long ReadState()
        {
            return Volatile.Read(ref _state);
        }
    }
}
"""

IMPORTED_ALIAS_FIXTURE = """using System.Threading;
using V = Volatile;

namespace Demo
{
    internal sealed class ImportedAlias
    {
        private long _state;

        internal long ReadState()
        {
            return V.Read(ref _state);
        }
    }
}
"""

RANK_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class Rank
    {
        private long[,,] _samples;

        internal long ReadSamples()
        {
            return Volatile.Read(ref _samples[0, 0, 0]);
        }
    }
}
"""

EDGE_FIXTURE = """using System.Threading;

namespace Demo
{
    internal sealed class Edge
    {
        private ulong _unsigned;
        private System.Int64 _wide;

        internal long ReadUnsigned()
        {
            return (long)Volatile.Read(ref _unsigned);
        }

        internal long ReadWide()
        {
            return Thread.VolatileRead(ref _wide);
        }

        internal long ReadQualified()
        {
            return global::System.Threading.Volatile.Read(ref _wide);
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
            "CompareExchange" in output,
            f"a double read needs the CompareExchange form: {output}",
        )
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


def case_scans_interpolation_holes_and_ignores_prose() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Interpolated.cs", INTERPOLATED_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"the interpolation fixture should fail: {output}", exit_code)
        assert_true(
            "Interpolated.cs:11:" in output,
            f"an interpolation hole is code and must be scanned: {output}",
        )
        assert_true(
            "Interpolated.cs:16:" not in output,
            f"literal prose must stay masked: {output}",
        )
        assert_true(
            "Interpolated.cs:21:" not in output,
            f"escaped braces are prose, not a hole: {output}",
        )
        assert_true(
            "Interpolated.cs:26:" not in output,
            f"a nested string literal in a hole must stay masked: {output}",
        )
        assert_true("1 non-atomic access" in output, f"only the hole is a violation: {output}")
    finally:
        remove_temp_root(root)


def case_reports_supported_replacements_and_array_references() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Edge.cs", EDGE_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"the edge fixture should fail: {output}", exit_code)
        assert_true(
            "Edge.cs:12:" in output and "long field" in output,
            f"a ulong field must name the long-field replacement: {output}",
        )
        assert_true(
            "Edge.cs:17:" in output and "Thread.VolatileRead" in output,
            f"Thread.VolatileRead must be named: {output}",
        )
        assert_true(
            "Interlocked.Read(ref _wide)" in output,
            f"a System.Int64 field must get the long advice: {output}",
        )
        assert_true(
            "Edge.cs:22:" in output,
            f"a global:: fully qualified access must be scanned: {output}",
        )
    finally:
        remove_temp_root(root)


def case_parses_balanced_ref_expressions_and_multidimensional_arrays() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Expressions.cs", EXPRESSION_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"the expression fixture should fail: {output}", exit_code)
        for line in (18, 23, 28):
            assert_true(
                f"Expressions.cs:{line}:" in output,
                f"must report the 64-bit element access on line {line}: {output}",
            )
        assert_true(
            "Interlocked.Read(ref _values[Pick(_index)])" in output,
            f"a method call in the reference must stay whole: {output}",
        )
        assert_true(
            "Interlocked.Read(ref _grid[0, 0])" in output,
            f"a multidimensional element must stay whole: {output}",
        )
        assert_true(
            "Read with Interlocked.Read" not in output,
            f"the banner must not recommend an uncompilable double replacement: {output}",
        )
    finally:
        remove_temp_root(root)


def case_preserves_line_numbers_after_multiline_strings() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Multiline.cs", MULTILINE_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"the multiline fixture should fail: {output}", exit_code)
        assert_true(
            "Multiline.cs:14:" in output,
            f"a multiline string must not shift the reported line: {output}",
        )
        assert_true(
            "Multiline.cs:10:" not in output and "Multiline.cs:12:" not in output,
            f"the prose and the shifted line must not be reported: {output}",
        )
    finally:
        remove_temp_root(root)


def case_rejects_static_imports_of_volatile() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Imported.cs", STATIC_IMPORT_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"a static import should fail: {output}", exit_code)
        assert_true(
            "Imported.cs:1:" in output and "Imported.cs:2:" in output,
            f"both the static import and the alias must be reported: {output}",
        )
        assert_true("static import" in output, f"the reason must be named: {output}")
    finally:
        remove_temp_root(root)


def case_rejects_qualified_and_multiline_static_imports() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/QualifiedImport.cs", GLOBAL_IMPORT_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"a qualified or multiline import should fail: {output}", exit_code)
        for line in (1, 2, 3):
            assert_true(
                f"QualifiedImport.cs:{line}:" in output,
                f"must report the static import on line {line}: {output}",
            )
        assert_true("static import" in output, f"the reason must be named: {output}")
    finally:
        remove_temp_root(root)


def case_recognizes_three_dimensional_array_elements() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Rank.cs", RANK_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"a three-dimensional element should fail: {output}", exit_code)
        assert_true(
            "Rank.cs:11:" in output and "Interlocked.Read(ref _samples[0, 0, 0])" in output,
            f"a three-dimensional element must stay whole: {output}",
        )
    finally:
        remove_temp_root(root)


def case_recognizes_globally_qualified_field_types() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/QualifiedField.cs", GLOBAL_FIELD_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"a globally qualified field should fail: {output}", exit_code)
        assert_true(
            "QualifiedField.cs:11:" in output and "Interlocked.Read(ref _state)" in output,
            f"a global::System.Int64 field must be a 64-bit field: {output}",
        )
    finally:
        remove_temp_root(root)


def case_excludes_array_index_identifiers_from_the_storage_match() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Indexed.cs", INDEX_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"the long-array element should fail: {output}", exit_code)
        assert_true(
            "Indexed.cs:18:" in output,
            f"a long-array element indexed by a long must be flagged: {output}",
        )
        assert_true(
            "Indexed.cs:13:" not in output,
            f"an int-array element indexed by a long must stay clean: {output}",
        )
        assert_true("1 non-atomic access" in output, f"only the long element is 64-bit: {output}")
    finally:
        remove_temp_root(root)


def case_rejects_namespace_aliases_of_threading() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/Alias.cs", NAMESPACE_ALIAS_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"a namespace alias should fail: {output}", exit_code)
        assert_true(
            "Alias.cs:1:" in output and "System.Threading" in output,
            f"the namespace alias must be reported: {output}",
        )
    finally:
        remove_temp_root(root)


def case_resolves_type_aliases_of_64_bit_fields() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/TypeAlias.cs", TYPE_ALIAS_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"the type alias fixture should fail: {output}", exit_code)
        assert_true(
            "TypeAlias.cs:12:" in output and "Interlocked.Read(ref _state)" in output,
            f"a type alias of System.Int64 must be a 64-bit field: {output}",
        )
    finally:
        remove_temp_root(root)


def case_rejects_an_alias_of_the_imported_volatile_type() -> None:
    root = new_temp_root(prefix="atomic-state-")
    try:
        write_fixture_file(root, "Runtime/ImportedAlias.cs", IMPORTED_ALIAS_FIXTURE)
        output, exit_code = invoke_atomic_state_lint(root)
        assert_exit_code(1, f"the imported alias should fail: {output}", exit_code)
        assert_true(
            "ImportedAlias.cs:2:" in output and "System.Threading" in output,
            f"an alias of the imported Volatile type must be reported: {output}",
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
        "Scans_InterpolationHolesAndIgnoresProse",
        case_scans_interpolation_holes_and_ignores_prose,
    )
    run_case(
        "Reports_SupportedReplacementsAndArrayReferences",
        case_reports_supported_replacements_and_array_references,
    )
    run_case(
        "Parses_BalancedRefExpressionsAndMultidimensionalArrays",
        case_parses_balanced_ref_expressions_and_multidimensional_arrays,
    )
    run_case(
        "Preserves_LineNumbersAfterMultilineStrings",
        case_preserves_line_numbers_after_multiline_strings,
    )
    run_case(
        "Rejects_StaticImportsOfVolatile",
        case_rejects_static_imports_of_volatile,
    )
    run_case(
        "Rejects_QualifiedAndMultilineStaticImports",
        case_rejects_qualified_and_multiline_static_imports,
    )
    run_case(
        "Recognizes_ThreeDimensionalArrayElements",
        case_recognizes_three_dimensional_array_elements,
    )
    run_case(
        "Recognizes_GloballyQualifiedFieldTypes",
        case_recognizes_globally_qualified_field_types,
    )
    run_case(
        "Excludes_ArrayIndexIdentifiersFromStorage",
        case_excludes_array_index_identifiers_from_the_storage_match,
    )
    run_case(
        "Rejects_NamespaceAliasesOfThreading",
        case_rejects_namespace_aliases_of_threading,
    )
    run_case(
        "Resolves_TypeAliasesOf64BitFields",
        case_resolves_type_aliases_of_64_bit_fields,
    )
    run_case(
        "Rejects_AnAliasOfTheImportedVolatileType",
        case_rejects_an_alias_of_the_imported_volatile_type,
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
