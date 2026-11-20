#!/usr/bin/env python3
"""Run DxAudio's locked, development-only C# checks."""

import argparse
import os
import subprocess
import tempfile
import sys
from pathlib import Path
from xml.sax.saxutils import quoteattr

ROOT = Path(__file__).resolve().parent
COMPATIBILITY = Path("Compatibility/DxAudio.Core.Compatibility.csproj")
TESTS = Path("DxAudio.Tests.csproj")
CATALOG = Path("Catalog/DxAudio.AnalyzerCatalog.csproj")


def dotnet(*args: str) -> None:
    subprocess.run(["dotnet", *args], cwd=ROOT, check=True)


def verify_red_controls() -> None:
    controls = (
        ("CS1030", "#warning DxAudio compiler control"),
        ("CS0117", "internal class Probe { internal void Verify(object value) { System.ArgumentNullException.ThrowIfNull(value); } }"),
        (
            "RS0030 WUH026 ERP021 EPS06",
            """
            internal struct Large
            {
                internal long A, B, C, D;
                internal long Read() => A + B + C + D;
            }

            internal sealed class Probe
            {
                private readonly Large _value = new Large { A = 1, B = 1, C = 1, D = 1 };
                internal long Copy() => _value.Read();
                internal bool Both(bool a, bool b) => a & b;
                internal object Linq() => System.Linq.Enumerable.Empty<int>();

                internal void Rethrow(System.Action run)
                {
                    try { run(); }
                    catch (System.Exception error) { throw error; }
                }
            }
            """,
        ),
    )
    with tempfile.TemporaryDirectory(prefix="dxaudio-compiler-controls-") as directory:
        fixture = Path(directory) / "Control.cs"
        project_import = Path(directory) / "Control.targets"
        project_import.write_text(
            f"<Project><ItemGroup><Compile Include={quoteattr(fixture.as_posix())} /></ItemGroup></Project>",
            encoding="utf-8",
        )
        for expected, source in controls:
            fixture.write_text(source, encoding="utf-8")
            result = subprocess.run(
                [
                    "dotnet", "build", str(COMPATIBILITY), "-c", "Release", "--no-restore", "--no-incremental",
                    f"-p:CustomAfterMicrosoftCommonTargets={project_import}",
                ],
                cwd=ROOT, capture_output=True, text=True,
            )
            if not result.returncode or any(f"error {code}:" not in result.stdout for code in expected.split()):
                raise SystemExit(f"Compiler control failed to produce {expected}.\n{result.stdout}\n{result.stderr}")
        print("Compiler and analyzer negative controls passed.", flush=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--test-only", action="store_true", help="run the .NET test project only")
    parser.add_argument("--inventory", action="store_true", help="run the full SDK analyzer profile and retain SARIF")
    options = parser.parse_args()

    projects = (TESTS,) if options.test_only else (COMPATIBILITY, TESTS)
    for project in projects:
        dotnet("restore", str(project), "--locked-mode", "-warnaserror")
    if options.inventory:
        reports = ROOT.parent / ".artifacts" / "analysis"
        reports.mkdir(parents=True, exist_ok=True)
        failures = 0
        for project in projects:
            result = subprocess.run(
                [
                    "dotnet", "build", str(project), "-c", "Release", "--no-restore", "--no-incremental",
                    "-p:AnalysisLevel=10.0-all", f"-p:ErrorLog={reports / (project.stem + '.sarif')}",
                ],
                cwd=ROOT,
            )
            failures += bool(result.returncode)
        return int(bool(failures))

    if not options.test_only:
        dotnet("restore", str(CATALOG), "--locked-mode", "-warnaserror")
        subprocess.run(
            [sys.executable, "prepare_analyzers.py", "--output-dir", "../.artifacts/analyzers"],
            cwd=ROOT, check=True,
        )
        dotnet(
            "run", "--project", str(CATALOG), "-c", "Release", "--no-restore", "--",
            "../.artifacts/analyzers/WallstopStudios.UnityHelpers.Analyzers.dll", "UnityHelpers.ruleset",
        )
        verify_red_controls()
        dotnet("build", str(COMPATIBILITY), "-c", "Release", "--no-restore", "-warnaserror")
    artifacts = ROOT.parent / ".artifacts" / "analysis" / "vstest"
    artifacts.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        ["dotnet", "test", str(TESTS), "-c", "Release", "--no-restore", "-warnaserror"],
        cwd=ROOT,
        check=True,
        env={**os.environ, "VSTEST_ARTIFACTS_DIRECTORY": str(artifacts)},
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.returncode) from None
