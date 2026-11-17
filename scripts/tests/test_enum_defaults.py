#!/usr/bin/env python3
"""Enum-default self-tests: every enum starts with an obsolete ``Unknown = 0``."""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path
from typing import List, Tuple

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    find_python,
    new_temp_root,
    remove_temp_root,
    run_cases,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
REPO_ROOT = SCRIPTS_DIR.parent
LINT_SCRIPT = SCRIPTS_DIR / "lint_enum_defaults.py"

GOOD_ENUM = """namespace Fixture
{
    public enum LoadType
    {
        [Obsolete("Declare a value; Unknown is what an unset field holds.", true)]
        Unknown = 0,

        Auto = 1,
    }
}
"""


def invoke(root: Path, arguments: List[str] = ()) -> Tuple[str, int]:
    env = dict(os.environ)
    env["ENUM_DEFAULTS_ROOTS"] = str(root / "Runtime")
    return run_tool_output([find_python(), str(LINT_SCRIPT), *arguments], env=env)


def run_fixture(source: str, arguments: List[str] = (), expect: int = 0, message: str = "") -> None:
    root = new_temp_root(prefix="enum-defaults-")
    try:
        write_fixture_file(root, "Runtime/Enums.cs", source)
        output, code = invoke(root, arguments)
        assert_exit_code(expect, f"fixture outcome: {output}", code)
        if message:
            assert_true(message in output, output)
    finally:
        remove_temp_root(root)


def case_passes_for_the_sanctioned_shape() -> None:
    run_fixture(GOOD_ENUM, ["--verbose"], 0, "Enum defaults in range: 1 enum(s) scanned.")


def case_fails_when_the_first_member_is_not_unknown() -> None:
    broken = GOOD_ENUM.replace("Unknown = 0,\n\n        Auto = 1,", "Auto = 0,\n\n        Streaming = 1,")
    run_fixture(broken, [], 1, "must declare 'Unknown = 0' first, not 'Auto'")


def case_fails_when_unknown_is_not_zero() -> None:
    broken = GOOD_ENUM.replace("Unknown = 0", "Unknown = 5")
    run_fixture(broken, [], 1, "must give Unknown the explicit value 0")


def case_fails_when_unknown_is_implicit() -> None:
    broken = GOOD_ENUM.replace("Unknown = 0", "Unknown")
    run_fixture(broken, [], 1, "must give Unknown the explicit value 0")


def case_fails_when_unknown_is_not_obsolete() -> None:
    broken = GOOD_ENUM.replace('        [Obsolete("Declare a value; Unknown is what an unset field holds.", true)]\n', "")
    run_fixture(broken, [], 1, "must mark Unknown [Obsolete]")


def case_fails_when_the_attribute_is_not_obsolete() -> None:
    broken = GOOD_ENUM.replace(
        '[Obsolete("Declare a value; Unknown is what an unset field holds.", true)]',
        '[System.ComponentModel.Description("Unset")]',
    )
    run_fixture(broken, [], 1, "must mark Unknown [Obsolete]")


def case_fails_when_a_later_member_has_no_value() -> None:
    broken = GOOD_ENUM.replace("        Auto = 1,", "        Auto,")
    run_fixture(broken, [], 1, "must give every member an explicit value, but 'Auto' has none")


def case_ignores_prose_and_strings() -> None:
    source = (
        "namespace Fixture\n{\n"
        "    // An enum in a comment: enum Imaginary { Auto = 0 }\n"
        "    /* A block comment: enum Blocked { Auto = 0 } */\n"
        '    internal static class Text { internal const string Note = "enum Quoted { Auto = 0 }"; }\n'
        '    internal const string Verbatim = @"enum Spread {\n        Auto = 0,\n    }";\n'
        "}\n"
    )
    run_fixture(source, ["--verbose"], 0, "0 enum(s) scanned.")


def case_passes_against_the_repository_itself() -> None:
    output, code = run_tool_output([find_python(), str(LINT_SCRIPT), "--verbose"])
    assert_exit_code(0, f"the repository must satisfy the enum rule: {output}", code)
    assert_true(re.search(r"Enum defaults in range: [1-9]\d* enum", output) is not None, output)


def main() -> int:
    print("== Enum default self-tests ==")
    run_cases(
        [
            ("Passes_ForTheSanctionedShape", case_passes_for_the_sanctioned_shape),
            ("Fails_WhenTheFirstMemberIsNotUnknown", case_fails_when_the_first_member_is_not_unknown),
            ("Fails_WhenUnknownIsNotZero", case_fails_when_unknown_is_not_zero),
            ("Fails_WhenUnknownIsImplicit", case_fails_when_unknown_is_implicit),
            ("Fails_WhenUnknownIsNotObsolete", case_fails_when_unknown_is_not_obsolete),
            ("Fails_WhenTheAttributeIsNotObsolete", case_fails_when_the_attribute_is_not_obsolete),
            ("Fails_WhenALaterMemberHasNoValue", case_fails_when_a_later_member_has_no_value),
            ("Ignores_ProseAndStrings", case_ignores_prose_and_strings),
            ("Passes_AgainstTheRepositoryItself", case_passes_against_the_repository_itself),
        ]
    )
    failures = failure_count()
    if failures > 0:
        print(f"enum-default self-tests: {failures} failed")
        return 1
    print("enum-default self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
