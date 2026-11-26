"""Negative controls for the disposable Unity CI runner."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import unittest


ROOT = Path(__file__).resolve().parents[2]
VERSION = "2021.3.45f1"
FAKE_EDITOR = r"""import os
import subprocess
import sys
import time
from pathlib import Path

args = sys.argv[1:]
def value(flag):
    return args[args.index(flag) + 1]

project = Path(value("-projectPath"))
log = Path(value("-logFile"))
log.write_text("Unity fake compiler\n", encoding="utf-8")
if value("-password") != os.environ.get("UNITY_PASSWORD") or not project.is_dir():
    raise SystemExit(2)
control = os.environ.get("CONTROL", "")
if control == "activation-once":
    first = project / "first-attempt"
    if not first.exists():
        first.write_text("20111", encoding="utf-8")
        with log.open("a", encoding="utf-8") as stream:
            stream.write("[Licensing::Client] Error: Code 20111\n")
        raise SystemExit(1)
if control in (
    "activation-expired",
    "activation-after-startup",
    "activation-persistent",
    "activation-other-code",
):
    with log.open("a", encoding="utf-8") as stream:
        if control == "activation-expired":
            stream.write("[Licensing::Client] Error: Code 20113 serial expired\n")
        elif control == "activation-after-startup":
            stream.write("Application.AssetDatabase Initial Refresh Start\n")
            stream.write("[Licensing::Client] Error: Code 20111\n")
        elif control == "activation-persistent":
            stream.write("[Licensing::Client] Error: Code 20111\n")
        else:
            stream.write("[Licensing::Client] Error: Code 201110\n")
    raise SystemExit(1)
if control == "licensing-observations":
    with log.open("a", encoding="utf-8") as stream:
        stream.write("while verifying Licensing Client signature synthetic-private-value\n")
        stream.write("LicensingClient has failed validation synthetic-private-value\n")
        stream.write("failed because channel doesn't exist synthetic-private-value\n")
if control == "stall":
    time.sleep(20)
    raise SystemExit(0)
if control == "completion-no-xml":
    with log.open("a", encoding="utf-8") as stream:
        stream.write("Test run completed. Exiting with code 0 (Ok). Run completed.\n")
    time.sleep(20)
    raise SystemExit(0)
if control == "chatty":
    for tick in range(1, 8):
        with log.open("a", encoding="utf-8") as stream:
            stream.write(f"tick {tick}\n")
        time.sleep(1)
if control == "timeout":
    child = Path(__file__).with_name("late-child.py")
    subprocess.Popen([sys.executable, str(child)])
    with log.open("a", encoding="utf-8") as stream:
        stream.write("Started descendant\n")
    time.sleep(20)
    raise SystemExit(0)
