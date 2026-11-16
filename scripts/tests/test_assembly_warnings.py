#!/usr/bin/env python3
"""Assembly warning policy contract tests (issue #37).

The policy exists so a hidden compiler warning fails a check instead of
reaching a player, and it runs in the fast check, the gate, and CI. Each case
plants one way a warning can be hidden in a throwaway tree and asserts the
lint fails on it, plus the two shapes that must stay out of scope: the
``Tests/`` fixtures and a pristine tree.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from typing import Callable, List, Tuple

sys.dont_write_bytecode = True

from _test_helpers import assert_exit_code, assert_true, new_temp_root, remove_temp_root, run_cases

TESTS_DIR = Path(__file__).resolve().parent
LINT = TESTS_DIR.parent / "lint_assembly_warnings.py"

CLEAN_SOURCE = "namespace WallstopStudios.DxAudio\n{\n    public sealed class Audio\n    {\n    }\n}\n"

PROJECT_WITH_WARNINGS_AS_ERRORS = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
"""


def assert_contains(description: str, needle: str, haystack: str) -> None:
    assert_true(needle in haystack, f"{description} (missing: [{needle}])")


class LintSandbox:
    """A throwaway package tree the lint scans through ``--repo-root``."""

    def __init__(self) -> None:
        self.root = new_temp_root(prefix="assembly-warnings-test-")
        self.last_exit = -1
        self.output = ""
        self.write(".net-tests/DxAudio.Tests.csproj", PROJECT_WITH_WARNINGS_AS_ERRORS)
        self.write("Runtime/WallstopStudios.DxAudio.asmdef", "{}\n")
        self.write("Runtime/Audio.cs", CLEAN_SOURCE)
        self.write("Editor/Tool.cs", CLEAN_SOURCE)

    def write(self, relative: str, content: str = "x") -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def run(self, *extra: str) -> None:
        """Run the real lint against the sandbox and capture its verdict."""
        completed = subprocess.run(
            [sys.executable, str(LINT), "--repo-root", str(self.root), *extra],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
            check=False,
        )
        self.last_exit = completed.returncode
        self.output = (completed.stdout or "") + (completed.stderr or "")

    def destroy(self) -> None:
        remove_temp_root(self.root)


def case_a_clean_package_passes() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.run()
        assert_exit_code(0, f"a clean package must pass: {sandbox.output}", sandbox.last_exit)
        assert_true(sandbox.output.strip() == "", f"a clean run must print nothing: {sandbox.output}")
        sandbox.run("--verbose")
        assert_exit_code(0, f"a clean verbose run must pass: {sandbox.output}", sandbox.last_exit)
        assert_contains("the verbose run reports the scan", "[assembly-warnings] ok:", sandbox.output)
        assert_contains("the verbose run counts the shipped sources", "2 shipped source file(s)", sandbox.output)
    finally:
        sandbox.destroy()


def case_a_planted_warning_suppression_fails() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write(
            "Runtime/Obsolete.cs",
            "#pragma warning disable CS0618\npublic sealed class Obsolete\n{\n}\n",
        )
        sandbox.run()
        assert_exit_code(1, "a planted warning suppression must fail the step", sandbox.last_exit)
        assert_contains("the failure names the file and line", "Runtime/Obsolete.cs:1", sandbox.output)
        assert_contains("the failure names the class", "disables a compiler warning", sandbox.output)
    finally:
        sandbox.destroy()


def case_suppression_spelling_and_location_do_not_matter() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write("Editor/Tool.cs", "#   PRAGMA   warning    DISABLE   0618\n// body\n")
        sandbox.run()
        assert_exit_code(1, "a mixed-case, spaced suppression must fail", sandbox.last_exit)
        assert_contains("the failure names the editor file", "Editor/Tool.cs:1", sandbox.output)
    finally:
        sandbox.destroy()


def case_compiler_configuration_files_fail() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write("Runtime/csc.rsp", "-warn:5\n")
        sandbox.write("Editor/strict.ruleset", "<RuleSet Name=\"x\" />\n")
        sandbox.run()
        assert_exit_code(1, "a response file or ruleset can hide a warning and must fail", sandbox.last_exit)
        assert_contains("the failure names the response file", "Runtime/csc.rsp", sandbox.output)
        assert_contains("the failure names the ruleset", "Editor/strict.ruleset", sandbox.output)
        assert_contains("the failure names the class", "configures the package compiler", sandbox.output)
    finally:
        sandbox.destroy()


def case_a_missing_warnings_as_errors_project_fails() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write(
            ".net-tests/DxAudio.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n  </PropertyGroup>\n</Project>\n",
        )
        sandbox.run()
        assert_exit_code(1, "the engine-free build must keep warnings as errors", sandbox.last_exit)
        assert_contains("the failure names the project", ".net-tests/DxAudio.Tests.csproj", sandbox.output)
        assert_contains("the failure names the setting", "TreatWarningsAsErrors", sandbox.output)
    finally:
        sandbox.destroy()


def case_test_fixture_suppression_is_out_of_scope() -> None:
    sandbox = LintSandbox()
    try:
        # A Unity test may pin one editor's obsolete API and suppress the
        # warning the other raises; the shipped assemblies stay covered.
        sandbox.write("Tests/Editor/Unity/Fixture.cs", "#pragma warning disable CS0618\n")
        sandbox.run()
        assert_exit_code(0, f"a Tests fixture suppression must stay out of scope: {sandbox.output}", sandbox.last_exit)
    finally:
        sandbox.destroy()


def main() -> int:
    print("== Assembly warning policy self-tests ==")
    cases: List[Tuple[str, Callable[[], None]]] = [
        ("Clean_PackagePasses", case_a_clean_package_passes),
        ("Planted_SuppressionFails", case_a_planted_warning_suppression_fails),
        ("Suppression_SpellingAndLocationDoNotMatter", case_suppression_spelling_and_location_do_not_matter),
        ("Compiler_ConfigurationFilesFail", case_compiler_configuration_files_fail),
        ("Project_MissingWarningsAsErrorsFails", case_a_missing_warnings_as_errors_project_fails),
        ("Tests_FixtureSuppressionIsOutOfScope", case_test_fixture_suppression_is_out_of_scope),
    ]
    failed = run_cases(cases)
    if failed:
        print(f"assembly warning policy self-tests: {failed} failed")
        return 1
    print("assembly warning policy self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
