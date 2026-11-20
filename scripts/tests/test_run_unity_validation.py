#!/usr/bin/env python3
"""Direct Unity validation runner tests (typed Python port of ``test-run-unity-validation.ps1``, issue #171).

Every case from the PowerShell original is kept with the same assertions;
only the driver language changed. The runner is driven as a child process:
a Python child costs a few milliseconds to start, so the in-process
invocation the PowerShell original needed to dodge expensive pwsh spawns
is no longer required, and the real CLI contract is exercised instead.
"""

from __future__ import annotations

import contextlib
import io
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Sequence
from unittest.mock import patch

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_true,
    failure_count,
    new_temp_root,
    remove_temp_root,
    run_case,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
RUNNER = REPO_ROOT / "scripts" / "run_unity_validation.py"
sys.path.insert(0, str(RUNNER.parent))


def new_unity_project_fixture(root: Path, version: str = "6000.6.7f1") -> None:
    (root / "Assets").mkdir(parents=True, exist_ok=True)
    write_fixture_file(
        root,
        "ProjectSettings/ProjectVersion.txt",
        f"m_EditorVersion: {version}\nm_EditorVersionWithRevision: {version} (fixture)\n",
    )


def invoke_runner(
    project: Path,
    output: Path,
    version: str = "6000.6.7f1",
    mode: str = "editmode",
    plan_only: bool = False,
) -> int:
    """Run the validation runner as a child and return its exit code.

    sys.executable is a deterministic non-Unity executable for the child:
    it starts successfully but rejects Unity's command-line switches.
    """
    arguments: Sequence[str] = [
        sys.executable,
        str(RUNNER),
        "--host-project", str(project),
        "--unity-executable", sys.executable,
        "--unity-version", version,
        "--suite", mode,
        "--output-directory", str(output),
    ]
    if plan_only:
        arguments = [*arguments, "--plan-only"]
    completed = subprocess.run(arguments, capture_output=True, text=True, check=False)
    return completed.returncode


def case_writes_explicit_plan_when_inputs_are_valid() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "Host Project"
        output = root / "Evidence Output"
        new_unity_project_fixture(project)

        exit_code = invoke_runner(project, output, mode="playmode", plan_only=True)
        assert_true(exit_code == 0, f"plan-only invocation should pass, got exit {exit_code}")

        manifest_path = output / "dxaudio-validation.json"
        assert_true(manifest_path.is_file(), "plan manifest should exist")
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        assert_true(manifest["status"] == "planned", "plan status should be planned")
        assert_true(manifest["configuration"]["suite"] == "playmode", "suite should be recorded")
        assert_true("-runTests" in manifest["command"], "Unity test argument should be present")
        assert_true(str(project) in manifest["command"], "path containing spaces should be one argument")
        assert_true(
            manifest["artifacts"]["testResults"].endswith("playmode-results.xml"),
            "result path should match the suite",
        )
    finally:
        remove_temp_root(root)


def case_rejects_unity_version_mismatch_before_writing_evidence() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "HostProject"
        output = root / "Output"
        new_unity_project_fixture(project, version="6000.5.2f1")

        exit_code = invoke_runner(project, output, plan_only=True)
        assert_true(exit_code == 2, f"version mismatch should exit 2, got {exit_code}")
        assert_true(not output.exists(), "invalid run should not create output")
    finally:
        remove_temp_root(root)


def case_rejects_output_inside_package_repository() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "HostProject"
        new_unity_project_fixture(project)
        inside_repo = REPO_ROOT / "Temp/forbidden-unity-validation-output"

        exit_code = invoke_runner(project, inside_repo, plan_only=True)
        assert_true(exit_code == 2, f"in-repository output should exit 2, got {exit_code}")
        assert_true(not inside_repo.exists(), "rejected output should not be created")
    finally:
        remove_temp_root(root)


def case_rejects_unsupported_suite_at_parameter_binding() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "HostProject"
        output = root / "Output"
        new_unity_project_fixture(project)

        exit_code = invoke_runner(project, output, mode="standalone", plan_only=True)
        assert_true(exit_code != 0, "unsupported suite should fail")
        assert_true(not output.exists(), "invalid suite should not create output")
    finally:
        remove_temp_root(root)


