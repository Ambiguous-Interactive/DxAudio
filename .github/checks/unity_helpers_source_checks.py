"""Run portable checks from an immutable unity-helpers Git tree on DxAudio."""

from __future__ import annotations

import importlib
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

UPSTREAM = "https://github.com/Ambiguous-Interactive/unity-helpers.git"
MODULES = (
    "check_eol",
    "cli_encoding",
    "csharp_sources",
    "lint_asmdef",
    "lint_comment_block_form",
    "lint_comparison_direction",
    "lint_concurrent_cache_fill",
    "lint_conditional_call_chains",
    "lint_drawer_multiobject",
    "lint_no_regions",
    "lint_preserve_attributes",
    "lint_unity_file_naming",
)
SOURCE_ROOTS = ("Runtime", "Editor", "Tests", "Samples~", ".net-tests")


def fetch(upstream: Path, revision: str) -> None:
    subprocess.run(["git", "init", "--quiet", str(upstream)], check=True)
    subprocess.run(["git", "-C", str(upstream), "remote", "add", "origin", UPSTREAM], check=True)
    subprocess.run(
        ["git", "-C", str(upstream), "fetch", "--quiet", "--depth=1", "--filter=blob:none", "origin", revision],
        check=True,
    )


def stage_paths(upstream: Path, revision: str, destination: Path, paths: tuple[str, ...]) -> None:
    destination.mkdir(exist_ok=True)
    # Fetch the sources and their internal helper modules together.
    subprocess.run(
        ["git", "-C", str(upstream), f"--work-tree={destination}", "restore", "--source", revision, "--worktree", "--", *paths],
        check=True,
    )


def stage_modules(upstream: Path, revision: str, destination: Path) -> None:
    paths = tuple(f"scripts/{module}.py" for module in MODULES) + (
        "scripts/comment-stripping.ps1",
        "scripts/tests/test-comment-stripping.ps1",
        "scripts/tests/test-out-parameter-discipline.js",
        "scripts/tests/test-pr-workflow-concurrency.ps1",
        "scripts/tests/test-workflow-license-secret-scope.ps1",
        "scripts/tests/test-workflow-repository-guard.ps1",
    )
    stage_paths(upstream, revision, destination, paths)


def run_contract_controls(root: Path, staged: Path) -> None:
    tests = staged / "scripts/tests"
    subprocess.run(["pwsh", "-NoProfile", "-File", str(tests / "test-comment-stripping.ps1")], check=True)

    upstream = tests / "test-out-parameter-discipline.js"
    text = upstream.read_text(encoding="utf-8")
    replacements = {
        'const root = path.resolve(__dirname, "../..");': "const root = process.cwd();",
        'const BASELINE = new Set(require("./out-parameter-baseline.json"));': "const BASELINE = new Set();",
        'anchorFile: "Runtime/Core/DataStructure/Adapters/SerializableDictionary.cs",\n    anchorMethod: "TryGetValue"':
            'anchorFile: "Runtime/Core/SfxVoiceArbiter.cs",\n    anchorMethod: "TryChoose"',
        'anchorFile: "Editor/CustomDrawers/SerializableDictionaryPropertyDrawer.cs",\n    anchorMethod: "TryResolveKeyValueTypes"':
            'anchorFile: "Editor/SfxCueClipDefaults.cs",\n    anchorMethod: "TryMeasure"',
    }
    for expected, replacement in replacements.items():
        if text.count(expected) != 1:
            raise ValueError("upstream out-parameter check layout differs from the pinned adapter")
        text = text.replace(expected, replacement)
    adapted = tests / "test-dxaudio-out-parameters.cjs"
    adapted.write_text(text, encoding="utf-8")
    subprocess.run(["node", str(adapted)], cwd=root, check=True)

    workflows = staged / ".github/workflows"
    workflows.mkdir(parents=True)
    for workflow in (root / ".github/workflows").glob("*.y*ml"):
        staged_name = workflow.name if workflow.suffix == ".yml" else f"{workflow.stem}-yaml.yml"
        destination = workflows / staged_name
        if destination.exists():
            raise ValueError(f"duplicate staged workflow name: {staged_name}")
        shutil.copyfile(workflow, destination)
    workflow_checks = (
        "test-pr-workflow-concurrency.ps1",
        "test-workflow-license-secret-scope.ps1",
        "test-workflow-repository-guard.ps1",
    )
    for script in workflow_checks:
        subprocess.run(["pwsh", "-NoProfile", "-File", str(tests / script)], check=True)

    fixture = workflows / "contract-negative-control.yml"
    fixture.write_text(
        "name: Broken control\non:\n  pull_request:\njobs:\n  broken:\n"
        "    if: github.repository == 'somewhere/else'\n    runs-on: ubuntu-latest\n"
        "    env:\n      UNITY_SERIAL: ${{ secrets.UNITY_SERIAL }}\n"
        "    steps:\n      - run: echo broken\n",
        encoding="utf-8",
    )
    expected = (
        "must define top-level concurrency",
        "puts secrets.UNITY_SERIAL in scope",
        "must not gate on a hard-coded repository name",
    )
    for script, diagnostic in zip(workflow_checks, expected):
        result = subprocess.run(
            ["pwsh", "-NoProfile", "-File", str(tests / script)],
            capture_output=True, text=True, encoding="utf-8",
        )
        if result.returncode != 1 or diagnostic not in result.stdout:
            raise ValueError(f"{script} missed its negative workflow control: {result.stdout}{result.stderr}")
    fixture.unlink()


