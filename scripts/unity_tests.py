#!/usr/bin/env python3
"""Run the DxAudio Unity EditMode suite on the development host.

Resolves the enclosing Unity project, the editor version it declares, and the
editor executable, then delegates to ``run_unity_validation.py`` with an output
directory outside the package repository. Prints the NUnit totals on success.

Example:
    npm run unity:tests
    uv run scripts/unity_tests.py --suite playmode --host-project C:/Code/DxAudio
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path
from typing import Dict, Optional, Sequence

REPO_ROOT = Path(__file__).resolve().parent.parent
PACKAGE_DIRECTORY = REPO_ROOT
VALIDATION_SCRIPT = REPO_ROOT / "scripts" / "run_unity_validation.py"
DEFAULT_OUTPUT = Path(os.environ.get("TEMP") or "/tmp") / "dxaudio-validation"


def read_env_local(path: Path) -> Dict[str, str]:
    """Parse a dotenv file as data; never execute it."""
    values: Dict[str, str] = {}
    if not path.is_file():
        return values
    for raw_line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
            value = value[1:-1]
        if key:
            values[key] = value
    return values


def resolve_project(explicit: Optional[str], env: Dict[str, str]) -> Path:
    """Resolve the Unity project root that contains this package."""
    for candidate in (
        explicit,
        env.get("UNITY_MCP_PROJECT"),
        env.get("UNITY_PROJECT_PATH"),
        os.environ.get("UNITY_MCP_PROJECT"),
        os.environ.get("UNITY_PROJECT_PATH"),
    ):
        if candidate and candidate.strip():
            return Path(os.path.abspath(candidate.strip()))
    # Packages/<package> -> the enclosing Unity project.
    return PACKAGE_DIRECTORY.parent.parent


def project_unity_version(project: Path) -> str:
    version_file = project / "ProjectSettings" / "ProjectVersion.txt"
    if not version_file.is_file():
        raise SystemExit(f"Not a Unity project (missing {version_file}). Pass --host-project.")
    match = re.search(r"(?m)^m_EditorVersion:\s*(\S+)\s*$", version_file.read_text(encoding="utf-8-sig"))
    if match is None:
        raise SystemExit(f"No m_EditorVersion in {version_file}.")
    return match.group(1)


def candidate_editors(version: str) -> Sequence[Path]:
    if sys.platform == "win32":
        roots = [
            Path(r"C:\Program Files\Unity\Hub\Editor"),
            Path(os.environ.get("UNITY_HUB_EDITOR_PATH") or ""),
        ]
        return [root / version / "Editor" / "Unity.exe" for root in roots if str(root)]
    if sys.platform == "darwin":
        return [Path("/Applications/Unity/Hub/Editor") / version / "Unity.app/Contents/MacOS/Unity"]
    return [Path.home() / "Unity/Hub/Editor" / version / "Editor/Unity"]


def resolve_editor(explicit: Optional[str], version: str, env: Dict[str, str]) -> Path:
    for candidate in (explicit, env.get("UNITY_EDITOR_PATH"), os.environ.get("UNITY_EDITOR_PATH")):
        if candidate and candidate.strip():
            path = Path(os.path.abspath(candidate.strip()))
            if path.is_file():
                return path
            raise SystemExit(f"UNITY_EDITOR_PATH is not a file: {path}")
    for path in candidate_editors(version):
        if path.is_file():
            return path
    raise SystemExit(f"Unity {version} is not installed at the standard Hub path. Set UNITY_EDITOR_PATH.")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="unity-tests", description=__doc__)
    parser.add_argument("--suite", choices=("editmode", "playmode"), default="editmode")
    parser.add_argument("--host-project", default=None, help="Unity project root; default: enclosing project or .env.local")
    parser.add_argument("--unity-executable", default=None, help="Editor executable; default: UNITY_EDITOR_PATH or the Hub path")
    parser.add_argument("--output-directory", default=str(DEFAULT_OUTPUT), help="Evidence directory outside the package")
    return parser


def totalling(results_path: Path) -> str:
    """Return one summary line from an NUnit result file."""
    if not results_path.is_file():
        return "No NUnit result file was written."
    text = results_path.read_text(encoding="utf-8", errors="replace")
    match = re.search(r'<test-run[^>]*total="(\d+)"[^>]*passed="(\d+)"[^>]*failed="(\d+)"', text)
    if match is None:
        return f"Wrote {results_path}."
    total, passed, failed = match.groups()
    return f"Unity tests: {passed}/{total} passed, {failed} failed. Results: {results_path}"


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = build_parser().parse_args(argv)
    env = read_env_local(REPO_ROOT / ".env.local")
    project = resolve_project(options.host_project, env)
    version = project_unity_version(project)
    editor = resolve_editor(options.unity_executable, version, env)
    output = Path(os.path.abspath(options.output_directory))
    output.mkdir(parents=True, exist_ok=True)

    sys.path.insert(0, str(VALIDATION_SCRIPT.parent))
    import run_unity_validation  # noqa: PLC0415 - resolved from this script's directory

    namespace = argparse.Namespace(
        host_project=str(project),
        unity_executable=str(editor),
        unity_version=version,
        suite=options.suite,
        output_directory=str(output),
        timeout_seconds=900,
        plan_only=False,
    )
    exit_code = run_unity_validation.run(namespace)
    print(totalling(output / f"{options.suite}-results.xml"))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