def case_preserves_process_failure_exit_code_and_records_failure() -> None:
    root = new_temp_root(prefix="unity-runner-")
    try:
        project = root / "HostProject"
        output = root / "Output"
        new_unity_project_fixture(project)

        # sys.executable is a deterministic non-Unity executable: it starts
        # successfully but rejects Unity's command-line switches, exercising
        # child-process exit propagation.
        exit_code = invoke_runner(project, output)
        assert_true(exit_code != 0, "failed child process should return a nonzero exit code")

        manifest = json.loads((output / "dxaudio-validation.json").read_text(encoding="utf-8"))
        assert_true(manifest["status"] == "failed", "process failure should be recorded")
        assert_true(manifest["exitCode"] == exit_code, "child exit code should be preserved")
        assert_true(
            str(manifest["completedAtUtc"]).strip() != "",
            "completion time should be recorded",
        )
    finally:
        remove_temp_root(root)


def case_writes_the_compiler_policy_before_unity_starts() -> None:
    """The package carries no compiler policy, so a live run must write it (issue #40)."""
    root = new_temp_root(prefix="unity-runner-policy-")
    outside = new_temp_root(prefix="unity-runner-policy-output-")
    try:
        # A copy of scripts/ is its own package root, so the case writes its
        # generated files there instead of into the developer's checkout.
        scripts = root / "scripts"
        shutil.copytree(REPO_ROOT / "scripts", scripts, ignore=shutil.ignore_patterns("__pycache__", "tests"))
        assembly = scripts / "Runtime"
        assembly.mkdir()
        write_fixture_file(scripts, "Runtime/WallstopStudios.DxAudio.asmdef", "{}\n")
        project = root / "HostProject"
        new_unity_project_fixture(project)
        runner = scripts / "run_unity_validation.py"
        arguments: Sequence[str] = [
            sys.executable,
            str(runner),
            "--host-project", str(project),
            "--unity-executable", sys.executable,
            "--unity-version", "6000.6.7f1",
            "--suite", "editmode",
        ]

        planned = subprocess.run(
            [*arguments, "--output-directory", str(outside / "Planned"), "--plan-only"],
            capture_output=True,
            text=True,
            check=False,
        )
        assert_true(planned.returncode == 0, f"the plan must pass: {planned.stdout}{planned.stderr}")
        assert_true(
            "compiler policy" not in planned.stdout,
            "a plan-only run compiles nothing and must write no policy",
        )
        assert_true(not (assembly / "csc.rsp").exists(), "a plan-only run must leave the assembly directory alone")

        live = subprocess.run(
            [*arguments, "--output-directory", str(outside / "Live")],
            capture_output=True,
            text=True,
            check=False,
        )
        assert_true("Wrote 2 compiler policy file(s)" in live.stdout, f"the run must write the policy: {live.stdout}")
        assert_true((assembly / "csc.rsp").is_file(), "the live run must write the response file")
        assert_true((assembly / "WarningsAsErrors.ruleset").is_file(), "the live run must write the ruleset")
    finally:
        remove_temp_root(root)
        remove_temp_root(outside)