if "-runTests" in args:
    if "-quit" in args or "-executeMethod" in args:
        raise SystemExit(2)
    if control != "missing-docs-assembly":
        assemblies = project / "Library/ScriptAssemblies"
        assemblies.mkdir(parents=True, exist_ok=True)
        (assemblies / "WallstopStudios.DxAudio.Documentation.Validation.dll").touch()
    failed = 1 if control == "contradictory-result" else 0
    passed = 4 if os.environ.get("DXMESSAGING_VERSION") else 3
    if control == "render-timing":
        passed += 3
    fixture_time = "120.100" if control == "slow-fixture" else "4.100"
    scope = "WallstopStudios.DxAudio.Tests.Editor.AnalyzerActivation."
    parts = [
        f'<test-run result="Passed" passed="{passed}" total="{passed}" failed="{failed}" skipped="0" inconclusive="0">',
        f'<test-suite type="TestFixture" fullname="AnalyzerFixtures" duration="{fixture_time}">',
    ]
    if control != "missing-activation":
        name = "OtherSuite." if control == "wrong-scope" else scope
        duration = "NaN" if control == "invalid-duration" else "2.850"
        parts.append(
            f'<test-case fullname="{name}AllExpectedAssembliesUseThePinnedAnalyzers" result="Passed" duration="{duration}"/>'
        )
    else:
        parts.append('<test-case fullname="OtherPassedCase" result="Passed" duration="0.400"/>')
    if control != "missing-reference-graph":
        parts.append(
            f'<test-case fullname="{scope}AssemblyDefinitionsMatchTheCompilerReferenceGraph" result="Passed" duration="0.100"/>'
        )
    else:
        parts.append(
            '<test-case fullname="OtherPassingReferenceCase" result="Passed" duration="0.100"/>'
        )
    parts.append(
        f'<test-case fullname="{scope}EditorOnlyAssembliesContainNoConcreteMonoBehaviours" result="Passed" duration="1.150"/>'
    )
    parts.append("</test-suite>")
    if os.environ.get("DXMESSAGING_VERSION") and control != "missing-integration":
        suite_count = 2 if control == "mismatched-integration-count" else 1
        case = '<test-case fullname="IntegrationCase" result="Passed" duration="0.200"/>'
        if control == "empty-integration":
            parts.append(
                f'<test-suite type="Assembly" name="WallstopStudios.DxAudio.Tests.Integration.DxMessaging.dll" passed="{suite_count}"></test-suite>'
            )
            parts.append(case)
        else:
            parts.append(
                f'<test-suite type="Assembly" name="WallstopStudios.DxAudio.Tests.Integration.DxMessaging.dll" passed="{suite_count}">{case}</test-suite>'
            )
    elif os.environ.get("DXMESSAGING_VERSION"):
        parts.append('<test-case fullname="OtherTestOutsideIntegration" result="Passed" duration="0.200"/>')
    if control == "render-timing":
        parts.append(
            '<test-case fullname="WallstopStudios.DxAudio.Tests.Editor.DspHostTests.RecordDspRenderWallTime(8,&quot;Noise Gate&quot;,False)" result="Passed" duration="0.200">'
            "<output>DxAudio DSP render: Noise Gate, added p95 534.00 us, wall-time reference 426.67 us (over).</output></test-case>"
        )
        parts.append(
            '<test-case fullname="WallstopStudios.DxAudio.Tests.Editor.AllocationTests.MixMonitorMeasuresOnlyTheSelectedPlayersPlayCalls" result="Passed" duration="0.300">'
            "<output>Mix Monitor Play probe over 100000 single-voice plays. Off 230.000 ms. On 299.000 ms.</output></test-case>"
        )
        parts.append(
            '<test-case fullname="WallstopStudios.DxAudio.Tests.Editor.DspDistortionTimingTests.RecordDistortionStageCosts" result="Passed" duration="0.400">'
            "<output>DxAudio Distortion timing: fixed controls, p50 340.00 us.\n"
            "DxAudio Distortion samples: 0,0/15/45,1/25/75,3/350/1050,2/340/1020</output></test-case>"
        )
    parts.append("</test-run>")
    xml = "".join(parts)
    if control == "external-entity":
        xml = '<!DOCTYPE test-run [<!ENTITY control SYSTEM "file:///missing.xml">]>' + xml
    Path(value("-testResults")).write_text(xml, encoding="utf-8")
    if control == "broken-analyzer":
        with log.open("a", encoding="utf-8") as stream:
            stream.write("warning CS8032: invalid analyzer\n")
    if control in ("completion-hang", "completion-failed"):
        reported = 0 if control == "completion-hang" else 2
        with log.open("a", encoding="utf-8") as stream:
            stream.write(f"Test run completed. Exiting with code {reported}. Run completed.\n")
        time.sleep(20)
        raise SystemExit(0)
    if control == "completion-real-failure":
        with log.open("a", encoding="utf-8") as stream:
            stream.write("Test run completed. Exiting with code 0 (Ok). Run completed.\n")
        time.sleep(0.65)
        raise SystemExit(9)
    if control == "activation-test-failure":
        with log.open("a", encoding="utf-8") as stream:
            stream.write("[Licensing::Client] Error: Code 20111\n")
        raise SystemExit(4)
elif control != "missing-receipt":
    if "-runTests" in args or "-quit" not in args:
        raise SystemExit(2)
    if not (project / "Packages/dxaudio.tgz").exists():
        raise SystemExit(1)
    method = value("-executeMethod")
    integration = method.endswith(".RunWithIntegration")
    with log.open("a", encoding="utf-8") as stream:
        if integration and control != "wrong-player-receipt":
            stream.write("DxAudio integration player script validation passed.\n")
        else:
            stream.write("DxAudio archive and player script validation passed.\n")
