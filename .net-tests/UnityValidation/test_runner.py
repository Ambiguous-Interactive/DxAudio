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
    $scope = 'WallstopStudios.DxAudio.Tests.Editor.AnalyzerActivation.'
    if ($env:CONTROL -ne 'missing-activation') {
        $name = if ($env:CONTROL -eq 'wrong-scope') { 'OtherSuite.' } else { $scope }
        $xml += '<test-case fullname="' + $name + 'AllExpectedAssembliesUseThePinnedAnalyzers" result="Passed"/>'
    } else {
        $xml += '<test-case fullname="OtherPassedCase" result="Passed"/>'
    }
    if ($env:CONTROL -ne 'missing-reference-graph') {
        $xml += '<test-case fullname="' + $scope + 'AssemblyDefinitionsMatchTheCompilerReferenceGraph" result="Passed"/>'
    } else {
        $xml += '<test-case fullname="OtherPassingReferenceCase" result="Passed"/>'
    }
    $xml += '<test-case fullname="' + $scope + 'EditorOnlyAssembliesContainNoConcreteMonoBehaviours" result="Passed"/>'
    if ($env:DXMESSAGING_VERSION -and $env:CONTROL -ne 'missing-integration') {
        $xml += '<test-suite type="Assembly" name="WallstopStudios.DxAudio.Tests.Integration.DxMessaging.dll" passed="1"><test-case result="Passed"/></test-suite>'
    } elseif ($env:DXMESSAGING_VERSION) {
        $xml += '<test-case fullname="OtherTestOutsideIntegration" result="Passed"/>'
    }
    $xml += '</test-run>'
    if ($env:CONTROL -eq 'external-entity') { $xml = '<!DOCTYPE test-run [<!ENTITY control SYSTEM "file:///missing.xml">]>' + $xml }
    Set-Content -LiteralPath $args[$args.IndexOf('-testResults') + 1] -Value $xml
    if ($env:CONTROL -eq 'broken-analyzer') { Add-Content -LiteralPath $log -Value 'warning CS8032: invalid analyzer' }
} elseif ($env:CONTROL -ne 'missing-receipt') {
    if ($args -contains '-runTests' -or $args -notcontains '-quit') { exit 2 }
    if (-not (Test-Path (Join-Path $project 'Packages/dxaudio.tgz'))) { exit 1 }
    Add-Content -LiteralPath $log -Value 'DxAudio archive and player script validation passed.'
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

    def run_validation(self, *args, control="", integration="", archive=""):
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
        self.assertIn("did not confirm the archive and player script validation", log)
        status, log = self.run_validation("-ArchiveSmoke")
        self.assertNotEqual(status, 0, log)
        self.assertIn("pristine consumer tarball is missing", log)

    def test_timeout_kills_the_editor_and_descendant(self):
        child = self.root / "late-child.ps1"
        child.write_text("Start-Sleep -Seconds 2; Set-Content -LiteralPath $env:LATE_RECEIPT -Value 'alive'\n")
        status, log = self.run_validation("-EditorTimeoutSeconds", "1", control="timeout")
        self.assertNotEqual(status, 0, log)
        self.assertIn("Started descendant", log)
        self.assertIn("did not exit within 1 seconds", log)
        time.sleep(2.3)
        self.assertFalse((self.root / "late-receipt").exists())

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
            '<test-run><test-case fullname="' + secrets[2] + '" result="Failed"><failure><message><![CDATA[' + text
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


if __name__ == "__main__":
    unittest.main()
