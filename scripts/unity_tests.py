#!/usr/bin/env python3
"""Run the DxAudio Unity tests on the development host.

In a devcontainer, the default EditMode suite runs through the host Unity MCP
bridge. On the host, during PlayMode runs, or with explicit local batchmode
options, the command writes artifacts through ``run_unity_validation.py``.

Example:
    npm run unity:tests
    uv run scripts/unity_tests.py --suite playmode --host-project C:/Code/DxAudio
"""

from __future__ import annotations

import argparse
import json
import math
import ntpath
import os
import re
import sys
from pathlib import Path
from typing import Dict, Optional, Sequence

from unity_mcp_doctor import Json, JsonObject, object_value, request_json
from unity_mcp_settings import credential_values, read_env_file, redact_credentials

REPO_ROOT = Path(__file__).resolve().parent.parent
PACKAGE_DIRECTORY = REPO_ROOT
VALIDATION_SCRIPT = REPO_ROOT / "scripts" / "run_unity_validation.py"
DEFAULT_OUTPUT = Path(os.environ.get("TEMP") or "/tmp") / "dxaudio-validation"


def in_container() -> bool:
    flag = os.environ.get("DXAUDIO_IN_CONTAINER")
    if flag is not None:
        return flag == "1"
    return Path("/.dockerenv").exists() or Path("/run/.containerenv").exists()


def run_bridge_editmode(env: Dict[str, str]) -> int:
    def setting(name: str, default: str = "") -> str:
        return os.environ.get(name) or env.get(name) or default

    pin = next(
        (value.strip() for source in (os.environ, env)
         for name in ("UNITY_MCP_PROJECT", "UNITY_PROJECT_PATH")
         if (value := source.get(name)) and value.strip()),
        "",
    )
    port = setting("UNITY_MCP_HTTP_PORT", "9020")
    endpoint = setting("UNITY_MCP_HTTP_URL", f"http://host.docker.internal:{port}/mcp").rstrip("/")
    headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
    token = setting("UNITY_MCP_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"

    def rpc(method: str, params: JsonObject, identifier: Optional[int] = None, timeout: float = 30) -> JsonObject:
        payload: JsonObject = {"jsonrpc": "2.0", "method": method, "params": params}
        if identifier is not None:
            payload["id"] = identifier
        response = request_json(endpoint, headers, payload, timeout=timeout)
        if "error" in response:
            raise ValueError(f"{method}: {response['error']}")
        return object_value(response.get("result"))

    def tool(name: str, arguments: JsonObject, identifier: int, timeout: float = 30) -> Json:
        response = rpc("tools/call", {"name": name, "arguments": arguments}, identifier, timeout)
        content = response.get("content")
        raw = object_value(content[0]).get("text") if isinstance(content, list) and content else None
        if response.get("isError") or not isinstance(raw, str):
            raise ValueError(f"{name}: {raw or 'missing tool result'}")
        result: Json = json.loads(raw)
        return result

    try:
        if not pin:
            raise ValueError("Set UNITY_MCP_PROJECT to the host Unity project root before running EditMode.")
        bridge_seconds = float(setting("UNITY_MCP_TIMEOUT_MS", "180000")) / 1000
        if not math.isfinite(bridge_seconds) or bridge_seconds < 2:
            raise ValueError("Set UNITY_MCP_TIMEOUT_MS to a finite value of at least 2000.")
        server = rpc("initialize", {
            "protocolVersion": "2025-03-26",
            "capabilities": {},
            "clientInfo": {"name": "dxaudio-unity-tests", "version": "0.1.0"},
        }, 1)
        if not server.get("serverInfo"):
            raise ValueError("MCP initialize returned no server information.")
        rpc("notifications/initialized", {})
        editor = object_value(tool("editor_status", {}, 2))
        project = editor.get("projectPath")
        if not isinstance(project, str) or not project:
            raise ValueError("The bridge did not identify its Unity project.")
        path_rules = ntpath if ntpath.splitdrive(project)[0] else os.path
        if path_rules.normcase(path_rules.normpath(project)) != path_rules.normcase(path_rules.normpath(pin)):
            raise ValueError(f"Editor project {project!r} differs from pin {pin!r}. Check UNITY_MCP_PROJECT.")
        if editor.get("status") != "ready" or editor.get("playMode") != "stopped":
            raise ValueError(f"Wait for the host editor to be ready in EditMode: {editor}.")

        # Request this run's result directly. test_status has no run identifier.
        results = tool("test_run", {"mode": "editor", "timeout": int(bridge_seconds) - 1}, 3, bridge_seconds + 5)
        if not isinstance(results, list) or not results:
            raise ValueError(f"EditMode returned no NUnit results: {results}.")
        counts = {"Passed": 0, "Failed": 0, "Skipped": 0, "Inconclusive": 0}
        failures = []
        for entry in results:
            row = object_value(entry)
            name, status = row.get("fullName"), row.get("status")
            if not isinstance(name, str) or not isinstance(status, str) or status not in counts:
                raise ValueError(f"Invalid NUnit test result for {name!r}: {status!r}.")
            counts[status] += 1
            if status == "Failed":
                failures.append(f"{name}: {row.get('message') or 'Open the host Unity Test Runner for details.'}")
        print(
            f"Unity tests: {counts['Passed']}/{len(results)} passed, {counts['Failed']} failed, "
            f"{counts['Skipped']} skipped, {counts['Inconclusive']} inconclusive."
        )
        credentials = credential_values((os.environ, env))
        for failure in failures:
            print(redact_credentials(failure, credentials), file=sys.stderr)
        return 1 if failures else 0
    except (OSError, ValueError) as error:
        message = (
            f"Unity MCP EditMode failed at {endpoint}: {error}\n"
            "Check the open host project and bridge settings with npm run unity:mcp:doctor. "
            "Start the bridge on the host with npm run unity:mcp:host."
        )
        print(redact_credentials(message, credential_values((os.environ, env))), file=sys.stderr)
        return 1


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
    parser.add_argument("--output-directory", help="Local batchmode evidence directory outside the package")
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
    env = read_env_file(REPO_ROOT / ".env.local")
    if (
        options.suite == "editmode"
        and in_container()
        and not (options.host_project or options.unity_executable or options.output_directory)
    ):
        return run_bridge_editmode(env)

    project = resolve_project(options.host_project, env)
    version = project_unity_version(project)
    editor = resolve_editor(options.unity_executable, version, env)
    output = Path(os.path.abspath(options.output_directory or DEFAULT_OUTPUT))
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
