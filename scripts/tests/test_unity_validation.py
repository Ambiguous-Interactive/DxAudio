#!/usr/bin/env python3
"""Unity validation port contract tests (issue #120)."""

from __future__ import annotations

import contextlib
import io
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from typing import TYPE_CHECKING
from unittest.mock import patch

if TYPE_CHECKING:
    from collections.abc import Callable

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts" / "unity"))
sys.dont_write_bytecode = True

import unity_validation as validation  # noqa: E402 - the repository module is imported after its path is added.


def _expect_validation_failure(body: Callable[[], object]) -> None:
    try:
        body()
    except validation.ValidationFailure:
        return
    raise AssertionError("expected ValidationFailure")


def _nunit(
    *,
    result: str = "Passed",
    passed: int = 2,
    total: int = 2,
    failed: int = 0,
    skipped: int = 0,
    cases: str = "",
    suites: str = "",
) -> str:
    body = cases or (
        '<test-case fullname="Case.One" result="Passed" duration="1.000"/>'
        '<test-case fullname="Case.Two" result="Passed" duration="2.000"/>'
    )
    return (
        f'<test-run result="{result}" passed="{passed}" total="{total}" '
        f'failed="{failed}" skipped="{skipped}" inconclusive="0">'
        f'<test-suite type="TestFixture" fullname="Fixture" duration="3.000">'
        f"{body}</test-suite>{suites}</test-run>"
    )


class RunNameTests(unittest.TestCase):
    def test_empty_label_keeps_the_version(self) -> None:
        assert validation.run_name("6000.5.2f1", "") == "6000.5.2f1"
        assert validation.run_name("6000.5.2f1", "   ") == "6000.5.2f1"

    def test_label_is_appended_and_validated(self) -> None:
        assert validation.run_name("6000.5.2f1", "no-integration") == "6000.5.2f1-no-integration"
        _expect_validation_failure(lambda: validation.run_name("6000.5.2f1", "bad label"))


class RedactionTests(unittest.TestCase):
    def test_redacts_every_credential_shape_and_control_byte(self) -> None:
        text = (
            "serial SECRET-SERIAL and password p@ss\n"
            "Authorization: Bearer abcdef1234567890\n"
            "Authorization: Basic c2VjcmV0OnBhc3M=\n"
            '<License id="FAKE-LICENSE">\n'
            "-----BEGIN PRIVATE KEY-----\nQmFzZTY0U2VjcmV0\n-----END PRIVATE KEY-----\n"
            "ghp_fakefakefakefake12345678 and AKIA0000FAKE0000FAKE\n"
            "control\x01byte"
        )
        environment = {
            "UNITY_SERIAL": "SECRET-SERIAL",
            "UNITY_EMAIL": "email@example.com",
            "UNITY_PASSWORD": "p@ss",
        }
        with patch.dict(os.environ, environment, clear=False):
            redacted = validation.safe_report(text)
        for secret in (
            "SECRET-SERIAL",
            "p@ss",
            "abcdef1234567890",
            "c2VjcmV0OnBhc3M",
            "FAKE-LICENSE",
            "QmFzZTY0U2VjcmV0",
            "ghp_fakefakefakefake12345678",
            "AKIA0000FAKE0000FAKE",
        ):
            assert secret not in redacted
        assert "[REDACTED]" in redacted
        assert "\x01" not in redacted
        assert "control?byte" in redacted


class NUnitValidationTests(unittest.TestCase):
    def test_accepts_a_complete_passing_run(self) -> None:
        root = validation.parse_nunit_result_from_text(_nunit())
        assert validation.assert_passing_nunit_result(root) == 2

    def test_rejects_an_unaccounted_case(self) -> None:
        root = validation.parse_nunit_result_from_text(_nunit(passed=1, total=2))
        _expect_validation_failure(lambda: validation.assert_passing_nunit_result(root))

    def test_rejects_a_dtd(self) -> None:
        _expect_validation_failure(
            lambda: validation.parse_nunit_result_from_text(
                '<!DOCTYPE test-run [<!ENTITY x SYSTEM "file:///x">]>' + _nunit()
            )
        )

    def test_rejects_a_doctype_after_a_comment(self) -> None:
        _expect_validation_failure(
            lambda: validation.parse_nunit_result_from_text(
                '<!--comment--><!DOCTYPE test-run [<!ENTITY x "expanded">]><test-run>&x;</test-run>'
            )
        )

    def test_accepts_doctype_text_inside_test_output(self) -> None:
        document = _nunit(
            passed=1,
            total=1,
            cases='<test-case fullname="Case.Text" result="Passed" duration="0.100">'
            "<output><![CDATA[<!DOCTYPE harmless>]]></output></test-case>",
        )
        root = validation.parse_nunit_result_from_text(document)
        assert validation.assert_passing_nunit_result(root) == 1

    def test_rejects_an_invalid_duration(self) -> None:
        root = validation.parse_nunit_result_from_text(
            _nunit(cases='<test-case fullname="Case.One" result="Passed" duration="NaN"/>')
        )
        _expect_validation_failure(lambda: validation.write_nunit_timings(root))


