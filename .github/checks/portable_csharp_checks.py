"""Adapt pinned unity-helpers source checks to the DxAudio tree."""

from __future__ import annotations

import argparse
import importlib
import os
import subprocess
import sys
import tempfile
from collections.abc import Callable
from pathlib import Path

PYTHON_CHECKS = (
    "lint_comment_block_form",
    "lint_duplicate_usings",
    "lint_production_test_hooks",
    "lint_xml_doc_summaries",
)
PORTABLE_PATHS = tuple(f"scripts/{module}.py" for module in PYTHON_CHECKS) + (
    "scripts/comment-stripping.ps1",
    "scripts/git-staging-helpers.ps1",
    "scripts/lint-csharp-naming.ps1",
    "scripts/lint-nested-type-placement.js",
)
ROOTS = ("Runtime", "Editor", "Tests", "Samples~", ".net-tests")


def naming_script(upstream: Path, source_roots: tuple[str, ...]) -> Path:
    source = upstream / "scripts" / "lint-csharp-naming.ps1"
    raw = source.read_text(encoding="utf-8-sig")
    # The pinned script's historical field exceptions describe unity-helpers
    # assets. DxAudio freezes its own serialized API in its contract snapshot.
    start = raw.index("$historicalSerializedFields = @(")
    end = raw.index("\n)", start) + 2
    raw = raw[:start] + "$historicalSerializedFields = @()" + raw[end:]
    raw = raw.replace(
        "$historicalSerializedFields.Path",
        "@($historicalSerializedFields | ForEach-Object { $_.Path })",
    )
    roots = ", ".join(f"'{name}'" for name in source_roots)
    start = raw.index("$sourceRoots = @(")
    end = raw.index(")", start) + 1
    raw = raw[:start] + f"$sourceRoots = @({roots})" + raw[end:]
    raw = raw.replace("(Join-Path $PSScriptRoot '..')", "$env:DXAUDIO_CHECK_ROOT")
    raw = raw.replace("Join-Path $PSScriptRoot '..' $rootName", "Join-Path $env:DXAUDIO_CHECK_ROOT $rootName")
    destination = source.with_name("lint-dxaudio-csharp-naming.ps1")
    destination.write_text(raw, encoding="utf-8")
    return destination


def command_output(command: list[str], root: Path, environment: dict[str, str]) -> tuple[int, str]:
    completed = subprocess.run(
        command,
        cwd=root,
        env=environment,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return completed.returncode, completed.stdout.strip()


def run_controls(checks: dict[str, Callable[[str], list]], naming: Path, nested: Path) -> None:
    samples = {
        "comment-block-form": (
            "class Good { /* two prose lines\n go here */ }",
            "class Bad\n{\n        // first\n        // second\n    int X;\n}\n",
        ),
        "duplicate-usings": (
            "namespace Good { using System; }",
            "namespace Bad { using System; using System; }",
        ),
        "production-test-hooks": (
            "class Good { internal void Send() {} }",
            "class Bad { internal void SendForTests() {} }",
        ),
        "xml-doc-summaries": (
            "class Good\n{\n  /// <summary>One.</summary>\n  void M() {}\n}",
            "class Bad\n{\n  /// <summary>One.</summary>\n  /// <summary>Two.</summary>\n  void M() {}\n}",
        ),
    }
    for name, (good, bad) in samples.items():
        if checks[name](good) or not checks[name](bad):
            raise ValueError(f"{name} did not distinguish its positive and negative controls")

    with tempfile.TemporaryDirectory(prefix="dxaudio-csharp-controls-") as folder:
        fixture = Path(folder)
        for name in ROOTS:
            (fixture / name).mkdir()
        subject = fixture / "Runtime" / "Fixture.cs"
        commands = (
            (["pwsh", "-NoProfile", "-File", str(naming)], ("UNH004", "badField", "Bad_Name")),
            (["node", str(nested)], ("nested-type-placement", "Nested")),
        )
        environment = dict(os.environ, DXAUDIO_CHECK_ROOT=str(fixture), NESTED_TYPE_PLACEMENT_ROOTS=str(subject.parent))
        subject.write_text("class Fixture { private int _value; private class Nested {} }\n", encoding="utf-8")
        for command, marks in commands:
            code, details = command_output(command, fixture, environment)
            if code:
                raise ValueError(f"{marks[0]} rejected a positive control. {details}")
        subject.write_text(
            "class Fixture\n{\n    private class Nested {}\n    private int badField;\n    private void Bad_Name() {}\n}\n",
            encoding="utf-8",
        )
        for command, marks in commands:
            code, details = command_output(command, fixture, environment)
            if not code or not all(mark in details for mark in marks):
                raise ValueError(f"{marks[0]} failed to reject its negative control. {details}")


def run_checks(root: Path, modules: Path, upstream_tree: Path) -> list[str]:
    sys.dont_write_bytecode = True
    if str(modules) not in sys.path:
        sys.path.insert(0, str(modules))
    comments, imports, hooks, summaries = (importlib.import_module(module) for module in PYTHON_CHECKS)
    sources = importlib.import_module("csharp_sources")
    roots = tuple(name for name in ROOTS if (root / name).is_dir())
    if roots != ROOTS:
        raise ValueError("portable C# checks require all DxAudio source roots")
    checks = {
        "comment-block-form": comments.analyze,
        "duplicate-usings": imports.analyze,
        "production-test-hooks": hooks.analyze_file,
        "xml-doc-summaries": summaries.analyze_file,
    }
    problems = []
    files = sources.source_files(root, roots)
    if not files:
        raise ValueError("portable C# checks found no inputs")
    for path in files:
        text = path.read_text(encoding="utf-8-sig")
        relative = path.relative_to(root).as_posix()
        for rule, analyze in checks.items():
            if rule == "production-test-hooks" and path.relative_to(root).parts[0] not in ("Runtime", "Editor", "Samples~"):
                continue
            for finding in analyze(text):
                if hasattr(finding, "line"):
                    line, detail = finding.line, getattr(finding, "first", "")
                elif isinstance(finding, tuple):
                    line, detail = finding
                else:
                    line, detail = 1, finding
                problems.append(f"{relative}:{line}: {rule}: {detail}")

    nested = upstream_tree / "scripts" / "lint-nested-type-placement.js"
    naming = naming_script(upstream_tree, roots)
    run_controls(checks, naming, nested)
    environment = dict(
        os.environ,
        DXAUDIO_CHECK_ROOT=str(root),
        NESTED_TYPE_PLACEMENT_ROOTS=os.pathsep.join(str(root / name) for name in roots),
    )
    for command in (["pwsh", "-NoProfile", "-File", str(naming)], ["node", str(nested), "--verbose"]):
        code, details = command_output(command, root, environment)
        if code:
            problems.append(details or f"portable C# command exited {code}")
    print(f"[unity-helpers-portable] checked {len(files)} files across six policies and their red/green controls")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("upstream_tree", type=Path, help="staged immutable unity-helpers tree")
    args = parser.parse_args()
    problems = run_checks(Path(__file__).resolve().parents[2], args.upstream_tree / "scripts", args.upstream_tree)
    for message in problems:
        print(message)
    return int(bool(problems))


if __name__ == "__main__":
    raise SystemExit(main())