def run_source_checks(root: Path, modules: Path) -> list[str]:
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(modules))
    sources = importlib.import_module("csharp_sources")
    assembly_definitions = importlib.import_module("lint_asmdef")
    comparisons = importlib.import_module("lint_comparison_direction")
    caches = importlib.import_module("lint_concurrent_cache_fill")
    conditional = importlib.import_module("lint_conditional_call_chains")
    drawers = importlib.import_module("lint_drawer_multiobject")
    regions = importlib.import_module("lint_no_regions")
    filenames = importlib.import_module("lint_unity_file_naming")
    preserve = importlib.import_module("lint_preserve_attributes")
    available_roots = tuple(name for name in SOURCE_ROOTS if (root / name).exists())
    problems = []
    methods = []
    files = sources.source_files(root, available_roots)
    if not files:
        raise ValueError("source checks found no C# inputs")
    assembly_definitions.EXTERNAL_ASSEMBLIES |= {"wallstopstudios.dxmessaging"}
    assembly_definitions.SOURCE_ROOTS = tuple(name for name in available_roots if name != ".net-tests")
    if assembly_definitions.check(root) != 0:
        problems.append("upstream assembly definition validation failed")
    for path in files:
        text = path.read_text(encoding="utf-8-sig")
        relative = path.relative_to(root).as_posix()
        for _, _, line in regions.directives(text):
            problems.append(f"{relative}:{line}: region directives are not allowed")
        for line, message in filenames.analyze(text, path.name):
            problems.append(f"{relative}:{line}: {message}")
        if relative.startswith("Editor/") and path.name.lower().endswith("drawer.cs"):
            for finding in drawers.analyze(text):
                problems.append(f"{relative}:{finding.line}: {finding.message}")
        if relative.startswith(("Runtime/", "Editor/")):
            for finding in caches.scan_source(text).findings:
                problems.append(f"{relative}:{finding.line}: {finding.message}")
            found, unparsed = conditional.methods_in(text, relative)
            methods.extend(found)
            problems.extend(unparsed)
        if not relative.startswith(("Samples~/", ".net-tests/")):
            tokens = comparisons.tokenize(text)
            for finding in comparisons.find_violations(tokens, comparisons.mark_generics(tokens)):
                problems.append(f"{relative}:{finding.line}:{finding.column}: use an ascending comparison")
    problems.extend(conditional.violations(methods))
    _, preservation_failures = preserve.analyze(root / "Runtime")
    problems.extend(preservation_failures)
    print(f"[unity-helpers-source] checked {len(files)} C# sources")
    return problems


UNSAFE_PATHS = (
    "scripts/lint-unsafe-code.js",
    "Generator~/Directory.Build.props",
    "Generator~/CheckProjects.ruleset",
    "Generator~/ProductionCheckProjects.ruleset",
    "Generator~/WallstopStudios.UnityHelpers.SyntaxPolicy",
    "Runtime/Analyzers",
)


def run_unsafe_checks(root: Path, upstream: Path, revision: str, destination: Path) -> None:
    stage_paths(upstream, revision, destination, UNSAFE_PATHS)
    environment = dict(os.environ, DOTNET_ROLL_FORWARD="Major")
    subprocess.run(
        ["node", str(Path(__file__).with_name("unity_helpers_unsafe.cjs")), str(destination), str(root)],
        cwd=destination, env=environment, check=True,
    )