raise SystemExit(0)
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
        (self.fixture / ".github/scripts").mkdir(parents=True)
        shutil.copy2(ROOT / ".github/scripts/compile_docs_snippets.py", self.fixture / ".github/scripts")
        shutil.copy2(ROOT / "README.md", self.fixture)
        shutil.copytree(ROOT / "Documentation~", self.fixture / "Documentation~")
        self.editor = self.root / "fake-editor.py"
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
        flags = {
            "-ArchiveSmoke": "--archive-smoke",
            "-IntegrationPlayerSmoke": "--integration-player-smoke",
            "-EditorTimeoutSeconds": "--editor-timeout-seconds",
            "-EditorStallSeconds": "--editor-stall-seconds",
            "-CompletionGraceSeconds": "--completion-grace-seconds",
            "-ActivationRetryBudgetSeconds": "--activation-retry-budget-seconds",
            "-Label": "--label",
            "-DxMessagingVersion": "--dxmessaging-version",
            "-UnityImportArchive": "--unity-import-archive",
        }
        translated = [flags.get(argument, argument) for argument in args]
        output = subprocess.run(
            [sys.executable, str(ROOT / "scripts" / "unity" / "unity_validation.py"), "run", *translated],
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
        docs = project / "Assets/DocSnippets"
        definition = json.loads((docs / "DocSnippets.asmdef").read_text(encoding="utf-8"))
        self.assertEqual(definition["references"], ["WallstopStudios.DxAudio"])
        self.assertTrue(any("quickstart.md" in source.read_text(encoding="utf-8") for source in docs.glob("*.cs")))
        self.assertFalse((docs / "DocSnippetDialogueMessages.cs").exists())
        self.assertIn("Unity compiled the documentation examples.", log)

    def test_lost_analyzer_or_integration_evidence_fails(self):
        status, log = self.run_validation(integration="3.3.0")
        self.assertEqual(0, status, log)
        docs = self.root / f"dxaudio-unity-{VERSION}-control" / "Assets/DocSnippets"
        definition = json.loads((docs / "DocSnippets.asmdef").read_text(encoding="utf-8"))
        self.assertIn("WallstopStudios.DxAudio.Integration.DxMessaging", definition["references"])
        self.assertTrue((docs / "DocSnippetDialogueMessages.cs").is_file())
        cases = (
            ("missing-activation", "", "AllExpectedAssembliesUseThePinnedAnalyzers control."),
            ("wrong-scope", "", "AllExpectedAssembliesUseThePinnedAnalyzers control."),
            ("missing-reference-graph", "", "AssemblyDefinitionsMatchTheCompilerReferenceGraph control."),
            ("broken-analyzer", "", "reported an analyzer load failure"),
            ("missing-integration", "3.3.0", "do not match the expected DxMessaging test state"),
            ("empty-integration", "3.3.0", "do not match the expected DxMessaging test state"),
            ("mismatched-integration-count", "3.3.0", "do not match the expected DxMessaging test state"),
            ("contradictory-result", "", "does not account for each test"),
            ("external-entity", "", "DtdProcessing"),
            ("invalid-duration", "", "missing or invalid duration"),
            ("missing-docs-assembly", "", "Unity did not compile the documentation examples"),
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

    def test_editmode_reports_scoped_measurements(self):
        summary = self.root / "summary.md"
        status, log = self.run_validation(control="render-timing", summary=summary)
        self.assertEqual(status, 0, log)
        summary_text = summary.read_text(encoding="utf-8")
        for measurement in (
            "DxAudio DSP render: Noise Gate, added p95 534.00 us, wall-time reference 426.67 us (over).",
            "Mix Monitor Play probe over 100000 single-voice plays. Off 230.000 ms. On 299.000 ms.",
            "DxAudio Distortion timing: fixed controls, p50 340.00 us.",
        ):
            self.assertIn(measurement, log)
            self.assertIn(measurement, summary_text)
        self.assertIn("DxAudio Distortion samples: 0,0/15/45,1/25/75,3/350/1050,2/340/1020", log)
        self.assertNotIn("DxAudio Distortion samples:", summary_text)

    def test_timeout_kills_the_editor_and_descendant(self):
        child = self.root / "late-child.py"
        child.write_text(
            "import os, time\nfrom pathlib import Path\ntime.sleep(5)\n"
            "Path(os.environ[\"LATE_RECEIPT\"]).write_text(\"alive\", encoding=\"utf-8\")\n",
            encoding="utf-8",
        )
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
        command = ROOT / "scripts" / "unity" / "unity_validation.py"
        output = subprocess.run(
            [
                sys.executable,
                str(command),
                "tail",
                "--log-path",
                str(log),
                "--results-path",
                str(xml),
            ],
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
