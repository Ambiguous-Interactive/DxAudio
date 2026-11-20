#!/usr/bin/env python3
"""Run DxAudio's locked, development-only C# checks."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from functools import partial
from pathlib import Path
from xml.sax.saxutils import quoteattr

ROOT = Path(__file__).resolve().parent
COMPATIBILITY = Path("Compatibility/DxAudio.Core.Compatibility.csproj")
TESTS = Path("DxAudio.Tests.csproj")
CATALOG = Path("Catalog/DxAudio.AnalyzerCatalog.csproj")


def dotnet(
    *args: str,
    check: bool = True,
    capture_output: bool = False,
    env: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    executable = shutil.which("dotnet")
    if executable is None:
        raise SystemExit("The .NET SDK is required for DxAudio checks.")
    return subprocess.run(  # noqa: S603 - SDK resolved from PATH and fixed build arguments.
        [str(Path(executable).resolve()), *args],
        cwd=ROOT,
        check=check,
        capture_output=capture_output,
        text=True,
        env=env,
    )


def verify_red_controls(environment: dict[str, str]) -> None:
    controls = (
        (COMPATIBILITY, "CS1030", "#warning DxAudio compiler control"),
        (
            COMPATIBILITY,
            "CS0117",
            "internal class Probe { internal void Verify(object value) { System.ArgumentNullException.ThrowIfNull(value); } }",
        ),
        (
            COMPATIBILITY,
            "RS0030 WUH026 ERP021 EPS06 CA2200",
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
        (
            TESTS,
            "NUnit2009",
            """
            internal sealed class NUnitProbe
            {
                [NUnit.Framework.Test]
                public void AssertsTheSameValue()
                {
                    int value = 1;
                    NUnit.Framework.Assert.That(value, NUnit.Framework.Is.EqualTo(value));
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
        for project, expected, source in controls:
            fixture.write_text(source, encoding="utf-8")
            result = dotnet(
                "build",
                str(project),
                "-c",
                "Release",
                "--no-restore",
                "--no-incremental",
                f"-p:CustomAfterMicrosoftCommonTargets={project_import}",
                capture_output=True,
                check=False,
                env=environment,
            )
            if not result.returncode or any(
                f"error {code}:" not in result.stdout for code in expected.split()
            ):
                raise SystemExit(
                    f"Compiler control failed to produce {expected}.\n{result.stdout}\n{result.stderr}"
                )
        print("Compiler and analyzer negative controls passed.", flush=True)


def run_checks(*, test_only: bool, inventory: bool, scratch: Path | None = None) -> int:
    environment = dict(os.environ)
    reports = ROOT.parent / ".artifacts" / "analysis"
    analyzers = ROOT.parent / ".artifacts" / "analyzers"
    test_artifacts = reports / "vstest"
    if scratch is not None:
        analyzers = scratch / "analyzers"
        test_artifacts = scratch / "vstest"
        environment.update(
            ArtifactsPath=str(scratch / "build"),
            UseSharedCompilation="false",
            DxAudioAnalyzerDirectory=str(analyzers),
        )
        print(
            "Rebuilding in a fresh artifact directory with shared compilation disabled.", flush=True
        )
    environment["VSTEST_ARTIFACTS_DIRECTORY"] = str(test_artifacts)
    run_dotnet = partial(dotnet, env=environment)
    projects = (TESTS,) if test_only else (COMPATIBILITY, TESTS)
    for project in projects:
        run_dotnet("restore", str(project), "--locked-mode", "-warnaserror")
    if inventory:
        reports.mkdir(parents=True, exist_ok=True)
        failures = 0
        for project in projects:
            result = run_dotnet(
                "build",
                str(project),
                "-c",
                "Release",
                "--no-restore",
                "--no-incremental",
                "-p:AnalysisLevel=10.0-all",
                f"-p:ErrorLog={reports / (project.stem + '.sarif')}",
                check=False,
            )
            failures += bool(result.returncode)
        return int(bool(failures))

    if not test_only:
        run_dotnet("restore", str(CATALOG), "--locked-mode", "-warnaserror")
        subprocess.run(  # noqa: S603 - fixed Python script and argument vector.
            [sys.executable, "prepare_analyzers.py", "--output-dir", str(analyzers)],
            cwd=ROOT,
            check=True,
        )
        run_dotnet(
            "run",
            "--project",
            str(CATALOG),
            "-c",
            "Release",
            "--no-restore",
            "--",
            str(analyzers / "WallstopStudios.UnityHelpers.Analyzers.dll"),
            "UnityHelpers.ruleset",
        )
        verify_red_controls(environment)
        run_dotnet("build", str(COMPATIBILITY), "-c", "Release", "--no-restore", "-warnaserror")
    test_artifacts.mkdir(parents=True, exist_ok=True)
    run_dotnet(
        "test",
        str(TESTS),
        "-c",
        "Release",
        "--no-restore",
        "-warnaserror",
    )
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--test-only", action="store_true", help="run the .NET test project only")
    parser.add_argument(
        "--inventory",
        action="store_true",
        help="run the full SDK analyzer profile and retain SARIF",
    )
    parser.add_argument(
        "--clean",
        action="store_true",
        help="use fresh build artifacts and disable shared compilation",
    )
    options = parser.parse_args()
    if options.clean:
        if options.test_only or options.inventory:
            parser.error("--clean runs the complete compiler and test gate")
        with tempfile.TemporaryDirectory(prefix="dxaudio-compiler-clean-") as scratch:
            return run_checks(test_only=False, inventory=False, scratch=Path(scratch))
    return run_checks(test_only=options.test_only, inventory=options.inventory)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.returncode) from None