def case_container_runs_the_pinned_editor_and_reports_its_result() -> None:
    from unity_tests import main as unity_tests

    endpoint = "http://bridge.example:9021/mcp"
    env = {
        "DXAUDIO_IN_CONTAINER": "1",
        "UNITY_MCP_HTTP_URL": endpoint + "/",
        "UNITY_MCP_PROJECT": r"c:\code\dxaudio",
        "UNITY_MCP_TOKEN": "fixture-token",
        "UNITY_MCP_TIMEOUT_MS": "180000.0",
    }
    for project, failed, expected_calls in [
        ("C:/Code/DxAudio", 0, 4),
        ("C:/Code/DxAudio", 1, 4),
        ("C:/Code/Other", 0, 3),
    ]:
        editor = {"status": "ready", "projectPath": project, "playMode": "stopped"}
        result = [
            {"fullName": "Fixture.First", "status": "Passed"},
            {"fullName": "Fixture.Second", "status": "Failed" if failed else "Passed", "message": "Planted failure"},
            {"fullName": "Fixture.Third", "status": "Skipped"},
            {"fullName": "Fixture.Fourth", "status": "Inconclusive"},
        ]
        output = io.StringIO()
        with (
            patch.dict(os.environ, env, clear=True),
            patch("unity_tests.read_env_file", return_value={}),
            patch("unity_tests.request_json", side_effect=[
                {"result": {"serverInfo": {"name": "fixture"}}},
                {},
                {"result": {"content": [{"text": json.dumps(editor)}]}},
                {"result": {"content": [{"text": json.dumps(result)}]}},
            ]) as request,
            contextlib.redirect_stdout(output),
            contextlib.redirect_stderr(output),
        ):
            code = unity_tests([])
        assert_true(request.call_count == expected_calls, str(request.call_args_list))
        assert_true(all(call.args[0] == endpoint for call in request.call_args_list), "wrong MCP endpoint")
        assert_true(request.call_args.args[1]["Authorization"] == "Bearer fixture-token", "missing token")
        if expected_calls == 4:
            arguments = request.call_args.args[2]["params"]
            assert_true(arguments["name"] == "test_run" and arguments["arguments"]["mode"] == "editor", str(arguments))
            assert_true(code == failed, f"wrong test exit code: {code}")
            summary = f"{2 - failed}/4 passed, {failed} failed, 1 skipped, 1 inconclusive"
            assert_true(summary in output.getvalue(), output.getvalue())
            if failed:
                assert_true("Fixture.Second: Planted failure" in output.getvalue(), output.getvalue())
        else:
            assert_true(code != 0, "a project mismatch must fail")
            assert_true(endpoint in output.getvalue() and "UNITY_MCP_PROJECT" in output.getvalue(), output.getvalue())

    output = io.StringIO()
    with (
        patch.dict(os.environ, env, clear=True),
        patch("unity_tests.read_env_file", return_value={}),
        patch("unity_tests.request_json", side_effect=OSError("Rejected fixture-token")),
        contextlib.redirect_stderr(output),
    ):
        code = unity_tests([])
    diagnostic = output.getvalue()
    assert_true(code != 0 and endpoint in diagnostic and "npm run unity:mcp:doctor" in diagnostic, diagnostic)
    assert_true("fixture-token" not in diagnostic, "the bridge diagnostic exposed a token")

    output = io.StringIO()
    unpinned_env = {key: value for key, value in env.items() if key != "UNITY_MCP_PROJECT"}
    with (
        patch.dict(os.environ, unpinned_env, clear=True),
        patch("unity_tests.read_env_file", return_value={}),
        patch("unity_tests.request_json") as request,
        contextlib.redirect_stderr(output),
    ):
        code = unity_tests([])
    request.assert_not_called()
    assert_true(code != 0 and "Set UNITY_MCP_PROJECT" in output.getvalue(), output.getvalue())


def case_batchmode_preserves_the_local_runner_and_output() -> None:
    from unity_tests import main as unity_tests

    root = new_temp_root(prefix="unity-wrapper-")
    try:
        new_unity_project_fixture(root)
        output = root / "evidence"
        for container, mode, options in [
            ("0", "editmode", []),
            ("1", "playmode", ["--suite", "playmode"]),
            ("1", "editmode", ["--output-directory", str(output)]),
        ]:
            env = {
                "DXAUDIO_IN_CONTAINER": container,
                "UNITY_MCP_PROJECT": str(root),
                "UNITY_EDITOR_PATH": sys.executable,
            }
            with (
                patch.dict(os.environ, env, clear=True),
                patch("unity_tests.DEFAULT_OUTPUT", output),
                patch("unity_tests.read_env_file", return_value={}),
                patch("unity_tests.request_json") as bridge,
                patch("run_unity_validation.run", return_value=0) as runner,
                contextlib.redirect_stdout(io.StringIO()),
            ):
                code = unity_tests(options)
            assert_true(code == 0, f"expected batchmode {mode} to pass")
            bridge.assert_not_called()
            runner.assert_called_once()
            namespace = runner.call_args.args[0]
            assert_true(namespace.suite == mode and namespace.output_directory == str(output), str(namespace))
    finally:
        remove_temp_root(root)


def main() -> int:
    print("== direct Unity validation runner tests ==")

    run_case("Writes_ExplicitPlan_When_InputsAreValid", case_writes_explicit_plan_when_inputs_are_valid)
    run_case("Rejects_UnityVersionMismatch_BeforeWritingEvidence", case_rejects_unity_version_mismatch_before_writing_evidence)
    run_case("Rejects_OutputInsidePackageRepository", case_rejects_output_inside_package_repository)
    run_case("Rejects_UnsupportedSuite_AtParameterBinding", case_rejects_unsupported_suite_at_parameter_binding)
    run_case("Preserves_ProcessFailureExitCode_AndRecordsFailure", case_preserves_process_failure_exit_code_and_records_failure)
    run_case("Writes_CompilerPolicy_BeforeUnityStarts", case_writes_the_compiler_policy_before_unity_starts)
    run_case("Container_RunsThePinnedEditor_AndReportsItsResult", case_container_runs_the_pinned_editor_and_reports_its_result)
    run_case("Batchmode_PreservesLocalRunnerAndOutput", case_batchmode_preserves_the_local_runner_and_output)

    failures = failure_count()
    print(f"== direct Unity validation runner: {failures} failure(s) ==")
    if failures > 0:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
