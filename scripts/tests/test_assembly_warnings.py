#!/usr/bin/env python3
"""Assembly warning policy contract tests (issues #37 and #40).

The policy exists so a hidden compiler warning fails a check instead of
reaching a player, and it runs in the fast check, the gate, and CI. Each case
plants one way a warning can be hidden in a throwaway tree and asserts the
lint fails on it, plus the shapes that must stay clean: prose that mentions a
suppression, a ``Tests/`` fixture, and a pristine package.
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
SYNC = TESTS_DIR.parent / "sync_compiler_policy.py"

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
        path.write_text(content, encoding="utf-8", newline="")

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

    def sync(self, verbose: bool = False) -> None:
        """Write the generated policy the same way a local or CI run does."""
        arguments = [sys.executable, str(SYNC), "--repo-root", str(self.root)]
        completed = subprocess.run(
            [*arguments, *(["--verbose"] if verbose else [])],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
            check=False,
        )
        assert_exit_code(0, f"the sync must write the policy: {completed.stdout}{completed.stderr}", completed.returncode)
        self.output = (completed.stdout or "") + (completed.stderr or "")

    def destroy(self) -> None:
        remove_temp_root(self.root)


def case_clean_package_passes() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.sync()
        sandbox.run()
        assert_exit_code(0, f"a clean package must pass: {sandbox.output}", sandbox.last_exit)
        assert_true(sandbox.output.strip() == "", f"a clean run must print nothing: {sandbox.output}")
        sandbox.run("--verbose")
        assert_exit_code(0, f"a clean verbose run must pass: {sandbox.output}", sandbox.last_exit)
        assert_contains("the verbose run reports the scan", "[assembly-warnings] ok:", sandbox.output)
        assert_contains("the verbose run counts the shipped sources", "2 shipped source file(s)", sandbox.output)
        assert_contains("the verbose run counts the assemblies", "1 assembly folder(s)", sandbox.output)
    finally:
        sandbox.destroy()


def case_missing_generated_policy_fails() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.run()
        assert_exit_code(1, "an assembly without the generated policy must fail", sandbox.last_exit)
        assert_contains("the failure names the response file", "Runtime/csc.rsp is missing", sandbox.output)
        assert_contains("the failure names the ruleset", "Runtime/WarningsAsErrors.ruleset is missing", sandbox.output)
        assert_contains("the failure names the sync script", "run scripts/sync_compiler_policy.py", sandbox.output)
    finally:
        sandbox.destroy()


def case_weakened_policy_fails() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.sync()
        sandbox.write("Runtime/csc.rsp", "-warn:9999\n-nowarn:0618\n")
        sandbox.write(
            "Runtime/WarningsAsErrors.ruleset",
            '<?xml version="1.0" encoding="utf-8"?>\n<RuleSet Name="x"><IncludeAll Action="Warning" /></RuleSet>\n',
        )
        sandbox.run()
        assert_exit_code(1, "a weakened policy must fail", sandbox.last_exit)
        assert_contains("the failure names the weakened response file", "Runtime/csc.rsp does not carry", sandbox.output)
        assert_contains(
            "the failure names the weakened ruleset", "Runtime/WarningsAsErrors.ruleset does not carry", sandbox.output
        )
    finally:
        sandbox.destroy()


def case_sync_writes_the_strict_policy_once() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.sync()
        response = (sandbox.root / "Runtime" / "csc.rsp").read_text(encoding="utf-8")
        ruleset = (sandbox.root / "Runtime" / "WarningsAsErrors.ruleset").read_text(encoding="utf-8")
        # Pin the literal policy: a weakened constant must fail here, not only in
        # a tree where someone also planted a matching suppression.
        assert_true(response == "-warn:9999\n", f"the response file must carry the maximum warning level: [{response}]")
        assert_true(
            '<IncludeAll Action="Error" />' in ruleset,
            f"the ruleset must promote every warning: [{ruleset}]",
        )
        assert_true("\r" not in response + ruleset, "the policy must use LF endings")
        sandbox.sync(verbose=True)
        assert_contains("a second sync must rewrite nothing", "wrote 0 file(s)", sandbox.output)
        assert_true(
            not (sandbox.root / "Tests").exists(),
            "a directory without an assembly definition must stay untouched",
        )
    finally:
        sandbox.destroy()


def case_planted_suppression_fails() -> None:
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
        sandbox.write("Editor/Tool.cs", "#\tPRAGMA   warning    DISABLE   0618\r\n// body\r\n")
        sandbox.run()
        assert_exit_code(1, "a mixed-case, tabbed, CRLF suppression must fail", sandbox.last_exit)
        assert_contains("the failure names the editor file", "Editor/Tool.cs:1", sandbox.output)
    finally:
        sandbox.destroy()


def case_prose_mentioning_a_suppression_passes() -> None:
    sandbox = LintSandbox()
    try:
        # A directive must start its line, so a comment or a string that
        # mentions the form cannot fail the scan.
        sandbox.sync()
        sandbox.write(
            "Runtime/Documented.cs",
            '// Example: "#pragma warning disable CS0618" is forbidden here.\n'
            "public sealed class Documented\n{\n}\n",
        )
        sandbox.run()
        assert_exit_code(0, f"prose must not fail the scan: {sandbox.output}", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_stray_compiler_configuration_files_fail() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.sync()
        sandbox.write("Editor/strict.ruleset", "<RuleSet Name=\"x\" />\n")
        sandbox.write(".net-tests/Directory.Build.rsp", "-nowarn:0618\n")
        sandbox.write("Tests/Editor/UPPER.RSP", "-nowarn:0169\n")
        sandbox.run()
        assert_exit_code(1, "a response file or ruleset can hide a warning and must fail", sandbox.last_exit)
        for path in ("Editor/strict.ruleset", ".net-tests/Directory.Build.rsp", "Tests/Editor/UPPER.RSP"):
            assert_contains(f"the failure names {path}", path, sandbox.output)
        assert_contains("the failure names the class", "configures the package compiler", sandbox.output)
    finally:
        sandbox.destroy()


def case_build_output_and_git_metadata_are_skipped() -> None:
    sandbox = LintSandbox()
    try:
        # Tool caches and Unity's tilde folders never compile, so a stale
        # response file inside one must not fail the live tree.
        sandbox.sync()
        sandbox.write(".git/csc.rsp", "-nowarn:0618\n")
        sandbox.write(".net-tests/obj/Generated.rsp", "-nowarn:0618\n")
        sandbox.write(".net-tests/bin/stale.ruleset", "<RuleSet />\n")
        sandbox.write("Generator~/csc.rsp", "-nowarn:0618\n")
        sandbox.run()
        assert_exit_code(0, f"skipped trees must not fail the scan: {sandbox.output}", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_missing_warnings_as_errors_fails() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.sync()
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


def case_last_warnings_as_errors_setting_wins() -> None:
    sandbox = LintSandbox()
    try:
        # MSBuild is last-wins: a later false must fail even though a true
        # appears earlier in the file.
        sandbox.sync()
        sandbox.write(
            ".net-tests/DxAudio.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            "  <PropertyGroup>\n"
            "    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>\n"
            "  </PropertyGroup>\n"
            "  <PropertyGroup>\n"
            "    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>\n"
            "  </PropertyGroup>\n"
            "</Project>\n",
        )
        sandbox.run()
        assert_exit_code(1, "a later false must not pass the policy", sandbox.last_exit)
        assert_contains("the failure names the setting", "TreatWarningsAsErrors", sandbox.output)
    finally:
        sandbox.destroy()


def case_test_fixture_suppression_is_out_of_scope() -> None:
    sandbox = LintSandbox()
    try:
        # A Unity test may pin one editor's obsolete API and suppress the
        # warning the other raises; the shipped assemblies stay covered.
        sandbox.sync()
        sandbox.write("Tests/Editor/Unity/Fixture.cs", "#pragma warning disable CS0618\n")
        sandbox.run()
        assert_exit_code(0, f"a Tests fixture suppression must stay out of scope: {sandbox.output}", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_missing_root_fails() -> None:
    missing = new_temp_root(prefix="assembly-warnings-missing-") / "absent"
    try:
        completed = subprocess.run(
            [sys.executable, str(LINT), "--repo-root", str(missing)],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
            check=False,
        )
        output = (completed.stdout or "") + (completed.stderr or "")
        assert_exit_code(1, f"an absent root must fail: {output}", completed.returncode)
        assert_contains("the failure names the missing root", "repository root does not exist", output)
    finally:
        remove_temp_root(missing.parent)


def main() -> int:
    print("== Assembly warning policy self-tests ==")
    cases: List[Tuple[str, Callable[[], None]]] = [
        ("Clean_PackagePasses", case_clean_package_passes),
        ("Generated_MissingPolicyFails", case_missing_generated_policy_fails),
        ("Generated_WeakenedPolicyFails", case_weakened_policy_fails),
        ("Generated_SyncWritesTheStrictPolicyOnce", case_sync_writes_the_strict_policy_once),
        ("Planted_SuppressionFails", case_planted_suppression_fails),
        ("Suppression_SpellingAndLocationDoNotMatter", case_suppression_spelling_and_location_do_not_matter),
        ("Prose_MentioningASuppressionPasses", case_prose_mentioning_a_suppression_passes),
        ("Compiler_StrayConfigurationFilesFail", case_stray_compiler_configuration_files_fail),
        ("Build_OutputAndGitMetadataAreSkipped", case_build_output_and_git_metadata_are_skipped),
        ("Project_MissingWarningsAsErrorsFails", case_missing_warnings_as_errors_fails),
        ("Project_LastWarningsAsErrorsSettingWins", case_last_warnings_as_errors_setting_wins),
        ("Tests_FixtureSuppressionIsOutOfScope", case_test_fixture_suppression_is_out_of_scope),
        ("Root_MissingRootFails", case_missing_root_fails),
    ]
    failed = run_cases(cases)
    if failed:
        print(f"assembly warning policy self-tests: {failed} failed")
        return 1
    print("assembly warning policy self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
