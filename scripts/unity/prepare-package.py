#!/usr/bin/env python3
"""Stage DxAudio and its pinned development analyzers outside the checkout."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from sync_compiler_policy import POLICY_RESPONSE_CONTENT, POLICY_RESPONSE_FILE, POLICY_RULESET_FILE, assembly_directories

ANALYZERS = ("WallstopStudios.UnityHelpers.Analyzers.dll", "nunit.analyzers.dll")
GENERATED_NAMES = {
    POLICY_RESPONSE_FILE, POLICY_RULESET_FILE, POLICY_RESPONSE_FILE + ".meta", POLICY_RULESET_FILE + ".meta"
}


def asset_guid(name: str) -> str:
    return uuid.uuid5(uuid.NAMESPACE_URL, f"https://github.com/Ambiguous-Interactive/DxAudio/validation/{name}").hex


def copy_script(source: Path, destination: Path, root: Path) -> None:
    shutil.copy2(source, destination)
    guid = asset_guid(destination.relative_to(root).as_posix())
    destination.with_suffix(".cs.meta").write_text(
        f"fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n  externalObjects: {{}}\n",
        encoding="utf-8",
    )


def write_analyzer_meta(dll: Path) -> None:
    guid = asset_guid(dll.name)
    dll.with_name(dll.name + ".meta").write_text(
        f"""fileFormatVersion: 2
guid: {guid}
labels:
- RoslynAnalyzer
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {{}}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:
""",
        encoding="utf-8",
    )


def stage_analyzers(root: Path, stage: Path) -> None:
    runtime = stage / "Runtime"
    subprocess.run(
        [sys.executable, str(root / ".net-tests" / "prepare_analyzers.py"), "--output-dir", str(runtime), "--unity-nunit"],
        check=True,
    )
    # Unity extends an analyzer's scope to dependent assemblies. Both test
    # assemblies reach Runtime, and NUnit ignores assemblies without NUnit references.
    for name in ANALYZERS:
        dll = runtime / name
        if not dll.is_file():
            raise RuntimeError(f"Analyzer fetch did not produce {name}")
        write_analyzer_meta(dll)


def stage_policy(root: Path, stage: Path) -> None:
    ruleset = root / ".net-tests" / "UnityHelpers.ruleset"
    suppressions = root / ".net-tests" / "WuhSuppressions.cs"
    assemblies = assembly_directories(stage)
    if not assemblies:
        raise RuntimeError("No Unity assembly definitions were staged.")
    for directory in assemblies:
        (directory / POLICY_RESPONSE_FILE).write_text(POLICY_RESPONSE_CONTENT, encoding="utf-8")
        shutil.copy2(ruleset, directory / POLICY_RULESET_FILE)
        if suppressions.is_file():
            # Include the staged folder so sibling fixtures get distinct asset GUIDs.
            copy_script(suppressions, directory / suppressions.name, stage.parent)


def stage_package(root: Path, stage: Path) -> None:
    if stage.resolve().is_relative_to(root.resolve()):
        raise ValueError("The development package must be staged outside the checkout.")
    stage.mkdir(parents=True)
    shutil.copy2(root / "package.json", stage)
    for folder in ("Runtime", "Editor", "Tests"):
        shutil.copytree(root / folder, stage / folder, ignore=shutil.ignore_patterns(*GENERATED_NAMES))
        shutil.copy2(root / (folder + ".meta"), stage)
    stage_analyzers(root, stage)
    stage_policy(root, stage)
    fixture = root / ".net-tests" / "UnityValidation" / "AnalyzerActivation.cs"
    copy_script(fixture, stage / "Tests" / "Editor" / fixture.name, stage)


def stage_sample(root: Path, destination: Path) -> None:
    if destination.resolve().is_relative_to(root.resolve()):
        raise ValueError("The sample must be staged outside the checkout.")
    shutil.copytree(root / "Samples~" / "BasicUsage", destination)
    (destination / "SampleValidation.asmdef").write_text(
        json.dumps({"name": "WallstopStudios.DxAudio.Samples.Validation", "references": ["WallstopStudios.DxAudio"]}),
        encoding="utf-8",
    )
    stage_policy(root, destination)


def stage_docs(root: Path, destination: Path, *, with_integration: bool) -> None:
    if destination.resolve().is_relative_to(root.resolve()):
        raise ValueError("The documentation fixtures must be staged outside the checkout.")
    references = ["WallstopStudios.DxAudio"]
    command = [
        sys.executable, str(root / ".github" / "scripts" / "compile_docs_snippets.py"),
        "--root", str(root), "--target", "unity", "--output", str(destination),
    ]
    if with_integration:
        references.extend(["WallstopStudios.DxAudio.Integration.DxMessaging", "WallstopStudios.DxMessaging"])
        command.append("--with-integration")
    subprocess.run(command, check=True)
    (destination / "DocSnippets.asmdef").write_text(
        json.dumps({
            "name": "WallstopStudios.DxAudio.Documentation.Validation",
            "references": references,
        }),
        encoding="utf-8",
    )
    stage_policy(root, destination)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--sample-destination", type=Path)
    parser.add_argument("--docs-destination", type=Path)
    parser.add_argument("--with-integration", action="store_true")
    arguments = parser.parse_args()
    stage_package(arguments.source.resolve(), arguments.destination.resolve())
    if arguments.sample_destination:
        stage_sample(arguments.source.resolve(), arguments.sample_destination.resolve())
    if arguments.docs_destination:
        stage_docs(arguments.source.resolve(), arguments.docs_destination.resolve(), with_integration=arguments.with_integration)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