class TimingTests(unittest.TestCase):
    def test_orders_slowest_first_and_warns_past_the_fixture_budget(self) -> None:
        document = (
            '<test-run result="Passed" passed="2" total="2" failed="0" skipped="0" '
            'inconclusive="0"><test-suite type="TestFixture" fullname="SlowFixture" '
            'duration="120.100">'
            '<test-case fullname="Case.Slow" result="Passed" duration="2.850"/>'
            '<test-case fullname="Case.Fast" result="Passed" duration="0.100"/>'
            "</test-suite></test-run>"
        )
        output = io.StringIO()
        with (
            patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": ""}),
            contextlib.redirect_stdout(output),
        ):
            validation.write_nunit_timings(validation.parse_nunit_result_from_text(document))
        text = output.getvalue()
        assert text.index("2.850s  Case.Slow") < text.index("0.100s  Case.Fast")
        assert "::warning::Fixture SlowFixture took 120.100s" in text

    def test_reports_scoped_measurements_but_keeps_samples_out_of_the_summary(self) -> None:
        document = (
            '<test-run result="Passed" passed="1" total="1" failed="0" skipped="0" '
            'inconclusive="0">'
            '<test-case fullname="WallstopStudios.DxAudio.Tests.Editor.DspHostTests.RecordDspRenderWallTime(8)" '
            'result="Passed" duration="0.200"><output>DxAudio DSP render: Noise Gate, '
            "added p95 534.00 us</output></test-case>"
            '<test-case fullname="WallstopStudios.DxAudio.Tests.Editor.DspDistortionTimingTests.RecordDistortionStageCosts" '
            'result="Passed" duration="0.400"><output>DxAudio Distortion samples: 0,1/2/3</output></test-case>'
            "</test-run>"
        )
        output = io.StringIO()
        with (
            patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": ""}),
            contextlib.redirect_stdout(output),
        ):
            validation.write_nunit_timings(validation.parse_nunit_result_from_text(document))
        text = output.getvalue()
        assert "DxAudio DSP render: Noise Gate, added p95 534.00 us" in text
        assert "DxAudio Distortion samples: 0,1/2/3" in text


class ActivationTests(unittest.TestCase):
    def test_only_a_pre_startup_seat_error_is_transient(self) -> None:
        with tempfile.TemporaryDirectory(prefix="dxaudio-activation-") as directory:
            root = Path(directory)
            log = root / "editmode.log"
            results = root / "results.xml"
            log.write_text("[Licensing::Client] Error: Code 20111\n", encoding="utf-8")
            assert validation.is_transient_activation(log, results)
            log.write_text(
                "Application.AssetDatabase Initial Refresh Start\n"
                "[Licensing::Client] Error: Code 20111\n",
                encoding="utf-8",
            )
            assert not validation.is_transient_activation(log, results)
            log.write_text(
                "[Licensing::Client] Error: Code 20113 serial expired\n", encoding="utf-8"
            )
            assert not validation.is_transient_activation(log, results)
            results.write_text("<test-run/>", encoding="utf-8")
            log.write_text("[Licensing::Client] Error: Code 20111\n", encoding="utf-8")
            assert not validation.is_transient_activation(log, results)


class WatchdogTests(unittest.TestCase):
    def _invoke(
        self, script: str, *, timeout: float, stall: int, grace: int
    ) -> tuple[validation.ProcessObservation, Path]:
        directory = Path(tempfile.mkdtemp(prefix="dxaudio-watchdog-"))
        self.addCleanup(shutil.rmtree, directory, True)
        editor = directory / "editor.py"
        editor.write_text(script, encoding="utf-8")
        log = directory / "editmode.log"
        observation = directory / "observation.json"
        result = validation.invoke_unity(
            [sys.executable, str(editor), str(log)],
            dict(os.environ),
            log,
            observation,
            timeout,
            stall,
            grace,
            True,
        )
        return result, log

    def test_wall_timeout_returns_124_and_kills_the_child(self) -> None:
        observation, _ = self._invoke("import time\ntime.sleep(20)\n", timeout=1, stall=0, grace=0)
        assert observation.origin == "wall-timeout"
        assert observation.exit_code == 124

    def test_completion_grace_preserves_the_reported_code(self) -> None:
        observation, _ = self._invoke(
            "import sys,time\n"
            "from pathlib import Path\n"
            "Path(sys.argv[1]).write_text("
            "'Test run completed. Exiting with code 0 (Ok). Run completed.\\n')\n"
            "time.sleep(20)\n",
            timeout=5,
            stall=0,
            grace=1,
        )
        assert observation.origin == "completion-grace"
        assert observation.exit_code == 0
        assert observation.completion_exit_code == 0

    def test_stall_timeout_returns_125(self) -> None:
        observation, _ = self._invoke("import time\ntime.sleep(20)\n", timeout=20, stall=1, grace=0)
        assert observation.origin == "stall-timeout"
        assert observation.exit_code == 125


class MainTests(unittest.TestCase):
    def test_tail_prints_a_redacted_result_and_failure(self) -> None:
        with tempfile.TemporaryDirectory(prefix="dxaudio-tail-") as directory:
            root = Path(directory)
            log = root / "unity.log"
            results = root / "results.xml"
            log.write_text("secret-password\n", encoding="utf-8")
            results.write_text(
                '<test-run><test-case fullname="Case.Failed" result="Failed" duration="2.325">'
                "<failure><message>bad secret-password value</message></failure>"
                "</test-case></test-run>",
                encoding="utf-8",
            )
            output = io.StringIO()
            with (
                patch.dict(
                    os.environ,
                    {
                        "UNITY_PASSWORD": "secret-password",
                        "RUNNER_TEMP": "",
                        "GITHUB_STEP_SUMMARY": "",
                    },
                    clear=False,
                ),
                contextlib.redirect_stdout(output),
            ):
                status = validation.main(
                    ["tail", "--log-path", str(log), "--results-path", str(results)]
                )
            assert status == 0
            text = output.getvalue()
            assert "secret-password" not in text
            assert "1 failed case(s)" in text
            assert "2.325s  Case.Failed" in text


if __name__ == "__main__":
    unittest.main()
