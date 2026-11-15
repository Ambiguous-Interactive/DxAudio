#!/usr/bin/env python3
"""Contract tests for the CI Unity project preparer."""

from __future__ import annotations

import json
import sys
from pathlib import Path

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    new_temp_root,
    remove_temp_root,
    run_case,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
SCRIPT = REPO_ROOT / "scripts" / "ci_prepare_project.py"


def prepare(package: Path, output: Path, version: str = "6000.6.7f1"):
    return run_tool_output(
        [sys.executable, str(SCRIPT), "--package", str(package), "--output", str(output), "--unity-version", version]
    )


def case_writes_a_manifest_that_references_the_package() -> None:
    root = new_temp_root(prefix="ci-prepare-")
    try:
        package = root / "package"
        write_fixture_file(package, "package.json", '{"name":"com.wallstop-studios.dxaudio"}\n')
        output = root / "project"
        text, exit_code = prepare(package, output)
        assert_exit_code(0, f"a valid package should prepare cleanly: {text}", exit_code)
        manifest = json.loads((output / "Packages" / "manifest.json").read_text(encoding="utf-8"))
        dependencies = manifest["dependencies"]
        assert_true(
            dependencies["com.wallstop-studios.dxaudio"] == f"file:{package.resolve().as_posix()}",
            f"the package should be a file reference, got {dependencies}",
        )
        assert_true(dependencies["com.unity.modules.audio"] == "1.0.0", "the audio module must be present")
        assert_true("com.unity.test-framework" in dependencies, "the test framework must be present")
        assert_true(manifest["testables"] == ["com.wallstop-studios.dxaudio"], "the package must be testable")
        version = (output / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8")
        assert_true(version.startswith("m_EditorVersion: 6000.6.7f1"), f"the version file is wrong: {version}")
    finally:
        remove_temp_root(root)


def case_rejects_a_directory_that_is_not_a_package() -> None:
    root = new_temp_root(prefix="ci-prepare-bad-")
    try:
        text, exit_code = prepare(root, root / "project")
        assert_true(exit_code != 0, "a directory without package.json must fail")
        assert_true("package.json" in text, f"the failure should name package.json, got: {text}")
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== CI project preparer tests ==")
    run_case("Writes_A_Manifest_That_References_The_Package", case_writes_a_manifest_that_references_the_package)
    run_case("Rejects_A_Directory_That_Is_Not_A_Package", case_rejects_a_directory_that_is_not_a_package)
    failures = failure_count()
    print(f"== CI project preparer: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
