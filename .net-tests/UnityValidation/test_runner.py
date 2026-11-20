"""Negative controls for the disposable Unity CI runner."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
VERSION = "2021.3.45f1"
FAKE_EDITOR = """
$ErrorActionPreference = 'Stop'
$project = $args[$args.IndexOf('-projectPath') + 1]
if ($args -contains '-runTests') {
    $xml = '<test-run result="Passed" passed="2">'
    if ($env:CONTROL -ne 'missing-activation') {
        $xml += '<test-case name="AllExpectedAssembliesUseThePinnedAnalyzers" result="Passed"/>'
    }
    if ($env:CONTROL -ne 'missing-reference-graph') {
        $xml += '<test-case name="AssemblyDefinitionsMatchTheCompilerReferenceGraph" result="Passed"/>'
    }
    $xml += '<test-case name="EditorOnlyAssembliesContainNoConcreteMonoBehaviours" result="Passed"/>'
    if ($env:DXMESSAGING_VERSION -and $env:CONTROL -ne 'missing-integration') {
        $xml += '<test-suite type="Assembly" name="WallstopStudios.DxAudio.Tests.Integration.DxMessaging.dll" passed="1"/>'
    }
    $xml += '</test-run>'
    Set-Content -LiteralPath $args[$args.IndexOf('-testResults') + 1] -Value $xml
    if ($env:CONTROL -eq 'broken-analyzer') { Write-Output 'warning CS8032: invalid analyzer' }
} elseif ($env:CONTROL -ne 'missing-receipt') {
    if (-not (Test-Path (Join-Path $project 'Packages/dxaudio.tgz'))) { exit 1 }
    Write-Output 'DxAudio archive and player script validation passed.'
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
        directory = tempfile.TemporaryDirectory(prefix="dxaudio-unity-controls-")
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
            "UNITY_PASSWORD": "dummy-password",
            "DXMESSAGING_VERSION": integration,
            "DXAUDIO_CONSUMER_ARCHIVE": str(archive),
            "CONTROL": control,
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
            ("missing-reference-graph", "", "AssemblyDefinitionsMatchTheCompilerReferenceGraph control."),
            ("broken-analyzer", "", "reported an analyzer load failure"),
            ("missing-integration", "3.3.0", "do not match the expected DxMessaging test state"),
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


if __name__ == "__main__":
    unittest.main()
