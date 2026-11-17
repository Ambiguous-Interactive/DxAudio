#!/usr/bin/env python3
"""PlayMode source policy contract tests (issue #53).

The editor died on a fatal abort during one PlayMode run, and this repository has
lost the editor twice more while it imported assets. Each case plants one way a
PlayMode test can change an asset inside the running player loop and asserts the
lint fails, plus the shapes that must stay clean: a comment, an EditMode fixture,
and a package with no PlayMode tree at all.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from typing import Callable, List, Tuple

sys.dont_write_bytecode = True

from _test_helpers import assert_exit_code, assert_true, new_temp_root, remove_temp_root, run_cases

TESTS_DIR = Path(__file__).resolve().parent
LINT = TESTS_DIR.parent / "lint_playmode_sources.py"

CLEAN_SOURCE = "namespace WallstopStudios.DxAudio.Tests.PlayMode\n{\n    public sealed class Probe\n    {\n    }\n}\n"


def assert_contains(description: str, needle: str, haystack: str) -> None:
    assert_true(needle in haystack, f"{description} (missing: [{needle}])")


class LintSandbox:
    """A throwaway package tree the lint scans through ``--repo-root``."""

    def __init__(self) -> None:
        self.root = new_temp_root(prefix="playmode-sources-test-")
        self.last_exit = -1
        self.output = ""
        self.write("Tests/PlayMode/Probe.cs", CLEAN_SOURCE)
        self.write("Tests/Editor/Unity/Fixture.cs", CLEAN_SOURCE)

    def write(self, relative: str, content: str = "x") -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="")

    def run(self, *extra: str) -> None:
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


def case_clean_tree_passes() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.run()
        assert_exit_code(0, f"a clean PlayMode tree must pass: {sandbox.output}", sandbox.last_exit)
        assert_true(sandbox.output.strip() == "", f"a clean run must print nothing: {sandbox.output}")
        sandbox.run("--verbose")
        assert_exit_code(0, f"a clean verbose run must pass: {sandbox.output}", sandbox.last_exit)
        assert_contains("the verbose run counts the sources", "1 PlayMode source file(s)", sandbox.output)
    finally:
        sandbox.destroy()


def case_asset_identifiers_fail() -> None:
    sandbox = LintSandbox()
    try:
        lines = [
            "AssetDatabase.CreateFolder(\"Assets\", \"Probe\");",
            "AssetImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;",
            "importer.SaveAndReimport();",
            "AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);",
            "File.Create(assetPath);",
            "File.WriteAllLines(reportPath, lines);",
        ]
        bodies = "\n".join(f"        {line}" for line in lines)
        sandbox.write(
            "Tests/PlayMode/Probe.cs",
            "namespace WallstopStudios.DxAudio.Tests.PlayMode\n{\n    public sealed class Probe\n    {\n        private void Step()\n        {\n"
            + bodies
            + "\n        }\n    }\n}\n",
        )
        sandbox.run()
        assert_exit_code(1, "an asset mutation in a PlayMode test must fail", sandbox.last_exit)
        for number in range(7, 13):
            assert_contains(f"the failure names Tests/PlayMode/Probe.cs:{number}", f"Tests/PlayMode/Probe.cs:{number}", sandbox.output)
        assert_contains("the failure names the player loop", "runs a refresh in the player loop", sandbox.output)
    finally:
        sandbox.destroy()


def case_nested_and_generated_folders_are_scanned() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write(
            "Tests/PlayMode/Probes/Inner/Nested.cs",
            "// placeholder\npublic sealed class Nested\n{\n    // AssetDatabase in a comment is prose\n}\n",
        )
        sandbox.run()
        assert_exit_code(0, f"a comment mentioning the form must not fail: {sandbox.output}", sandbox.last_exit)
        sandbox.write("Tests/PlayMode/Probes/Inner/Nested.cs", "public sealed class Nested\n{\n    void M() { File.AppendAllText(p, \"x\"); }\n}\n")
        sandbox.run()
        assert_exit_code(1, "a nested PlayMode source must be scanned", sandbox.last_exit)
        assert_contains("the failure names the nested file", "Tests/PlayMode/Probes/Inner/Nested.cs:3", sandbox.output)
    finally:
        sandbox.destroy()


def case_editmode_fixture_imports_stay_in_scope() -> None:
    sandbox = LintSandbox()
    try:
        sandbox.write(
            "Tests/Editor/Unity/Importer.cs",
            "public sealed class Importer\n{\n    void M() { AssetDatabase.ImportAsset(path); }\n}\n",
        )
        sandbox.run()
        assert_exit_code(0, f"an EditMode fixture owns its imports: {sandbox.output}", sandbox.last_exit)
    finally:
        sandbox.destroy()


def case_missing_playmode_tree_passes() -> None:
    root = new_temp_root(prefix="playmode-sources-absent-")
    try:
        (root / "Tests" / "Editor").mkdir(parents=True)
        completed = subprocess.run(
            [sys.executable, str(LINT), "--repo-root", str(root), "--verbose"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
            check=False,
        )
        output = (completed.stdout or "") + (completed.stderr or "")
        assert_exit_code(0, f"a package with no PlayMode tree must pass: {output}", completed.returncode)
        assert_contains("the verbose run reports the empty scan", "0 PlayMode source file(s)", output)
    finally:
        remove_temp_root(root)


def case_missing_root_fails() -> None:
    missing = new_temp_root(prefix="playmode-sources-missing-") / "absent"
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
    print("== PlayMode source policy self-tests ==")
    cases: List[Tuple[str, Callable[[], None]]] = [
        ("Tree_CleanPasses", case_clean_tree_passes),
        ("Asset_IdentifiersFail", case_asset_identifiers_fail),
        ("Tree_NestedSourcesScanned", case_nested_and_generated_folders_are_scanned),
        ("EditMode_ImportsStayInScope", case_editmode_fixture_imports_stay_in_scope),
        ("Tree_MissingPlayModeTreePasses", case_missing_playmode_tree_passes),
        ("Root_MissingRootFails", case_missing_root_fails),
    ]
    failed = run_cases(cases)
    if failed:
        print(f"PlayMode source policy self-tests: {failed} failed")
        return 1
    print("PlayMode source policy self-tests: all passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
