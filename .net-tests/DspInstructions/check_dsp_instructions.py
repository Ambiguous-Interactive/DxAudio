"""Compare warmed DSP render instruction counts using the same Mono and Callgrind."""

import os
import re
import shutil
import subprocess
import tarfile
from argparse import ArgumentParser
from io import BytesIO
from pathlib import Path
from tempfile import TemporaryDirectory

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SDK = HERE.parent
RUNTIME = Path("Runtime/Core")
WORKLOAD = ROOT / "Tests/Editor/Core/Dsp/DspRenderWorkload.cs"
# Allow minor runtime and JIT differences while preserving a per-scenario comparison.
MAX_INCREASE_PERCENT = 2


def tool(name: str) -> str:
    executable = shutil.which(name)
    if executable is None:
        raise FileNotFoundError(f"Required profiler tool is missing: {name}")
    return str(Path(executable).absolute())


def capture(command: list[str], cwd: Path = ROOT) -> str:
    return subprocess.check_output(command, cwd=cwd, text=True).strip()  # noqa: S603 - argv only


def build(runtime: Path, output: Path, refs: Path, compiler: Path) -> None:
    sources = [
        runtime / "AudioMath.cs",
        *sorted((runtime / "Dsp").glob("*.cs")),
        WORKLOAD,
        HERE / "Program.cs",
    ]
    references = [refs / name for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    subprocess.run(  # noqa: S603 - local build or profiling tool with argv only
        [
            tool("dotnet"),
            str(compiler),
            "-nologo",
            "-optimize+",
            "-noconfig",
            "-nostdlib+",
            "-warnaserror+",
            "-langversion:9.0",
            "-target:exe",
            f"-out:{output}",
            *[f"-r:{path}" for path in references],
            *map(str, sources),
        ],
        cwd=SDK,
        check=True,
    )


def count(mono: str, program: Path, index: int, work: Path, label: str) -> int:
    profile = work / f"{label}-{index}.callgrind"
    env = os.environ.copy()
    env["LD_LIBRARY_PATH"] = str(work) + os.pathsep + env.get("LD_LIBRARY_PATH", "")
    subprocess.run(  # noqa: S603 - local build or profiling tool with argv only
        [
            tool("valgrind"),
            "--tool=callgrind",
            "--instr-atstart=no",
            "--quiet",
            f"--callgrind-out-file={profile}",
            tool(mono),
            str(program),
            str(index),
        ],
        cwd=work,
        env=env,
        check=True,
    )
    data = profile.read_text(encoding="utf-8")
    match = re.search(r"^totals: ([1-9][0-9]*)$", data, re.MULTILINE)
    if match is None or "events: Ir\n" not in data:
        raise ValueError(f"No instruction count in {profile.name}")
    return int(match[1])


def check(base: str, mono: str, refs: Path, work: Path) -> int:
    sdk_version = capture([tool("dotnet"), "--version"], SDK)
    sdk_line = next(
        line
        for line in capture([tool("dotnet"), "--list-sdks"], SDK).splitlines()
        if line.startswith(sdk_version + " ")
    )
    sdk_root = Path(sdk_line.split("[", 1)[1].removesuffix("]"))
    compiler = sdk_root / sdk_version / "Roslyn/bincore/csc.dll"
    archive = subprocess.check_output(  # noqa: S603 - resolved Git revision and source path
        [tool("git"), "archive", base, str(RUNTIME)], cwd=ROOT
    )
    reference = work / "reference"
    (reference / RUNTIME / "Dsp").mkdir(parents=True)
    with tarfile.open(fileobj=BytesIO(archive), mode="r:") as source:
        for entry in source:
            path = Path(entry.name)
            if entry.isfile() and (
                path == RUNTIME / "AudioMath.cs"
                or (path.parent == RUNTIME / "Dsp" and path.suffix == ".cs")
            ):
                data = source.extractfile(entry)
                if data is None:
                    raise ValueError(f"Missing reference source {entry.name}")
                (reference / path).write_bytes(data.read())

    baseline = work / "baseline.exe"
    candidate = work / "candidate.exe"
    build(reference / RUNTIME, baseline, refs, compiler)
    build(ROOT / RUNTIME, candidate, refs, compiler)
    subprocess.run(  # noqa: S603 - local build or profiling tool with argv only
        [
            tool("cc"),
            "-shared",
            "-fPIC",
            "-o",
            str(work / "libdxaudio_callgrind.so"),
            str(HERE / "count.c"),
        ],
        check=True,
    )

    names = capture([tool(mono), str(candidate), "list"]).splitlines()
    if not names or names != capture([tool(mono), str(baseline), "list"]).splitlines():
        raise ValueError("The two builds did not list the same DSP workloads.")
    print(
        f"Comparing {len(names)} DSP workloads against {base}, using {capture([tool('valgrind'), '--version'])}.",
        flush=True,
    )
    print(capture([tool(mono), "--version"]).splitlines()[0], flush=True)
    failures = 0
    for index, name in enumerate(names):
        before = count(mono, baseline, index, work, "baseline")
        after = count(mono, candidate, index, work, "candidate")
        exceeded = after * 100 > before * (100 + MAX_INCREASE_PERCENT)
        failures += int(exceeded)
        result = "OVER" if exceeded else "within"
        print(
            f"{name}: {before:,} -> {after:,} Ir ({after / before - 1:+.2%}, {result} +{MAX_INCREASE_PERCENT}%)",
            flush=True,
        )
    return int(failures != 0)


if __name__ == "__main__":
    parser = ArgumentParser(description=__doc__)
    parser.add_argument("base", help="Git revision containing the reference runtime")
    parser.add_argument("--mono", default="mono")
    parser.add_argument("--mono-references", type=Path, default=Path("/usr/lib/mono/4.5"))
    args = parser.parse_args()
    revision = capture(
        [tool("git"), "rev-parse", "--verify", "--end-of-options", args.base + "^{commit}"]
    )
    with TemporaryDirectory(prefix="dxaudio-instructions-") as directory:
        raise SystemExit(
            check(revision, args.mono, args.mono_references.resolve(), Path(directory))
        )
