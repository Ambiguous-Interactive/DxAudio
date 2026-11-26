#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Validate DxAudio in a disposable Unity project.

This is the typed-Python replacement for the Windows-only validation
scripts (issue #120). It creates a minimal Unity project that references a
staged package, runs the EditMode suite, and validates the exact result
shape. The optional DxMessaging integration is a second state of the same
job, selected with ``--dxmessaging-version`` and kept apart by ``--label``.

The ``tail`` command prints a redacted log tail and the failed NUnit cases
for a run. It is safe to call from a ``failure()`` step, which a regular
step cannot do because a timed-out step never reaches its own handler.

Examples::

    uv run --no-project scripts/unity/unity_validation.py run
    uv run --no-project scripts/unity/unity_validation.py run --archive-smoke
    uv run --no-project scripts/unity/unity_validation.py tail
"""

from __future__ import annotations

import argparse
import contextlib
import html
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING, cast

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

UNITY_VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+f\d+$")
LABEL_PATTERN = re.compile(r"^[A-Za-z0-9-]+$")
COUNT_PATTERN = re.compile(r"^[0-9]{1,9}$")
MAX_RESULT_BYTES = 64 * 1024 * 1024
COMPLETION_PATTERN = re.compile(
    r"^Test run completed\. Exiting with code (?P<code>[0-9]{1,3})(?:[ .(][^\r\n]*)?$"
)
ANALYZER_FAILURE_PATTERN = re.compile(r"\b(?:AD0001|AD0002|CS8032|CS8034|CS9057)\b")
ACTIVATION_TRANSIENT_PATTERN = re.compile(
    r"(?im)Licensing::[^\r\n]*(?:\b20111\b|maximum number of activations)"
)
ACTIVATION_PERMANENT_PATTERN = re.compile(r"(?im)Licensing::[^\r\n]*(?:\b20113\b|serial expired)")
STARTUP_PATTERN = re.compile(
    r"(?m)^(?:Application\.AssetDatabase Initial Refresh Start|\[ScriptCompilation\]|"
    r"Test run completed\.|DxAudio .*validation passed\.)"
)
PRIVATE_KEY_PATTERN = re.compile(
    r"(?m)-----BEGIN [^\r\n-]*PRIVATE KEY-----\r?\n"
    r"(?:[A-Za-z0-9+/=]+\r?\n)*"
    r"(?:-----END [^\r\n-]*PRIVATE KEY-----|[A-Za-z0-9+/=]+$)?"
)
CONTROL_PATTERN = re.compile(r"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]|\r(?!\n)")
WHITESPACE_PATTERN = re.compile(r"\s+")
TOKEN_PATTERN = re.compile(
    r"(?i)\b(?:SC-[A-Za-z0-9-]{8,}|gh[pousr]_[A-Za-z0-9_]{16,}|"
    r"github_pat_[A-Za-z0-9_]{16,}|(?:AKIA|ASIA)[A-Z0-9]{16})\b"
)
LICENSE_ID_PATTERN = re.compile(r'(?i)(<License\s+id=")[^"]+(")')
BEARER_PATTERN = re.compile(
    r"(?i)(\b(?:Authorization:\s*(?:Bearer|Basic)|Successfully updated the access token|"
    r"Access token\s*[:=])[^\S\r\n]*[\"']?)[A-Za-z0-9_+/=.\-]{5,}"
)
ASSIGNMENT_PATTERN = re.compile(
    r"(?i)(\b(?:UNITY_SERIAL|UNITY_EMAIL|UNITY_PASSWORD|GITHUB_TOKEN|API_KEY)\s*=|"
    r"-(?:serial|username|password)\s+)(?:\"[^\"\r\n]*\"|'[^'\r\n]*'|[^\s\[\]<>]+)"
)
MEASUREMENTS: tuple[tuple[str, str, str, bool], ...] = (
    (
        "DspHostTests.RecordDspRenderWallTime(",
        "DxAudio DSP render: ",
        "DSP render timings:",
        False,
    ),
    (
        "DspDistortionTimingTests.RecordDistortionStageCosts",
        "DxAudio Distortion timing: ",
        "Distortion stage timings:",
        False,
    ),
    (
        "DspDistortionTimingTests.RecordDistortionStageCosts",
        "DxAudio Distortion samples: ",
        "",
        True,
    ),
    (
        "AllocationTests.MixMonitor",
        "Mix Monitor ",
        "Mix Monitor timings",
        False,
    ),
)
ENVIRONMENT_WARNINGS: tuple[tuple[str, str], ...] = (
    (
        "while verifying Licensing Client signature",
        "Licensing client signature validation failed.",
    ),
    (
        "LicensingClient has failed validation",
        "Licensing client was ignored after failing validation.",
    ),
    (
        "failed because channel doesn't exist",
        "Licensing IPC channel was absent at connection attempt.",
    ),
)


class ValidationFailure(Exception):
    """A rejected validation run."""


@dataclass
class ProcessObservation:
    process_id: int | None = None
    origin: str = "launch-failed"
    raw_exit_code: int | None = None
    exit_code: int | None = None
    completion_exit_code: int | None = None
    started_utc: datetime = field(default_factory=lambda: datetime.now(UTC))
    ended_utc: datetime | None = None

    def as_json(self) -> dict[str, object]:
        return {
            "ProcessId": self.process_id,
            "StartedUtc": _utc_text(self.started_utc),
            "EndedUtc": None if self.ended_utc is None else _utc_text(self.ended_utc),
            "Origin": self.origin,
            "RawExitCode": self.raw_exit_code,
            "ExitCode": self.exit_code,
            "CompletionExitCode": self.completion_exit_code,
        }


def _utc_text(value: datetime) -> str:
    return value.isoformat(timespec="seconds").replace("+00:00", "Z")


def run_name(unity_version: str, label: str) -> str:
    """Return the per-run directory name, rejecting unsafe labels."""
    if not label or not label.strip():
        return unity_version
    if LABEL_PATTERN.fullmatch(label) is None:
        raise ValidationFailure(f"Label must be empty or match ^[A-Za-z0-9-]+$; got '{label}'.")
    return f"{unity_version}-{label}"


def safe_report(text: str) -> str:
    """Redact credentials and control bytes before any text reaches a job log."""
    secrets = [
        value
        for value in (
            os.environ.get("UNITY_SERIAL", ""),
            os.environ.get("UNITY_EMAIL", ""),
            os.environ.get("UNITY_PASSWORD", ""),
        )
        if value
    ]
    result = text
    for secret in sorted(secrets, key=len, reverse=True):
        result = result.replace(secret, "[REDACTED]")
    result = PRIVATE_KEY_PATTERN.sub("[REDACTED]", result)
    result = LICENSE_ID_PATTERN.sub(r"\1[REDACTED]\2", result)
    result = BEARER_PATTERN.sub(r"\1[REDACTED]", result)
    result = ASSIGNMENT_PATTERN.sub(r"\1[REDACTED]", result)
    result = TOKEN_PATTERN.sub("[REDACTED]", result)
    return CONTROL_PATTERN.sub("?", result)


def write_redacted_tail(path: Path, max_lines: int = 40) -> None:
    """Print a redacted tail, sanitizing before choosing the visible lines."""
    if not path or not path.is_file():
        print("  | (no log file)", flush=True)
        return
    try:
        raw = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        print("  | (no log file)", flush=True)
        return
    lines = safe_report(raw).splitlines()
    for line in lines[-max_lines:]:
        print(f"  | {line}", flush=True)


def _has_leading_doctype(text: str) -> bool:
    """Return true only when a DTD declaration precedes the root element."""
    prefix = text.lstrip("\ufeff \t\r\n")
    while True:
        if prefix.startswith("<?"):
            end = prefix.find("?>")
            if end >= 0:
                end += 2
        elif prefix.startswith("<!--"):
            end = prefix.find("-->")
            if end >= 0:
                end += 3
        else:
            break
        if end < 0:
            return True
        prefix = prefix[end:].lstrip("\ufeff \t\r\n")
    return prefix.upper().startswith(("<!DOCTYPE", "<!ENTITY"))


def parse_nunit_result_from_text(text: str) -> ET.Element:
    """Parse one NUnit result, refusing a DTD or an external entity."""
    if len(text) - MAX_RESULT_BYTES > 0:
        raise ValidationFailure("Unity result XML exceeds the 64 MiB limit.")
    if _has_leading_doctype(text):
        raise ValidationFailure("Unity result XML uses a DTD; DtdProcessing is prohibited.")
    try:
        return ET.fromstring(text)  # noqa: S314 - trusted editor output; leading DTD and entities are refused above.
    except ET.ParseError as error:
        raise ValidationFailure(f"Unity result XML could not be read: {error}") from error


def parse_nunit_result(path: Path) -> ET.Element:
    """Read an NUnit result file, refusing a DTD or an external entity."""
    try:
        size = path.stat().st_size
    except OSError as error:
        raise ValidationFailure(f"Unity result XML could not be read: {error}") from error
    if size - MAX_RESULT_BYTES > 0:
        raise ValidationFailure("Unity result XML exceeds the 64 MiB limit.")
    return parse_nunit_result_from_text(path.read_text(encoding="utf-8", errors="replace"))


def assert_passing_nunit_result(root: ET.Element) -> int:
    """Validate that one NUnit test-run accounts for every case and passed."""
    if root.tag != "test-run" or root.get("result") != "Passed":
        raise ValidationFailure("Unity did not report a passing EditMode run.")
    counts: dict[str, int] = {}
    for name in ("total", "passed", "failed", "inconclusive", "skipped"):
        value = root.get(name, "")
        if COUNT_PATTERN.fullmatch(value) is None:
            raise ValidationFailure(f"Unity EditMode result has an invalid {name} count.")
        counts[name] = int(value)
    cases = root.findall(".//test-case")
    passed_cases = [case for case in cases if case.get("result") == "Passed"]
    skipped_cases = [case for case in cases if case.get("result") == "Skipped"]
    if (
        counts["passed"] == 0
        or counts["failed"] != 0
        or counts["inconclusive"] != 0
        or counts["total"] != counts["passed"] + counts["skipped"]
        or len(cases) != counts["total"]
        or len(passed_cases) != counts["passed"]
        or len(skipped_cases) != counts["skipped"]
    ):
        raise ValidationFailure(
            "Unity EditMode result does not account for each test in the passing run."
        )
    return counts["passed"]


def _duration(node: ET.Element, label: str) -> float | None:
    value = node.get("duration", "")
    if not value and node.get("result") in ("Skipped", "NotRun"):
        return None
    try:
        seconds = float(value)
    except ValueError as error:
        raise ValidationFailure(
            f"Unity timing report has a missing or invalid duration among its {label}."
        ) from error
    if seconds != seconds or seconds in (float("inf"), float("-inf")) or seconds < 0:
        raise ValidationFailure(
            f"Unity timing report has a missing or invalid duration among its {label}."
        )
    return seconds


def write_nunit_timings(root: ET.Element) -> None:
    """Print the slowest fixtures and cases, scoped measurements, and a summary."""
    if root.tag != "test-run":
        raise ValidationFailure("Unity timing report requires an NUnit test-run.")
    lines: list[str] = []
    warnings: list[str] = []
    groups: tuple[tuple[str, str, float], ...] = (
        ("fixtures", './/test-suite[@type="TestFixture"]', 120.0),
        ("test cases", ".//test-case", 0.0),
    )
    for label, xpath, budget in groups:
        timings: list[tuple[str, float]] = []
        for node in root.findall(xpath):
            seconds = _duration(node, label)
            if seconds is None:
                continue
            name = node.get("fullname") or node.get("name") or ""
            timings.append((name, seconds))
            if 0 < budget < seconds:
                safe_name = WHITESPACE_PATTERN.sub(" ", safe_report(name))
                warnings.append(
                    f"Fixture {safe_name} took {seconds:.3f}s, over the {budget:.0f}s budget."
                )
        timings.sort(key=lambda item: (-item[1], item[0]))
        lines.append(f"Unity EditMode slowest {label}:")
        for name, seconds in timings[:30]:
            safe_name = WHITESPACE_PATTERN.sub(" ", safe_report(name))
            lines.append(f"  | {seconds:.3f}s  {safe_name}")

    for test_suffix, prefix, label, log_only in MEASUREMENTS:
        test_prefix = f"WallstopStudios.DxAudio.Tests.Editor.{test_suffix}"
        measurement_lines: list[str] = []
        for case in root.findall(".//test-case"):
            if not case.get("fullname", "").startswith(test_prefix):
                continue
            for output in case.findall("output"):
                text = safe_report("".join(output.itertext()))
                measurement_lines.extend(
                    f"  | {line}" for line in text.splitlines() if line.startswith(prefix)
                )
        if not measurement_lines:
            continue
        if log_only:
            for line in measurement_lines:
                print(line, flush=True)
        else:
            lines.append(f"Unity EditMode {label}")
            lines.extend(measurement_lines)

    for line in lines:
        print(line, flush=True)
    for warning in warnings:
        print(f"::warning::{warning.replace('%', '%25')}", flush=True)
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY", "")
    if summary_path:
        safe_html = html.escape("\n".join(lines))
        with Path(summary_path).open("a", encoding="utf-8", newline="") as summary:
            summary.write(f"\n### Unity EditMode timings\n\n<pre>{safe_html}</pre>\n")


def write_environment_warnings(path: Path) -> None:
    """Report known licensing-client noise without failing a green run."""
    if not path.is_file():
        print("Unity environment warnings: nothing was scanned.", flush=True)
        return
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    matches = 0
    for pattern, message in ENVIRONMENT_WARNINGS:
        count = sum(1 for line in lines if pattern in line)
        if count == 0:
            continue
        matches += count
        print(
            f"::warning::{message} Observed {count} time(s). "
            "License cleanup is checked separately.",
            flush=True,
        )
    print(
        f"Unity environment warnings: scanned {len(lines)} log line(s), {matches} match(es).",
        flush=True,
    )


def read_log_delta(path: Path, offset: int) -> tuple[str, int]:
    """Read only new bytes, sharing Unity's open log until the editor exits."""
    if not path.exists():
        return "", offset
    try:
        with path.open("rb") as stream:
            length = stream.seek(0, os.SEEK_END)
            if length < offset:
                offset = 0
            stream.seek(offset, os.SEEK_SET)
            data = stream.read()
            return data.decode("utf-8", errors="replace"), stream.tell()
    except OSError:
        return "", offset


def is_transient_activation(log_path: Path, results_path: Path) -> bool:
    """Return true only for the seat handoff Unity can recover from."""
    if results_path.exists() or not log_path.exists():
        return False
    log = log_path.read_text(encoding="utf-8", errors="replace")
    if STARTUP_PATTERN.search(log) is not None:
        return False
    if ACTIVATION_PERMANENT_PATTERN.search(log) is not None:
        return False
    return ACTIVATION_TRANSIENT_PATTERN.search(log) is not None


def _kill_tree(process: subprocess.Popen[bytes]) -> None:
    if sys.platform == "win32":
        subprocess.run(  # noqa: S603 - fixed system command with numeric process ID.
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],  # noqa: S607 - Windows system utility.
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        return
    with contextlib.suppress(ProcessLookupError):
        os.killpg(os.getpgid(process.pid), signal.SIGKILL)


def invoke_unity(
    command: Sequence[str],
    environment: dict[str, str],
    log_path: Path,
    observation_path: Path,
    timeout_seconds: float,
    stall_seconds: int,
    completion_grace_seconds: int,
    watch_test_completion: bool,
) -> ProcessObservation:
    """Run Unity with a wall, stall, and completion watchdog."""
    observation = ProcessObservation()
    process: subprocess.Popen[bytes] | None = None
    started = time.monotonic()
    try:
        creation_flags = 0
        if sys.platform == "win32":
            creation_flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
        process = subprocess.Popen(  # noqa: S603 - command is assembled from validated inputs.
            list(command),
            env=environment,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            creationflags=creation_flags,
            start_new_session=sys.platform != "win32",
        )
        observation.process_id = process.pid
        observation.origin = "process-exit"
        last_output = 0.0
        completed_time = 0.0
        offset = 0
        observed_length = 0
        partial_line = ""
        while process.poll() is None:
            elapsed = time.monotonic() - started
            if watch_test_completion and observation.completion_exit_code is None:
                try:
                    length = log_path.stat().st_size
                except OSError:
                    length = observed_length
                if length != observed_length:
                    observed_length = length
                    last_output = elapsed
                delta, offset = read_log_delta(log_path, offset)
                if delta:
                    last_output = elapsed
                    buffer = partial_line + delta
                    end = buffer.rfind("\n")
                    if end >= 0:
                        complete = buffer[: end + 1]
                        partial_line = buffer[end + 1 :]
                        for line in complete.splitlines():
                            match = COMPLETION_PATTERN.fullmatch(line.rstrip("\r"))
                            if match is not None:
                                observation.completion_exit_code = int(match.group("code"))
                                completed_time = elapsed
                                break
                    else:
                        partial_line = buffer
            origin: str | None = None
            if timeout_seconds <= elapsed:
                origin = "wall-timeout"
            elif observation.completion_exit_code is not None and completion_grace_seconds <= (
                elapsed - completed_time
            ):
                origin = "completion-grace"
            elif (
                watch_test_completion
                and observation.completion_exit_code is None
                and 0 < stall_seconds <= (elapsed - last_output)
            ):
                origin = "stall-timeout"
            if origin is None:
                time.sleep(0.25)
                continue
            if process.poll() is not None:
                break
            _kill_tree(process)
            observation.origin = origin
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired as error:
                raise ValidationFailure(
                    "Unity did not stop after the watchdog terminated its process tree."
                ) from error
            break
        observation.raw_exit_code = process.wait()
        if observation.origin == "wall-timeout":
            observation.exit_code = 124
        elif observation.origin == "stall-timeout":
            observation.exit_code = 125
        elif observation.origin == "completion-grace":
            observation.exit_code = observation.completion_exit_code
        else:
            observation.exit_code = observation.raw_exit_code
        return observation
    finally:
        if process is not None:
            try:
                if process.poll() is None:
                    _kill_tree(process)
                    process.wait(timeout=5)
            except (OSError, subprocess.TimeoutExpired):
                pass
        observation.ended_utc = datetime.now(UTC)
        observation_path.parent.mkdir(parents=True, exist_ok=True)
        observation_path.write_text(
            json.dumps(observation.as_json(), indent=2) + "\n", encoding="utf-8"
        )


def _write_json(path: Path, value: dict[str, object]) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def _manifest_dependencies(dxmessaging_version: str) -> dict[str, object]:
    manifest: dict[str, object] = {
        "dependencies": {
            "com.unity.modules.audio": "1.0.0",
            "com.unity.modules.imgui": "1.0.0",
        }
    }
    if dxmessaging_version:
        manifest["scopedRegistries"] = [
            {
                "name": "npmjs",
                "url": "https://registry.npmjs.org",
                "scopes": ["com.wallstop-studios"],
            }
        ]
    return manifest


def _check_receipt(log_path: Path, receipt: str, import_smoke: bool) -> None:
    lines = log_path.read_text(encoding="utf-8", errors="replace").splitlines()
    if any(line == receipt for line in lines):
        return
    write_redacted_tail(log_path)
    if import_smoke:
        raise ValidationFailure(
            f"Unity did not emit the expected import validation receipt: {receipt}"
        )
    raise ValidationFailure(
        f"Unity did not emit the expected player script validation receipt: {receipt}"
    )


def _validate_results(
    results_path: Path,
    dxmessaging_version: str,
    docs_path: Path,
    project_path: Path,
    run_label: str,
) -> None:
    root = parse_nunit_result(results_path)
    passed = assert_passing_nunit_result(root)
    scope = "WallstopStudios.DxAudio.Tests.Editor.AnalyzerActivation."
    for test in (
        "AllExpectedAssembliesUseThePinnedAnalyzers",
        "AssemblyDefinitionsMatchTheCompilerReferenceGraph",
        "EditorOnlyAssembliesContainNoConcreteMonoBehaviours",
    ):
        evidence = [
            case for case in root.findall(".//test-case") if case.get("fullname") == scope + test
        ]
        if len(evidence) != 1 or evidence[0].get("result") != "Passed":
            raise ValidationFailure(f"Unity did not report a passing {test} control.")
    integration_suites = [
        suite
        for suite in root.findall(".//test-suite")
        if suite.get("type") == "Assembly"
        and suite.get("name")
        in (
            "WallstopStudios.DxAudio.Tests.Integration.DxMessaging.dll",
            "WallstopStudios.DxAudio.Tests.Integration.DxMessaging",
        )
    ]
    expect_integration = bool(dxmessaging_version)
    if len(integration_suites) != int(expect_integration):
        raise ValidationFailure(
            "The EditMode results do not match the expected DxMessaging test state."
        )
    if expect_integration:
        suite = integration_suites[0]
        reported = suite.get("passed", "")
        actual = sum(1 for case in suite.findall(".//test-case") if case.get("result") == "Passed")
        if COUNT_PATTERN.fullmatch(reported) is None or actual <= 0 or int(reported) != actual:
            raise ValidationFailure(
                "The EditMode results do not match the expected DxMessaging test state. "
                "The suite must account for its passing test cases."
            )
    assembly_path = docs_path / "DocSnippets.asmdef"
    try:
        definition = json.loads(assembly_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValidationFailure(
            f"Unity documentation assembly definition could not be read: {error}"
        ) from error
    definition_object: object = definition
    if not isinstance(definition_object, dict):
        raise ValidationFailure("Unity documentation assembly definition has no name.")
    definition_map = cast("dict[str, object]", definition_object)
    name = definition_map.get("name")
    if not isinstance(name, str) or not name:
        raise ValidationFailure("Unity documentation assembly definition has no name.")
    docs_output = project_path / "Library" / "ScriptAssemblies" / f"{name}.dll"
    if not docs_output.is_file():
        raise ValidationFailure("Unity did not compile the documentation examples.")
    print("Unity compiled the documentation examples.", flush=True)
    print(f"EditMode passed {passed} tests on {run_label}.", flush=True)
    write_nunit_timings(root)


def _run_smoke(
    options: argparse.Namespace,
    package_root: Path,
    assets_path: Path,
    packages_path: Path,
    manifest: dict[str, object],
) -> tuple[list[str], str, float]:
    """Stage an archive or import smoke and return its Unity arguments."""
    player_smoke = options.archive_smoke or options.integration_player_smoke
    import_archive = options.unity_import_archive or ""
    if import_archive:
        if player_smoke or not Path(import_archive).is_file():
            raise ValidationFailure("Unity import validation requires its own archive and project.")
        editor_path = assets_path / "Editor"
        editor_path.mkdir(parents=True, exist_ok=True)
        source = package_root / ".net-tests/UnityValidation/UnityPackageSmoke.cs"
        (editor_path / source.name).write_bytes(source.read_bytes())
        return (
            ["-executeMethod", "WallstopStudios.DxAudio.Validation.UnityPackageSmoke.Run"],
            "DxAudio Unity import validation passed.",
            float(min(options.editor_timeout_seconds, 300)),
        )
    consumer_archive = options.consumer_archive or ""
    if player_smoke:
        if not consumer_archive or not Path(consumer_archive).is_file():
            raise ValidationFailure("The pristine consumer tarball is missing.")
        if options.archive_smoke and (
            options.integration_player_smoke or options.dxmessaging_version
        ):
            raise ValidationFailure(
                "The consumer archive smoke requires the optional integration to be absent."
            )
        if options.integration_player_smoke and not options.dxmessaging_version:
            raise ValidationFailure("The integration player smoke requires a DxMessaging version.")
        (packages_path / "dxaudio.tgz").write_bytes(Path(consumer_archive).read_bytes())
        dependencies = manifest["dependencies"]
        if not isinstance(dependencies, dict):
            raise ValidationFailure("The package manifest has no dependency table.")
        dependencies["com.wallstop-studios.dxaudio"] = "file:dxaudio.tgz"
        editor_path = assets_path / "Editor"
        editor_path.mkdir(parents=True, exist_ok=True)
        source = package_root / ".net-tests/UnityValidation/ArchiveSmoke.cs"
        (editor_path / source.name).write_bytes(source.read_bytes())
        if options.integration_player_smoke:
            method = "WallstopStudios.DxAudio.Validation.ArchiveSmoke.RunWithIntegration"
            receipt = "DxAudio integration player script validation passed."
        else:
            method = "WallstopStudios.DxAudio.Validation.ArchiveSmoke.Run"
            receipt = "DxAudio archive and player script validation passed."
        return (
            ["-quit", "-executeMethod", method],
            receipt,
            float(min(options.editor_timeout_seconds, 420)),
        )
    return [], "", 0.0


def run(options: argparse.Namespace) -> int:
    """Create the disposable project and validate one Unity run."""
    unity_version = options.unity_version
    if UNITY_VERSION_PATTERN.fullmatch(unity_version) is None:
        raise ValidationFailure(
            "UnityVersion must be an exact editor release, for example 6000.6.7f1."
        )
    if not options.unity_editor_path:
        raise ValidationFailure("UnityEditorPath is required.")
    if not options.package_root:
        raise ValidationFailure("PackageRoot is required.")
    for name in ("UNITY_SERIAL", "UNITY_EMAIL", "UNITY_PASSWORD"):
        if not os.environ.get(name, "").strip():
            raise ValidationFailure(f"{name} is required.")
    runner_temp = os.environ.get("RUNNER_TEMP", "")
    if not runner_temp:
        raise ValidationFailure("RUNNER_TEMP is required.")

    run_label = run_name(unity_version, options.label)
    artifacts_path = Path(runner_temp) / f"dxaudio-results-{run_label}"
    if artifacts_path.exists():
        shutil.rmtree(artifacts_path)
    artifacts_path.mkdir(parents=True)
    results_path = artifacts_path / "editmode-results.xml"
    log_path = artifacts_path / "editmode.log"

    project_path = Path(runner_temp) / f"dxaudio-unity-{run_label}"
    if project_path.exists():
        shutil.rmtree(project_path)
    packages_path = project_path / "Packages"
    settings_path = project_path / "ProjectSettings"
    assets_path = project_path / "Assets"
    for path in (assets_path, packages_path, settings_path):
        path.mkdir(parents=True, exist_ok=True)

    package_root = Path(options.package_root).resolve()
    manifest = _manifest_dependencies(options.dxmessaging_version)
    player_smoke = options.archive_smoke or options.integration_player_smoke
    import_smoke = bool(options.unity_import_archive)
    if options.unity_import_archive or player_smoke:
        smoke_args, receipt, smoke_timeout = _run_smoke(
            options, package_root, assets_path, packages_path, manifest
        )
    else:
        smoke_args = []
        receipt = ""
        smoke_timeout = 0.0
        staged_package = project_path / "DxAudio"
        sample_path = assets_path / "BasicUsage"
        docs_path = assets_path / "DocSnippets"
        prepare_script = Path(__file__).resolve().parents[2] / "scripts/unity/prepare-package.py"
        prepare_args = [
            sys.executable,
            str(prepare_script),
            "--source",
            str(package_root),
            "--destination",
            str(staged_package),
            "--sample-destination",
            str(sample_path),
            "--docs-destination",
            str(docs_path),
        ]
        if options.dxmessaging_version:
            prepare_args.append("--with-integration")
        staged = subprocess.run(  # noqa: S603 - fixed interpreter and repository script.
            prepare_args,
            cwd=package_root,
            capture_output=True,
            text=True,
            errors="replace",
            check=False,
        )
        if staged.returncode != 0:
            raise ValidationFailure(
                "Could not stage the analysis package (exit code "
                f"{staged.returncode}).\n{staged.stdout}\n{staged.stderr}"
            )
        dependencies = manifest["dependencies"]
        if not isinstance(dependencies, dict):
            raise ValidationFailure("The package manifest has no dependency table.")
        dependencies["com.wallstop-studios.dxaudio"] = "file:../DxAudio"
        dependencies["com.unity.test-framework"] = (
            "1.1.33" if unity_version.startswith("2021.") else "1.4.6"
        )
        manifest["testables"] = ["com.wallstop-studios.dxaudio"]
    if options.dxmessaging_version:
        dependencies = manifest["dependencies"]
        if not isinstance(dependencies, dict):
            raise ValidationFailure("The package manifest has no dependency table.")
        dependencies["com.wallstop-studios.dxmessaging"] = options.dxmessaging_version
    _write_json(packages_path / "manifest.json", manifest)
    (settings_path / "ProjectVersion.txt").write_text(
        f"m_EditorVersion: {unity_version}\n", encoding="utf-8"
    )

    unity_args = [
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(project_path),
        "-logFile",
        str(log_path),
        "-serial",
        os.environ["UNITY_SERIAL"],
        "-username",
        os.environ["UNITY_EMAIL"],
        "-password",
        os.environ["UNITY_PASSWORD"],
    ]
    if import_smoke or player_smoke:
        unity_args.extend(smoke_args)
        editor_timeout = smoke_timeout
    else:
        unity_args.extend(
            ["-runTests", "-testPlatform", "EditMode", "-testResults", str(results_path)]
        )
        editor_timeout = float(options.editor_timeout_seconds)

    editor_path = Path(options.unity_editor_path)
    if editor_path.suffix.lower() == ".py":
        command = [sys.executable, str(editor_path), *unity_args]
    elif editor_path.suffix.lower() == ".ps1":
        command = ["pwsh", "-NoProfile", "-NonInteractive", "-File", str(editor_path), *unity_args]
    else:
        command = [str(editor_path), *unity_args]
    environment = dict(os.environ)
    environment["DXMESSAGING_VERSION"] = options.dxmessaging_version
    if options.unity_import_archive:
        environment["DXAUDIO_UNITYPACKAGE"] = options.unity_import_archive

    observation_path = artifacts_path / "process-observation.json"
    launch_started = time.monotonic()
    retry_window = min(float(options.activation_retry_budget_seconds), editor_timeout)
    attempt = 0
    observation = ProcessObservation()
    try:
        while True:
            attempt += 1
            remaining = editor_timeout - (time.monotonic() - launch_started)
            observation = invoke_unity(
                command,
                environment,
                log_path,
                observation_path,
                remaining,
                options.editor_stall_seconds,
                options.completion_grace_seconds,
                not (player_smoke or import_smoke),
            )
            if (
                observation.origin != "process-exit"
                or observation.exit_code == 0
                or retry_window <= (time.monotonic() - launch_started)
                or not is_transient_activation(log_path, results_path)
            ):
                break
            remaining = retry_window - (time.monotonic() - launch_started)
            delay = min(60.0, 5.0 * (2.0 ** (attempt - 1)), remaining / 2.0)
            if delay <= 0:
                break
            print(
                "::warning::Unity activation hit its 20111 seat limit before project startup. "
                "Retrying within the activation budget.",
                flush=True,
            )
            time.sleep(delay)
            if retry_window <= (time.monotonic() - launch_started):
                break
            log_path.unlink(missing_ok=True)
    except Exception as error:
        write_redacted_tail(log_path)
        raise ValidationFailure(safe_report(str(error))) from error

    write_environment_warnings(log_path)
    if observation.origin == "wall-timeout":
        write_redacted_tail(log_path)
        raise ValidationFailure(
            f"Unity did not exit within {options.editor_timeout_seconds} seconds."
        )
    if observation.origin == "stall-timeout":
        write_redacted_tail(log_path)
        raise ValidationFailure(
            f"Unity produced no log output for {options.editor_stall_seconds} seconds "
            "before completion."
        )
    if observation.origin == "completion-grace":
        print(
            "::warning::Unity reported test completion but required shutdown after its "
            "grace period. The NUnit result remains mandatory.",
            flush=True,
        )
    exit_code = observation.exit_code or 0
    if exit_code != 0:
        write_redacted_tail(log_path)
        raise ValidationFailure(f"Unity failed with exit code {exit_code}. Log: {log_path}")
    if not log_path.is_file():
        raise ValidationFailure("Unity did not write a compiler log.")
    log_text = log_path.read_text(encoding="utf-8", errors="replace")
    if ANALYZER_FAILURE_PATTERN.search(log_text) is not None:
        write_redacted_tail(log_path)
        raise ValidationFailure("Unity reported an analyzer load failure or exception.")
    if import_smoke or player_smoke:
        _check_receipt(log_path, receipt, import_smoke)
        return 0
    if not results_path.is_file():
        write_redacted_tail(log_path)
        raise ValidationFailure(f"Unity did not write an EditMode result file. Log: {log_path}")
    try:
        _validate_results(
            results_path,
            options.dxmessaging_version,
            assets_path / "DocSnippets",
            project_path,
            run_label,
        )
    except ValidationFailure as error:
        raise ValidationFailure(safe_report(str(error))) from error
    return 0


def tail(options: argparse.Namespace) -> int:
    """Print a redacted log tail and the failed NUnit cases for one run."""
    log_path = Path(options.log_path) if options.log_path else Path()
    results_path = Path(options.results_path) if options.results_path else Path()
    runner_temp = os.environ.get("RUNNER_TEMP", "")
    if runner_temp and options.unity_version:
        run_label = run_name(options.unity_version, options.label)
        base = Path(runner_temp) / f"dxaudio-results-{run_label}"
        if not options.log_path:
            log_path = base / "editmode.log"
        if not options.results_path:
            results_path = base / "editmode-results.xml"
    print("Unity EditMode log tail (redacted):", flush=True)
    write_redacted_tail(log_path)
    if not results_path or not results_path.is_file():
        print("Unity EditMode failures: no result file.", flush=True)
        return 0
    print("Unity EditMode failures:", flush=True)
    try:
        root = parse_nunit_result(results_path)
        failed = 0
        for case in root.findall(".//test-case"):
            if case.get("result") not in ("Failed", "Error"):
                continue
            failed += 1
            message = ""
            failure = case.find("failure/message")
            if failure is not None:
                message = WHITESPACE_PATTERN.sub(
                    " ", safe_report("".join(failure.itertext()))
                ).strip()
            name = WHITESPACE_PATTERN.sub(" ", safe_report(case.get("fullname", ""))).strip()
            print(f"  | {name}: {message}", flush=True)
        print(f"  {failed} failed case(s).", flush=True)
        write_nunit_timings(root)
    except (ValidationFailure, ET.ParseError) as error:
        print(f"  | {safe_report(f'Could not read result: {error}')}", flush=True)
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="unity-validation",
        description="Run or report one disposable Unity EditMode validation.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    run_parser = subparsers.add_parser("run", help="create a disposable project and run Unity")
    run_parser.add_argument("--unity-version", default=os.environ.get("UNITY_VERSION", ""))
    run_parser.add_argument("--unity-editor-path", default=os.environ.get("UNITY_EDITOR_PATH", ""))
    run_parser.add_argument("--package-root", default=os.environ.get("GITHUB_WORKSPACE", ""))
    run_parser.add_argument(
        "--dxmessaging-version", default=os.environ.get("DXMESSAGING_VERSION", "")
    )
    run_parser.add_argument("--label", default=os.environ.get("UNITY_TEST_LABEL", ""))
    run_parser.add_argument(
        "--consumer-archive", default=os.environ.get("DXAUDIO_CONSUMER_ARCHIVE", "")
    )
    run_parser.add_argument("--archive-smoke", action="store_true")
    run_parser.add_argument("--integration-player-smoke", action="store_true")
    run_parser.add_argument("--unity-import-archive", default="")
    run_parser.add_argument("--editor-timeout-seconds", type=int, default=2400)
    run_parser.add_argument("--editor-stall-seconds", type=int, default=300)
    run_parser.add_argument("--completion-grace-seconds", type=int, default=120)
    run_parser.add_argument("--activation-retry-budget-seconds", type=int, default=360)
    run_parser.set_defaults(handler=run)
    tail_parser = subparsers.add_parser("tail", help="print a redacted log tail")
    tail_parser.add_argument("--unity-version", default=os.environ.get("UNITY_VERSION", ""))
    tail_parser.add_argument("--label", default=os.environ.get("UNITY_TEST_LABEL", ""))
    tail_parser.add_argument("--log-path", default="")
    tail_parser.add_argument("--results-path", default="")
    tail_parser.set_defaults(handler=tail)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    options = build_parser().parse_args(argv)
    handler = cast("Callable[[argparse.Namespace], int]", options.handler)
    try:
        return int(handler(options))
    except ValidationFailure as error:
        print(safe_report(str(error)), file=sys.stderr, flush=True)
        return 1
    except KeyboardInterrupt:
        print("Unity validation interrupted.", file=sys.stderr, flush=True)
        return 130
    except Exception as error:  # a validator must fail closed and redact its cause
        print(safe_report(str(error)), file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    sys.exit(main())
