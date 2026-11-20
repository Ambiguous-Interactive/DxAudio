"""Negative controls for the disposable Unity CI runner."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest


ROOT = Path(__file__).resolve().parents[2]
VERSION = "2021.3.45f1"
FAKE_EDITOR = """
$ErrorActionPreference = 'Stop'
$project = $args[$args.IndexOf('-projectPath') + 1]
$log = $args[$args.IndexOf('-logFile') + 1]
Set-Content -LiteralPath $log -Value 'Unity fake compiler'
if ($args[$args.IndexOf('-password') + 1] -cne $env:UNITY_PASSWORD -or -not (Test-Path -LiteralPath $project)) {
    exit 2
}
if ($env:CONTROL -eq 'activation-once') {
    $first = Join-Path $project 'first-attempt'
    if (-not (Test-Path -LiteralPath $first)) {
        Set-Content -LiteralPath $first -Value '20111'
        Add-Content -LiteralPath $log -Value '[Licensing::Client] Error: Code 20111'
        exit 1
    }
}
if ($env:CONTROL -in 'activation-expired','activation-after-startup','activation-persistent','activation-other-code') {
    if ($env:CONTROL -eq 'activation-expired') {
        Add-Content -LiteralPath $log -Value '[Licensing::Client] Error: Code 20113 serial expired'
    } elseif ($env:CONTROL -eq 'activation-after-startup') {
        Add-Content -LiteralPath $log -Value 'Application.AssetDatabase Initial Refresh Start','[Licensing::Client] Error: Code 20111'
    } elseif ($env:CONTROL -eq 'activation-persistent') {
        Add-Content -LiteralPath $log -Value '[Licensing::Client] Error: Code 20111'
    } else {
        Add-Content -LiteralPath $log -Value '[Licensing::Client] Error: Code 201110'
    }
    exit 1
}
if ($env:CONTROL -eq 'licensing-observations') {
    Add-Content -LiteralPath $log -Value 'while verifying Licensing Client signature synthetic-private-value',
        'LicensingClient has failed validation synthetic-private-value',
        "failed because channel doesn't exist synthetic-private-value"
}
if ($env:CONTROL -eq 'stall') {
    Start-Sleep -Seconds 20
    exit 0
}
if ($env:CONTROL -eq 'completion-no-xml') {
    Add-Content -LiteralPath $log -Value 'Test run completed. Exiting with code 0 (Ok). Run completed.'
    Start-Sleep -Seconds 20
    exit 0
}
if ($env:CONTROL -eq 'chatty') {
    1..7 | ForEach-Object { Add-Content -LiteralPath $log -Value "tick $_"; Start-Sleep -Seconds 1 }
}
if ($env:CONTROL -eq 'timeout') {
    $child = Join-Path $PSScriptRoot 'late-child.ps1'
    Start-Process -FilePath ([Environment]::ProcessPath) -ArgumentList ('-NoProfile -NonInteractive -File "' + $child + '"')
    Add-Content -LiteralPath $log -Value 'Started descendant'
    Start-Sleep -Seconds 20
    exit 0
}
if ($args -contains '-runTests') {
    if ($args -contains '-quit' -or $args -contains '-executeMethod') { exit 2 }
    $failed = if ($env:CONTROL -eq 'contradictory-result') { 1 } else { 0 }
    $passed = if ($env:DXMESSAGING_VERSION) { 4 } else { 3 }
    $xml = '<test-run result="Passed" passed="' + $passed + '" total="' + $passed + '" failed="' + $failed + '" skipped="0" inconclusive="0">'
    $fixtureTime = if ($env:CONTROL -eq 'slow-fixture') { '120.100' } else { '4.100' }
    $xml += '<test-suite type="TestFixture" fullname="AnalyzerFixtures" duration="' + $fixtureTime + '">'
    $scope = 'WallstopStudios.DxAudio.Tests.Editor.AnalyzerActivation.'
    if ($env:CONTROL -ne 'missing-activation') {
        $name = if ($env:CONTROL -eq 'wrong-scope') { 'OtherSuite.' } else { $scope }
        $duration = if ($env:CONTROL -eq 'invalid-duration') { 'NaN' } else { '2.850' }
        $xml += '<test-case fullname="' + $name + 'AllExpectedAssembliesUseThePinnedAnalyzers" result="Passed" duration="' + $duration + '"/>'
    } else {
        $xml += '<test-case fullname="OtherPassedCase" result="Passed" duration="0.400"/>'
    }
    if ($env:CONTROL -ne 'missing-reference-graph') {
        $xml += '<test-case fullname="' + $scope + 'AssemblyDefinitionsMatchTheCompilerReferenceGraph" result="Passed" duration="0.100"/>'
    } else {
        $xml += '<test-case fullname="OtherPassingReferenceCase" result="Passed" duration="0.100"/>'
    }
    $xml += '<test-case fullname="' + $scope + 'EditorOnlyAssembliesContainNoConcreteMonoBehaviours" result="Passed" duration="1.150"/></test-suite>'
    if ($env:DXMESSAGING_VERSION -and $env:CONTROL -ne 'missing-integration') {
        $xml += '<test-suite type="Assembly" name="WallstopStudios.DxAudio.Tests.Integration.DxMessaging.dll" passed="1">'
        $xml += '<test-case fullname="IntegrationCase" result="Passed" duration="0.200"/></test-suite>'
    } elseif ($env:DXMESSAGING_VERSION) {
        $xml += '<test-case fullname="OtherTestOutsideIntegration" result="Passed" duration="0.200"/>'
    }
    $xml += '</test-run>'
    if ($env:CONTROL -eq 'external-entity') { $xml = '<!DOCTYPE test-run [<!ENTITY control SYSTEM "file:///missing.xml">]>' + $xml }
    Set-Content -LiteralPath $args[$args.IndexOf('-testResults') + 1] -Value $xml
    if ($env:CONTROL -eq 'broken-analyzer') { Add-Content -LiteralPath $log -Value 'warning CS8032: invalid analyzer' }
    if ($env:CONTROL -in 'completion-hang','completion-failed') {
        $reported = if ($env:CONTROL -eq 'completion-hang') { 0 } else { 2 }
        Add-Content -LiteralPath $log -Value "Test run completed. Exiting with code $reported. Run completed."
        Start-Sleep -Seconds 20
        exit 0
    }
    if ($env:CONTROL -eq 'completion-real-failure') {
        Add-Content -LiteralPath $log -Value 'Test run completed. Exiting with code 0 (Ok). Run completed.'
        Start-Sleep -Milliseconds 650
        exit 9
    }
    if ($env:CONTROL -eq 'activation-test-failure') {
        Add-Content -LiteralPath $log -Value '[Licensing::Client] Error: Code 20111'
        exit 4
    }
} elseif ($env:CONTROL -ne 'missing-receipt') {
    if ($args -contains '-runTests' -or $args -notcontains '-quit') { exit 2 }
    if (-not (Test-Path (Join-Path $project 'Packages/dxaudio.tgz'))) { exit 1 }
    $method = $args[$args.IndexOf('-executeMethod') + 1]
    $integration = $method.EndsWith('.RunWithIntegration', [StringComparison]::Ordinal)
    if ($integration -ne [bool]$env:DXMESSAGING_VERSION) { exit 2 }
    if ($integration -and $env:CONTROL -ne 'wrong-player-receipt') {
        Add-Content -LiteralPath $log -Value 'DxAudio integration player script validation passed.'
    } else {
        Add-Content -LiteralPath $log -Value 'DxAudio archive and player script validation passed.'
    }
}
exit 0
"""
FAKE_FETCH = '''
import pathlib
import sys
out = pathlib.Path(sys.argv[sys.argv.index("--output-dir") + 1])
for name in ("WallstopStudios.UnityHelpers.Analyzers.dll", "nunit.analyzers.dll"):
    (out / name).write_bytes(b"fake binary")
'''


class UnityRunnerTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(prefix="dxaudio unity controls ")
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.fixture = self.root / "checkout"
        self.fixture.mkdir()
        for path in ("Runtime", "Editor", "Tests/Editor", "Samples~/BasicUsage", ".net-tests/UnityValidation"):
            (self.fixture / path).mkdir(parents=True)
        for path in ("Runtime", "Editor", "Tests"):
            (self.fixture / (path + ".meta")).touch()
            (self.fixture / path / (path + ".asmdef")).write_text("{}", encoding="utf-8")
        (self.fixture / "package.json").write_text("{}", encoding="utf-8")
        (self.fixture / ".net-tests" / "prepare_analyzers.py").write_text(FAKE_FETCH, encoding="utf-8")
        for path in ("UnityHelpers.ruleset", "UnityValidation/ArchiveSmoke.cs", "UnityValidation/AnalyzerActivation.cs"):
            shutil.copy2(ROOT / ".net-tests" / path, self.fixture / ".net-tests" / path)
        self.editor = self.root / "fake-editor.ps1"
        self.editor.write_text(FAKE_EDITOR, encoding="utf-8")
        self.archive = self.root / "archive.tgz"
        self.archive.touch()

    def run_validation(self, *args, control="", integration="", archive="", summary=""):
        env = {
            **os.environ,
            "RUNNER_TEMP": str(self.root),
            "GITHUB_WORKSPACE": str(self.fixture),
            "UNITY_EDITOR_PATH": str(self.editor),
            "UNITY_VERSION": VERSION,
            "UNITY_TEST_LABEL": "control",
            "UNITY_SERIAL": "dummy-serial",
            "UNITY_EMAIL": "dummy-email",
            "UNITY_PASSWORD": 'dummy password "quoted"',
            "DXMESSAGING_VERSION": integration,
            "DXAUDIO_CONSUMER_ARCHIVE": str(archive),
            "CONTROL": control,
            "LATE_RECEIPT": str(self.root / "late-receipt"),
            "GITHUB_STEP_SUMMARY": str(summary),
        }
        output = subprocess.run(
            ["pwsh", "-NoProfile", "-File", str(ROOT / "scripts" / "unity" / "run-editmode-tests.ps1"), *args],
            env=env,
            capture_output=True,
            text=True,
            timeout=30,
        )
        return output.returncode, output.stdout + output.stderr

    def test_stage_applies_analyzers_only_to_disposable_package(self):
        status, log = self.run_validation()
        self.assertEqual(status, 0, log)
        project = self.root / f"dxaudio-unity-{VERSION}-control"
        staged = project / "DxAudio"
        self.assertEqual(len(list(staged.rglob("WarningsAsErrors.ruleset"))), 3)
        self.assertIn("RoslynAnalyzer", (staged / "Runtime/WallstopStudios.UnityHelpers.Analyzers.dll.meta").read_text())
        self.assertFalse(list(self.fixture.rglob("*.dll")))

    def test_lost_analyzer_or_integration_evidence_fails(self):
        cases = (
            ("missing-activation", "", "AllExpectedAssembliesUseThePinnedAnalyzers control."),
            ("wrong-scope", "", "AllExpectedAssembliesUseThePinnedAnalyzers control."),
            ("missing-reference-graph", "", "AssemblyDefinitionsMatchTheCompilerReferenceGraph control."),
            ("broken-analyzer", "", "reported an analyzer load failure"),
            ("missing-integration", "3.3.0", "do not match the expected DxMessaging test state"),
            ("contradictory-result", "", "does not account for each test"),
            ("external-entity", "", "DtdProcessing"),
            ("invalid-duration", "", "missing or invalid duration"),
        )
        for control, integration, expected in cases:
            with self.subTest(control=control):
                status, log = self.run_validation(control=control, integration=integration)
                self.assertNotEqual(status, 0, log)
                self.assertIn(expected, log)

    def test_archive_is_pristine_and_requires_compilation_receipt(self):
        status, log = self.run_validation("-ArchiveSmoke", archive=self.archive)
        self.assertEqual(status, 0, log)
        project = self.root / f"dxaudio-unity-{VERSION}-control"
        self.assertFalse((project / "DxAudio").exists())
        manifest = json.loads((project / "Packages/manifest.json").read_text(encoding="utf-8-sig"))
        self.assertEqual(
            manifest,
            {"dependencies": {"com.unity.modules.audio": "1.0.0", "com.unity.modules.imgui": "1.0.0", "com.wallstop-studios.dxaudio": "file:dxaudio.tgz"}},
        )
        status, log = self.run_validation("-ArchiveSmoke", archive=self.archive, control="missing-receipt")
        self.assertNotEqual(status, 0, log)
        self.assertIn("did not emit the expected player script validation receipt", log)
        status, log = self.run_validation("-ArchiveSmoke")
        self.assertNotEqual(status, 0, log)
        self.assertIn("pristine consumer tarball is missing", log)

    def test_integration_player_smoke_has_a_distinct_manifest_and_receipt(self):
        status, log = self.run_validation("-IntegrationPlayerSmoke", archive=self.archive, integration="3.3.0")
        self.assertEqual(status, 0, log)
        project = self.root / f"dxaudio-unity-{VERSION}-control"
        manifest = json.loads((project / "Packages/manifest.json").read_text(encoding="utf-8-sig"))
        self.assertEqual(manifest["dependencies"]["com.wallstop-studios.dxmessaging"], "3.3.0")
        self.assertNotIn("com.unity.test-framework", manifest["dependencies"])
        self.assertNotIn("testables", manifest)
        for flags, version, control, expected in (
            (("-IntegrationPlayerSmoke",), "", "", "requires a DxMessaging version"),
            (("-ArchiveSmoke",), "3.3.0", "", "archive smoke requires the optional integration"),
            (("-IntegrationPlayerSmoke",), "3.3.0", "wrong-player-receipt", "did not emit the expected player script validation receipt"),
        ):
            with self.subTest(flags=flags, version=version, control=control):
                status, log = self.run_validation(*flags, archive=self.archive, integration=version, control=control)
                self.assertNotEqual(status, 0, log)
                self.assertIn(expected, log)

    def test_successful_editmode_reports_slowest_fixtures_and_cases(self):
        status, log = self.run_validation()
        self.assertEqual(status, 0, log)
        self.assertIn("4.100s  AnalyzerFixtures", log)
        self.assertLess(log.index("2.850s"), log.index("1.150s"))
        self.assertLess(log.index("1.150s"), log.index("0.100s"))
        summary = self.root / "summary.md"
        status, log = self.run_validation(control="slow-fixture", summary=summary)
        self.assertEqual(status, 0, log)
        self.assertIn("::warning::Fixture AnalyzerFixtures took 120.100s", log)
        self.assertIn("### Unity EditMode timings", summary.read_text(encoding="utf-8"))

    def test_timeout_kills_the_editor_and_descendant(self):
        child = self.root / "late-child.ps1"
        child.write_text("Start-Sleep -Seconds 5; Set-Content -LiteralPath $env:LATE_RECEIPT -Value 'alive'\n")
        status, log = self.run_validation("-EditorTimeoutSeconds", "4", control="timeout")
        self.assertNotEqual(status, 0, log)
        self.assertIn("Started descendant", log)
        self.assertIn("did not exit within 4 seconds", log)
        time.sleep(3)
        self.assertFalse((self.root / "late-receipt").exists())

    def process_observation(self):
        return json.loads((self.root / f"dxaudio-results-{VERSION}-control" / "process-observation.json").read_text(encoding="utf-8"))

    def test_watchdog_completion_requires_a_passing_receipt_and_preserves_real_exit(self):
        for control, expected_status, origin, exit_code, expected in (
            ("completion-hang", 0, "completion-grace", 0, "required shutdown after its grace period"),
            ("completion-no-xml", 1, "completion-grace", 0, "Unity did not write an EditMode result file"),
            ("completion-failed", 1, "completion-grace", 2, "Unity failed with exit code 2"),
            ("completion-real-failure", 1, "process-exit", 9, "Unity failed with exit code 9"),
        ):
            with self.subTest(control=control):
                status, log = self.run_validation(
                    "-EditorTimeoutSeconds", "20", "-EditorStallSeconds", "4", "-CompletionGraceSeconds", "5", control=control
                )
                self.assertEqual(status, expected_status, log)
                self.assertIn(expected, log)
                observation = self.process_observation()
                self.assertEqual(observation["Origin"], origin)
                if origin == "process-exit":
                    self.assertEqual(observation["RawExitCode"], exit_code)
                else:
                    self.assertEqual(observation["CompletionExitCode"], exit_code)

    def test_stall_fails_and_periodic_activity_avoids_stall(self):
        status, log = self.run_validation("-EditorTimeoutSeconds", "18", "-EditorStallSeconds", "4", control="stall")
        self.assertNotEqual(status, 0, log)
        self.assertIn("produced no log output for 4 seconds", log)
        self.assertEqual(self.process_observation()["Origin"], "stall-timeout")
        status, log = self.run_validation("-EditorTimeoutSeconds", "18", "-EditorStallSeconds", "4", control="chatty")
        self.assertEqual(status, 0, log)
        self.assertEqual(self.process_observation()["Origin"], "process-exit")

    def test_retries_only_a_transient_activation_error_before_startup(self):
        status, log = self.run_validation("-ActivationRetryBudgetSeconds", "7", control="activation-once")
        self.assertEqual(status, 0, log)
        self.assertIn("seat limit before project startup", log)
        final_log = self.root / f"dxaudio-results-{VERSION}-control" / "editmode.log"
        self.assertNotIn("20111", final_log.read_text(encoding="utf-8"))
        for control in ("activation-expired", "activation-after-startup", "activation-other-code", "activation-test-failure"):
            with self.subTest(control=control):
                status, log = self.run_validation("-ActivationRetryBudgetSeconds", "7", control=control)
                self.assertNotEqual(status, 0, log)
                self.assertNotIn("Retrying within the activation budget", log)
        status, log = self.run_validation("-ActivationRetryBudgetSeconds", "5", control="activation-persistent")
        self.assertNotEqual(status, 0, log)
        self.assertIn("Retrying within the activation budget", log)
        self.assertIn("20111", final_log.read_text(encoding="utf-8"))

    def test_licensing_warnings_report_observations_without_altering_success(self):
        status, log = self.run_validation(control="licensing-observations")
        self.assertEqual(status, 0, log)
        self.assertEqual(log.count("::warning::Licensing"), 3)
        self.assertIn("3 match(es)", log)
        self.assertNotIn("synthetic-private-value", log)

    def test_stage_failure_cannot_reuse_a_previous_receipt(self):
        status, log = self.run_validation()
        self.assertEqual(status, 0, log)
        results = self.root / f"dxaudio-results-{VERSION}-control" / "editmode-results.xml"
        self.assertTrue(results.is_file())
        (self.fixture / ".net-tests" / "prepare_analyzers.py").write_text("raise SystemExit(1)\n", encoding="utf-8")
        status, log = self.run_validation()
        self.assertNotEqual(status, 0, log)
        self.assertFalse(results.exists())

    def test_redacts_duplicate_credentials_in_log_and_test_failure(self):
        log = self.root / "unity.log"
        xml = self.root / "results.xml"
        secrets = [
            "SC-0000-SYNTHETIC",
            "SYNTHETIC_access-token...",
            "ghp_fakefakefakefake12345678",
            "synthetic-password",
            "AKIA0000FAKE0000FAKE",
            "NONREAL123TOKEN",
            "FAKE-LICENSE-0000-0000",
            "FakeBase64Secret1234==",
        ]
        text = (
            f"before Access token: {secrets[1]} UNITY_PASSWORD={secrets[3]} after\n"
            f"{secrets[4]} Authorization: Bearer {secrets[5]}\n"
            f'<License id="{secrets[6]}">\n'
            f'-----BEGIN PRIVATE KEY-----\n{secrets[7]}\n-----END PRIVATE KEY-----\n'
        )
        log.write_text(secrets[0] + "\n" + text, encoding="utf-8")
        xml.write_text(
            '<test-run><test-case fullname="' + secrets[2] + '" result="Failed" duration="2.325"><failure><message><![CDATA[' + text
            + ']]></message></failure></test-case></test-run>',
            encoding="utf-8",
        )
        command = ROOT / "scripts" / "unity" / "show-editmode-log-tail.ps1"
        output = subprocess.run(
            ["pwsh", "-NoProfile", "-File", str(command), "-LogPath", str(log), "-ResultsPath", str(xml)],
            capture_output=True,
            text=True,
            timeout=10,
        )
        self.assertEqual(output.returncode, 0, output.stdout + output.stderr)
        for secret in secrets:
            self.assertNotIn(secret, output.stdout)
        self.assertIn("before Access token:", output.stdout)
        self.assertIn("after", output.stdout)
        self.assertIn("1 failed case(s)", output.stdout)
        self.assertIn("2.325s  [REDACTED]", output.stdout)


if __name__ == "__main__":
    unittest.main()
