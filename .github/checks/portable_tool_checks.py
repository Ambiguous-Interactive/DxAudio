"""Apply pinned unity-helpers workflow, path and devcontainer checks to DxAudio."""

from __future__ import annotations

import argparse
import importlib
import json
import re
import subprocess
import sys
import tempfile
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch

PORTABLE_TOOL_PATHS = (
    "scripts/lint_path_normalization.py",
    "scripts/lint_workflow_expression_length.py",
    "scripts/validate_devcontainer_config.py",
    "scripts/validate_devcontainer_urls.py",
)
SOURCE_ROOTS = ("Runtime", "Editor", "Tests", "Samples~", ".net-tests")
PRETTIER_LANGUAGES = ("javascript", "json", "jsonc", "markdown", "yaml")
MANUAL_LANGUAGES = ("powershell", "shellscript", "xml")
JSON_STRING = r'"(?:\\.|[^"\\])*"'
JSONC_COMMENT = re.compile(JSON_STRING + r"|//[^\r\n]*|/\*.*?\*/", re.DOTALL)
JSONC_TRAILING_COMMA = re.compile(JSON_STRING + r"|,(?=\s*[}\]])")


def inspect_formatters(text: str) -> list[str]:
    content = JSONC_COMMENT.sub(lambda m: m[0] if m[0].startswith('"') else "", text)
    config = json.loads(JSONC_TRAILING_COMMA.sub(lambda m: m[0] if m[0].startswith('"') else "", content))
    code = config["customizations"]["vscode"]
    settings = code["settings"]
    expected = {"csharp": "csharpier.csharpier"}
    expected.update({name: "esbenp.prettier-vscode" for name in PRETTIER_LANGUAGES})
    problems = []
    for language, formatter in expected.items():
        if formatter not in code["extensions"] or settings.get(f"[{language}]", {}).get("editor.defaultFormatter") != formatter:
            problems.append(f".devcontainer/devcontainer.json: [{language}] must select the installed {formatter} formatter")
    for language in MANUAL_LANGUAGES:
        if settings.get(f"[{language}]", {}).get("editor.formatOnSave") is not False:
            problems.append(f".devcontainer/devcontainer.json: [{language}] must explicitly disable format on save")
    return problems


def workflow_control(workflows: Path) -> None:
    command = [
        "uv", "run", "--no-project", "--with", "pyyaml==6.0.3", "python", str(workflows),
    ]
    with tempfile.TemporaryDirectory(prefix="dxaudio-workflow-control-") as folder:
        root = Path(folder)
        workflow = root / ".github" / "workflows" / "control.yml"
        workflow.parent.mkdir(parents=True)
        base = "jobs:\n  control:\n    steps:\n      - run: |\n          echo "
        stable = "${{ env.REF }}"
        workflow.write_text(base + stable + "\n", encoding="utf-8")
        subprocess.run(command + ["--repo-root", folder], check=True, capture_output=True, text=True)
        workflow.write_text(base + stable + "a" * 21_000 + "\n", encoding="utf-8")
        negative = subprocess.run(command + ["--repo-root", folder], check=False, capture_output=True, text=True)
        if negative.returncode == 0 or "WFL001" not in negative.stdout:
            raise ValueError("the workflow run expression limit missed its negative control")


def run_path_checks(root: Path, normalizations: object, csharp: object) -> list[str]:
    if normalizations.find_manual_normalizations(r"string p = Path.GetFileName(input);"):
        raise ValueError("the path normalization policy rejected its positive control")
    if not normalizations.find_manual_normalizations(r"string p = input.Replace('\\', '/');"):
        raise ValueError("the path normalization policy missed its negative control")

    subjects = csharp.source_files(root, SOURCE_ROOTS)
    if not subjects:
        raise ValueError("the path normalization policy found no C# inputs")
    print(f"[unity-helpers-paths] inspected {len(subjects)} C# files")
    return [
        f"{path.relative_to(root).as_posix()}:{line}: use a canonical asset path without platform-separator replacement"
        for path in subjects
        for line in normalizations.find_manual_normalizations(path.read_text(encoding="utf-8-sig"))
    ]


def run_devcontainer_checks(root: Path, configuration: object, downloads: object) -> list[str]:
    # DxAudio builds a local devcontainer, so only the portable configuration
    # and formatter portions of upstream's publisher policy apply here.
    dockerfile = root / ".devcontainer" / "Dockerfile"
    docker_lines = configuration.read_text(dockerfile).splitlines()
    valid_config = configuration.read_text(root / ".devcontainer" / "devcontainer.json")
    problems = inspect_formatters(valid_config)

    if not inspect_formatters(valid_config.replace('"[csharp]"', '"[wrong]"')):
        raise ValueError("the devcontainer formatter check missed its negative control")
    if downloads.apt_installs(["RUN apt-get install -y curl"]) or not downloads.apt_installs(
        ["RUN apt-get install -y \\", "    powershell"]
    ):
        raise ValueError("the devcontainer architecture check did not distinguish its controls")
    # Exercise a missing asset without relying on a live 404 or curl's retry
    # timing. The actual pinned assets below provide the positive control.
    missing_asset = {
        "run_curl": Mock(return_value=(0, "404")),
        "supports_bounded_get": Mock(return_value=False),
        "time": SimpleNamespace(sleep=lambda _: None),
    }
    with patch.multiple(downloads, **missing_asset), redirect_stdout(StringIO()):
        if downloads.check_url("control", "https://github.com/missing-release.tar.gz"):
            raise ValueError("the devcontainer asset check accepted a missing release")
    problems.extend(f".devcontainer/Dockerfile:{line}: PowerShell must use pinned release archives" for line in downloads.apt_installs(docker_lines))
    tools = downloads.extract_tools(docker_lines)
    powershell = [tool for tool in tools if tool.name == "powershell"]
    if len(powershell) != 1 or (powershell[0].amd64, powershell[0].arm64) != ("x64", "arm64"):
        problems.append(".devcontainer/Dockerfile: expected one versioned PowerShell download with x64 and arm64 assets")
    if not problems and downloads.validate_downloads(powershell):
        problems.append(".devcontainer/Dockerfile: a PowerShell asset could not be downloaded")
    return problems


def run_tool_checks(root: Path, modules: Path, upstream_tree: Path) -> list[str]:
    if str(modules) not in sys.path:
        sys.path.insert(0, str(modules))
    sys.dont_write_bytecode = True
    normalizations = importlib.import_module("lint_path_normalization")
    csharp = importlib.import_module("csharp_sources")
    configuration = importlib.import_module("validate_devcontainer_config")
    downloads = importlib.import_module("validate_devcontainer_urls")

    problems = run_path_checks(root, normalizations, csharp)
    problems.extend(run_devcontainer_checks(root, configuration, downloads))

    workflow = upstream_tree / "scripts" / "lint_workflow_expression_length.py"
    workflow_control(workflow)
    command = [
        "uv", "run", "--no-project", "--with", "pyyaml==6.0.3", "python", str(workflow),
        "--repo-root", str(root),
    ]
    if subprocess.run(command, check=False).returncode:
        problems.append("the workflow run expression length check failed")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("upstream_tree", type=Path, help="staged immutable unity-helpers tree")
    args = parser.parse_args()
    problems = run_tool_checks(Path(__file__).resolve().parents[2], args.upstream_tree / "scripts", args.upstream_tree)
    for problem in problems:
        print(problem)
    return int(bool(problems))


if __name__ == "__main__":
    raise SystemExit(main())
