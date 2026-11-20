#!/usr/bin/env python3
"""Audit DxAudio coverage of the pinned unity-helpers checks."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import tempfile
from collections import Counter
from pathlib import Path

from portable_csharp_checks import PORTABLE_PATHS, run_checks as run_portable_checks
from portable_doc_checks import PORTABLE_DOC_PATHS, run_doc_checks
from portable_tool_checks import PORTABLE_TOOL_PATHS, run_tool_checks
from unity_helpers_source_checks import (
    fetch,
    run_analyzer_fixtures,
    run_contract_controls,
    run_source_checks,
    run_unsafe_checks,
    stage_modules,
    stage_paths,
    verify_source_controls,
)

ROOT = Path(__file__).resolve().parents[2]
CHECKS = ROOT / ".github/checks"
INVENTORY = CHECKS / "unity-helpers-inventory.json"
COVERAGE = CHECKS / "unity-helpers-coverage.json"
REGISTRIES = {
    "lint": "scripts/lint-checks.json",
    "contract": "scripts/contracts-checks.json",
    "hook": "scripts/hooks-checks.json",
    "typecheck": "scripts/typecheck-projects.json",
}
DIAGNOSTICS = "Generator~/WallstopStudios.UnityHelpers.Analyzers/UnityHelpersDiagnostics.cs"
CHECK_COMMAND = re.compile(r"^(?:(?:check|lint|test|typecheck|validate|verify)(?::|$)|format:.*:check$|unity:(?:compile|test|validate)(?::|$))")
JOB = re.compile(r"^  ([a-zA-Z0-9_-]+):\s*(?:#.*)?$")
STEP = re.compile(r"^      - ([a-zA-Z0-9_-]+):\s*(.*)$")
STEP_PROPERTY = re.compile(r"^        (name|id|uses|run|if|env|continue-on-error):\s*(.*)$")
JOB_PROPERTY = re.compile(r"^    (if|env|continue-on-error):\s*(.*)$")
USES = re.compile(r"^\s*(?:-\s*)?uses:\s+(\"[^\"]*\"|'[^']*'|[^#\s]+)")
PINNED_ACTION = re.compile(r"@[a-f0-9]{40}$")
PINNED_IMAGE = re.compile(r"@sha256:[a-f0-9]{64}$")
SUPPORT_NAME = re.compile(
    r"^(checkout\b|check out\b|set ?up\b|install\b|cache\b|download\b|upload\b|"
    r"log in\b|enable git long paths\b|restore \.net tools\b|restore dotnet tools\b|"
    r"dump\b|print\b|comment\b|commit\b|publish\b|open or update\b|"
    r"dispatch\b|retain\b|workflow summary\b|deployment summary\b|"
    r"build summary\b|step summary\b|debug build output\b|setup[-_]|upload[-_])",
    re.IGNORECASE,
)
SUPPORT_ACTION = re.compile(
    r"^(actions/(?:checkout|cache|setup-[^/@]+|upload-artifact|download-artifact|"
    r"upload-pages-artifact|configure-pages|deploy-pages|create-github-app-token)|"
    r"docker/(?:setup-[^/@]+|metadata-action|build-push-action|login-action))@"
)
SUPPORT_IDS = {
    "app-token", "artifacts", "cache", "changes", "checkout", "changelog", "clone-wiki",
    "create_pr", "deployment", "dotnet", "formatter-dotnet", "git_changes", "meta", "pack", "pages",
    "policy_pin", "prepare", "publish", "publish_measurements", "reader-token", "release_drafter",
    "retained_protobuf_dotnet", "version", "Workflow summary", "write_summary",
}


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def git(path: Path, *args: str) -> str:
    return subprocess.check_output(["git", "-C", str(path), *args], encoding="utf-8")


def workflow_steps(source: str) -> dict[str, list[dict[str, str]]]:
    """Read the block-style jobs and steps shared by both repositories."""
    jobs: dict[str, list[dict[str, str]]] = {}
    current_job = None
    current_step = None
    inside_jobs = False
    run_indent = None
    for line in source.splitlines():
        if line == "jobs:":
            inside_jobs = True
            continue
        if not inside_jobs or not line.strip() or line.lstrip().startswith("#"):
            continue
        if not line.startswith(" "):
            break
        if run_indent is not None:
            if line.startswith(" " * run_indent):
                current_step["run"] += "\n" + line[run_indent:]
                continue
            run_indent = None
        if match := JOB.fullmatch(line):
            current_job = []
            current_step = None
            jobs[match[1]] = current_job
            continue
        if current_job is None:
            continue
        if match := JOB_PROPERTY.fullmatch(line):
            current_job.append({f"job-{match[1]}": match[2]})
            continue
        if line.startswith("    uses: "):
            current_job.append({"uses": line.partition(": ")[2]})
            continue
        if match := STEP.fullmatch(line):
            current_step = {}
            current_job.append(current_step)
        elif current_step is not None:
            match = STEP_PROPERTY.fullmatch(line)
        if match is None:
            continue
        key, value = match.groups()
        if key == "run" and value in ("|", "|-", "|+", ">", ">-", ">+"):
            run_indent = 8 if line.startswith("      - ") else 10
            value = ""
        value = value.strip()
        current_step[key] = value[1:-1] if value[:1] in ("'", '"') and value[:1] == value[-1:] else value
    return jobs


def inventory(upstream: Path, revision: str) -> dict:
    ids: dict[str, list[str]] = {}
    digests: dict[str, str] = {}
    invocations: dict[str, set[str]] = {}
    aliases: dict[str, dict[str, str]] = {}

    def read(path: str) -> str:
        contents = subprocess.check_output(["git", "-C", str(upstream), "show", f"{revision}:{path}"])
        digests[path] = hashlib.sha256(contents).hexdigest()
        return contents.decode("utf-8-sig")

    for kind, path in REGISTRIES.items():
        records = json.loads(read(path))
        ids[kind] = sorted(row["id"] for row in records)
        for row in records:
            if "run" in row:
                invocations.setdefault(row["run"].strip(), set()).add(f"{kind}/{row['id']}")
    ids["diagnostic"] = sorted(re.findall(r'"(WUH[0-9]{3})"', read(DIAGNOSTICS)))
    scripts = json.loads(read("package.json"))["scripts"]
    ids["command"] = []
    command_operations = []
    for name in sorted(scripts):
        if not CHECK_COMMAND.match(name):
            continue
        if name.endswith((":fix", ":add")):
            command_operations.append(name)
            continue
        invocation = f"npm run {name}"
        candidates = invocations.get(invocation, set())
        if len(candidates) == 1:
            aliases.setdefault("command", {})[name] = next(iter(candidates))
        else:
            ids["command"].append(name)
            if not candidates:
                invocations[invocation] = {f"command/{name}"}
    operations: dict[str, list[str]] = {}
    paths = git(upstream, "ls-tree", "-r", "--name-only", revision, ".github/workflows").splitlines()
    for path in paths:
        if Path(path).suffix not in (".yml", ".yaml"):
            continue
        jobs = workflow_steps(read(path))
        if not jobs:
            raise ValueError(f"Could not read jobs in {path}")
        for name, steps in jobs.items():
            scope = f"workflow:{Path(path).stem}/{name}"
            commands = []
            support = []
            inherits_environment = any("job-env" in step for step in steps)
            executed = (step for step in steps if "run" in step or "uses" in step)
            for index, step in enumerate(executed, 1):
                label = step.get("id", step.get("name", step.get("uses", "run")))
                item = f"{index} {label}"
                if is_support_operation(step):
                    support.append(item)
                    continue
                invocation = step.get("run", "").strip()
                candidates = invocations.get(invocation, set())
                if len(candidates) == 1 and not inherits_environment and "env" not in step and not step.get("if"):
                    aliases.setdefault(scope, {})[item] = next(iter(candidates))
                    continue
                commands.append(item)
            if commands:
                ids[scope] = commands
            if support:
                operations[scope] = support
    return {
        "revision": revision, "sources": digests, "checks": ids, "aliases": aliases,
        "workflow_operations": operations, "command_operations": command_operations,
    }


def is_support_operation(step: dict[str, str]) -> bool:
    """Exclude setup, transport, publication and reporting from check coverage."""
    label = step.get("name", step.get("id", ""))
    if re.search(r"\b(verify|validate|require|check|assert|recheck|gate)\b", label, re.IGNORECASE):
        return False
    return bool(
        SUPPORT_ACTION.match(step.get("uses", ""))
        or SUPPORT_NAME.match(label)
        or step.get("id") in SUPPORT_IDS
    )


def action_pin_violations(root: Path) -> list[str]:
    violations = []
    for path in sorted((root / ".github/workflows").glob("*.y*ml")):
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            match = USES.match(line)
            if match is None:
                continue
            reference = match[1].strip("'\042")
            if reference.startswith("./"):
                continue
            pinned = PINNED_IMAGE if reference.startswith("docker://") else PINNED_ACTION
            if not pinned.search(reference):
                violations.append(f"{path.relative_to(root).as_posix()}:{number} has mutable action {reference}")
    return violations


def workflow_command_exists(root: Path, declaration: dict) -> bool:
    path = root / declaration["path"]
    if not path.is_file():
        return False
    steps = workflow_steps(path.read_text(encoding="utf-8")).get(declaration["job"], ())
    disabled = ("false", "${{ false }}")
    optional = ("true", "${{ true }}")
    if any(step.get("job-if") in disabled or step.get("job-continue-on-error") in optional for step in steps):
        return False
    field = "uses" if "uses" in declaration else "run"
    return any(
        declaration[field] in (line.strip() for line in step.get(field, "").splitlines())
        and step.get("if") not in disabled
        and step.get("continue-on-error") not in optional
        for step in steps
    )


def audit(root: Path, expected: dict, coverage: dict) -> tuple[list[str], set[tuple[str, str]], Counter]:
    known = {(kind, name) for kind, names in expected["checks"].items() for name in names}
    if len(known) != sum(len(names) for names in expected["checks"].values()):
        raise ValueError("duplicate upstream check IDs")
    problems = []
    mapped: set[tuple[str, str]] = set()
    pending: set[tuple[str, str]] = set()
    totals: Counter = Counter()
    if coverage["revision"] != expected["revision"]:
        problems.append("upstream revision differs between inventory and coverage")
    gates = coverage["gates"]
    for name, declaration in sorted(gates.items()):
        if not workflow_command_exists(root, declaration):
            problems.append(f"gate {name!r} is absent from {declaration['path']} job {declaration['job']!r}")
        for file in declaration.get("files", []):
            if not (root / file).is_file():
                problems.append(f"gate {name!r} has missing input {file}")
    for row in coverage["mappings"]:
        status = row["status"]
        if status == "enforced":
            if row.get("gate") not in gates or not row.get("evidence"):
                problems.append(f"enforced coverage lacks a gate or evidence: {row['checks']}")
        elif status not in ("inapplicable", "pending") or not row.get("reason", "").strip():
            problems.append(f"coverage requires a gate, a missing prerequisite, or outstanding work: {row['checks']}")
        for kind, names in row["checks"].items():
            for name in names:
                check = (kind, name)
                if check not in known:
                    problems.append(f"unknown check {kind}/{name}")
                if check in mapped:
                    problems.append(f"duplicate mapping {kind}/{name}")
                mapped.add(check)
                if status == "pending":
                    pending.add(check)
                totals[status] += 1
    for kind, name in sorted(known - mapped):
        problems.append(f"upstream check {kind}/{name} has no decision")
    totals["unmapped"] = len(known - mapped)
    canonical = {f"{kind}/{name}" for kind, name in known}
    for kind, aliases in expected.get("aliases", {}).items():
        for name, target in aliases.items():
            if target not in canonical or (kind, name) in known:
                problems.append(f"invalid upstream alias {kind}/{name} → {target}")
            totals["aliases"] += 1
    return problems, pending | (known - mapped), totals


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--upstream", type=Path, help="verify the inventory against a Git checkout containing the pin")
    parser.add_argument("--write-inventory", action="store_true", help="regenerate inventory from the pinned Git checkout")
    parser.add_argument("--require-complete", action="store_true", help="fail for every remaining unmapped check")
    parser.add_argument("--list-pending", action="store_true")
    parser.add_argument("--validate", action="store_true", help="verify the pin and run applicable upstream source checks")
    parser.add_argument("--analyzer-fixtures", action="store_true", help="run the pinned upstream analyzer fixtures with --validate")
    parser.add_argument("--lint-action-pins", action="store_true", help="enforce immutable workflow action references")
    options = parser.parse_args()
    if options.analyzer_fixtures and not options.validate:
        parser.error("--analyzer-fixtures requires --validate")
    try:
        if options.lint_action_pins:
            problems = action_pin_violations(ROOT)
            for problem in problems:
                print(problem, file=sys.stderr)
            return int(bool(problems))
        coverage = read_json(COVERAGE)
        if re.fullmatch(r"[a-f0-9]{40}", coverage["revision"]) is None:
            raise ValueError("unity-helpers revision must be a full immutable Git commit SHA")
        if options.write_inventory:
            if options.upstream is None:
                parser.error("--write-inventory requires --upstream")
            generated = inventory(options.upstream, coverage["revision"])
            INVENTORY.write_text(json.dumps(generated, indent=2) + "\n", encoding="utf-8")
        expected = read_json(INVENTORY)
        problems, pending, totals = audit(ROOT, expected, coverage)
        if options.validate:
            with tempfile.TemporaryDirectory(prefix="dxaudio-unity-helpers-") as directory:
                scratch = Path(directory)
                upstream = options.upstream or scratch / "upstream"
                if options.upstream is None:
                    fetch(upstream, coverage["revision"])
                if inventory(upstream, coverage["revision"]) != expected:
                    problems.append("the pinned upstream inputs differ from the recorded inventory")
                subprocess.run([sys.executable, str(CHECKS / "test_unity_helpers_coverage.py")], check=True)
                staged = scratch / "sources"
                stage_modules(upstream, coverage["revision"], staged)
                stage_paths(upstream, coverage["revision"], staged, PORTABLE_PATHS)
                stage_paths(upstream, coverage["revision"], staged, PORTABLE_DOC_PATHS)
                stage_paths(upstream, coverage["revision"], staged, PORTABLE_TOOL_PATHS)
                problems.extend(action_pin_violations(ROOT))
                modules = staged / "scripts"
                problems.extend(verify_source_controls(scratch / "controls", modules))
                problems.extend(run_source_checks(ROOT, modules))
                problems.extend(run_portable_checks(ROOT, modules, staged))
                problems.extend(run_doc_checks(ROOT, modules, staged))
                problems.extend(run_tool_checks(ROOT, modules, staged))
                if not problems:
                    run_contract_controls(ROOT, staged)
                if options.analyzer_fixtures and not problems:
                    run_unsafe_checks(ROOT, upstream, coverage["revision"], scratch / "fixtures")
                    run_analyzer_fixtures(upstream, coverage["revision"], scratch / "fixtures")
        elif options.upstream is not None and inventory(options.upstream, coverage["revision"]) != expected:
            problems.append("the pinned upstream inputs differ from the recorded inventory")
        if options.require_complete and pending:
            problems.append(f"{len(pending)} upstream checks still need a coverage decision or enforcement")
        for problem in problems:
            print(f"[unity-helpers-coverage] {problem}", file=sys.stderr)
        if options.list_pending:
            for kind, name in sorted(pending):
                print(f"pending {kind}/{name}")
        print(f"[unity-helpers-coverage] {totals['enforced']} enforced, "
              f"{totals['inapplicable']} inapplicable, {totals['pending']} recorded pending, "
              f"{totals['unmapped']} unmapped, {totals['aliases']} aliases, "
              f"{sum(len(items) for items in expected['workflow_operations'].values())} workflow operations")
        return int(bool(problems))
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(f"[unity-helpers-coverage] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