ANALYZER_TESTS = "Generator~/WallstopStudios.UnityHelpers.Analyzers.Tests"
ANALYZER_FIXTURE_PATHS = (
    ANALYZER_TESTS,
    "Generator~/WallstopStudios.UnityHelpers.Analyzers",
    "Generator~/Directory.Build.props",
    "Generator~/CheckProjects.ruleset",
    "Generator~/ProductionCheckProjects.ruleset",
    "Editor/Tools/AnalyzerPolicyExamples.cs",
    "Editor/Tools/AnalyzerPolicyWindow.cs",
    "Runtime/Analyzers",
    "Runtime/Core/DataStructure/Adapters/SerializableDictionary.cs",
    "Runtime/Core/DataStructure/Adapters/SerializableHashSet.cs",
    "Runtime/Core/DataStructure/Adapters/SerializableList.cs",
    "Runtime/Core/Extension/DictionaryExtensions.cs",
    "Runtime/Core/Extension/RandomExtensions.cs",
    "Runtime/Core/Helper/Helpers.cs",
    "Runtime/Core/Helper/RestorableGlobal.cs",
    "Runtime/Core/Random/RandomState.cs",
    "Runtime/Core/Random/UnityRandom.cs",
    "Runtime/Core/Threading/SemaphoreLease.cs",
    "Runtime/Tags/AttributeUtilities.cs",
    "Runtime/Tags/EffectHandle.cs",
    "Runtime/Tags/EffectHandler.cs",
    "Runtime/Tags/TagHandler.cs",
    "Runtime/Utils/BufferAction.cs",
    "Runtime/Utils/BufferFunc.cs",
    "Runtime/Utils/DisposalLease.cs",
    "Runtime/Utils/RuntimeSingleton.cs",
)


def run_analyzer_fixtures(upstream: Path, revision: str, destination: Path) -> None:
    stage_paths(upstream, revision, destination, ANALYZER_FIXTURE_PATHS)
    # The upstream launcher targets .NET 9. Run it on the already pinned .NET 10 runtime.
    environment = dict(os.environ, DOTNET_ROLL_FORWARD="Major")
    subprocess.run(
        ["dotnet", "test", f"{ANALYZER_TESTS}/WallstopStudios.UnityHelpers.Analyzers.Tests.csproj",
         "-c", "Release", "--nologo", "-p:CopyAnalyzerPayload=false",
         "--logger", "trx;LogFileName=fixtures.trx", "--results-directory", str(destination / "results")],
        cwd=destination,
        env=environment,
        check=True,
    )

    counters = ET.parse(destination / "results" / "fixtures.trx").find(".//{*}Counters")
    if counters is None or int(counters.get("passed", "0")) == 0:
        raise ValueError("upstream analyzer fixture run did not execute any passing tests")


def verify_source_controls(root: Path, modules: Path) -> list[str]:
    runtime = root / "Runtime"
    runtime.mkdir(parents=True)
    editor = root / "Editor"
    editor.mkdir()
    fault = runtime / "Fault.cs"
    drawer = editor / "FaultDrawer.cs"
    definition = runtime / "Control.asmdef"
    definition.write_text('{"name":"WrongName","rootNamespace":"Control"}\n', encoding="utf-8")
    fault.write_text("""#region hidden
public class Renamed : MonoBehaviour
{
    ConcurrentDictionary<int, int> cache;
    public void Fill(int key) { cache[key] = 1; }
    public bool Active(int count) { return count > 0; }
    [Conditional("ONE")]
    public void First() { Second(); }
    [Conditional("TWO")]
    public void Second() { }
    public void Read() { this.GetCustomAttribute<FeatureAttribute>(); }
}
public class FeatureAttribute : Attribute { }
#endregion
""", encoding="utf-8")
    drawer.write_text("""public class FaultDrawer
{
    public void OnGUI()
    {
        property.intValue = 1;
    }
}
""", encoding="utf-8")
    diagnostics = "\n".join(run_source_checks(root, modules))
    expected = (
        "region directives are not allowed",
        "use an ascending comparison",
        "[Conditional] method 'First' calls [Conditional] method 'Second'",
        "is not marked [Preserve]",
        "Renamed.cs",
        "Property assignment during render",
        "filled through its indexer",
        "upstream assembly definition validation failed",
    )
    problems = [f"Negative control did not report {label!r}" for label in expected if label not in diagnostics]
    fault.write_text("""public class Fault : MonoBehaviour
{
    public bool Active(int count) { return 0 < count; }
    [Conditional("ONE")]
    public void First() { }
    public void Read() { this.GetCustomAttribute<FeatureAttribute>(); }
}
[Preserve]
public class FeatureAttribute : Attribute { }
""", encoding="utf-8")
    drawer.write_text("public class FaultDrawer { }\n", encoding="utf-8")
    definition.write_text('{"name":"Control","rootNamespace":"Control"}\n', encoding="utf-8")
    problems.extend(f"Green control failed: {message}" for message in run_source_checks(root, modules))
    return problems
