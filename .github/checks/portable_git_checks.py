"""Check ignored documentation and machine-local MCP config inventory without writing either."""

from __future__ import annotations

import argparse
import importlib
import json
import os
import shlex
import subprocess
import sys
import tempfile
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
from urllib.parse import urlsplit
from unittest.mock import patch

PORTABLE_GIT_PATHS = ("scripts/lint_gitignore_docs.py",)
MCP_JSON = {
    ".mcp.json": ("mcpServers",),
    ".cursor/mcp.json": ("mcpServers",),
    ".vscode/mcp.json": ("servers",),
    "opencode.json": ("mcp", "servers"),
    ".nanocoder/mcp.json": ("mcpServers",),
    ".copilot/mcp-config.json": ("mcpServers",),
    ".claude/settings.local.json": (),
    "agents.config.json": (),
}
CODEX_TOML = ".codex/config.toml"
LOCAL_CONFIGS = (*MCP_JSON, CODEX_TOML, "opencode.jsonc", ".env.local")


def check_ignored_documentation(root: Path, documents: object) -> int:
    original_paths = documents.protected_paths

    def paths(repository: Path) -> list[str]:
        selected = original_paths(repository)
        for name in ("Documentation~", "Samples~"):
            folder = repository / name
            if folder.is_symlink():
                raise ValueError(f"Cannot inspect linked documentation directory: {name}")
            selected.extend(
                path.relative_to(repository).as_posix() for path in folder.rglob("*")
                if path.is_file() or path.is_symlink()
            )
        selected.extend(name for name in ("README.md", "CHANGELOG.md") if (repository / name).is_file())
        return sorted(set(selected))

    with patch.object(documents, "protected_paths", paths):
        return documents.check(root, verbose=True)


def inventory(root: Path, documents: object) -> list[str]:
    payload = b"\0".join(os.fsencode(path) for path in LOCAL_CONFIGS) + b"\0"
    tracked = set(documents.nul_paths(documents.git_command(root, ["ls-files", "--cached", "-z", "--", *LOCAL_CONFIGS])))
    ignored = set(documents.nul_paths(documents.git_command(
        root, ["-c", f"core.excludesFile={os.devnull}", "check-ignore", "-z", "--stdin"], payload, (0, 1)
    )))
    return [
        f"{path}: machine-local MCP input must be gitignored and untracked"
        for path in LOCAL_CONFIGS if path in tracked or path not in ignored
    ]


def mapping(value: object) -> dict:
    if not isinstance(value, dict):
        raise ValueError("expected an object")
    return value


def unity_endpoint(entry: object) -> None:
    server = mapping(entry)
    url = server.get("url")
    if url is None:
        # VS Code's generated mcp-remote command holds the endpoint as a
        # quoted shell argument. Split it as data and never run that command.
        command = server.get("command")
        arguments = server.get("args")
        if isinstance(command, str) and isinstance(arguments, list):
            command = [command, *arguments]
        if not isinstance(command, list) or not command or not all(isinstance(arg, str) for arg in command):
            raise ValueError("expected an HTTP bridge endpoint")
        args = command if "mcp-remote" in command else shlex.split(command[-1])
        index = args.index("mcp-remote") + 1
        url = args[index]
    if not isinstance(url, str):
        raise ValueError("expected an HTTP bridge endpoint")
    parsed = urlsplit(url)
    if parsed.scheme not in ("http", "https") or not parsed.hostname or not parsed.path.rstrip("/").endswith("/mcp"):
        raise ValueError("expected an HTTP endpoint ending in /mcp")


def present_mcp_problems(root: Path, backend: object, toml: object) -> list[str]:
    problems = []
    for name, keys in MCP_JSON.items():
        path = root / name
        if not path.exists() and not path.is_symlink():
            continue
        try:
            if path.is_symlink() or not path.is_file():
                raise ValueError("expected a regular config file")
            value = backend.read_object(path)
            for key in keys:
                value = mapping(value.get(key))
            for key in ("unity", "unity-mcp-remote"):
                if keys and key in value:
                    unity_endpoint(value[key])
        except (OSError, UnicodeError, ValueError, RecursionError, IndexError):
            problems.append(f"{name}: invalid MCP JSON or Unity endpoint")

    path = root / CODEX_TOML
    if path.exists() or path.is_symlink():
        try:
            if path.is_symlink() or not path.is_file():
                raise ValueError("expected a regular config file")
            contents = toml.loads(path.read_text(encoding="utf-8-sig"))
            servers = mapping(contents.get("mcp_servers", {}))
            for key in ("unity", "unity_mcp_remote"):
                if key in servers:
                    unity_endpoint(servers[key])
        except (OSError, UnicodeError, ValueError, RecursionError, IndexError):
            problems.append(f"{CODEX_TOML}: invalid MCP TOML or Unity endpoint")
    return problems


def controls(documents: object, backend: object, toml: object) -> None:
    with tempfile.TemporaryDirectory(prefix="dxaudio-git-controls-") as folder:
        root = Path(folder)
        subprocess.run(["git", "init", "--quiet", "--template=", folder], check=True)
        ignore = root / ".gitignore"
        ignore.write_text("".join(f"/{path}\n" for path in LOCAL_CONFIGS), encoding="utf-8")
        doc = root / "Documentation~" / "Reader.md"
        doc.parent.mkdir()
        doc.write_text("# Reader\n", encoding="utf-8")
        config = root / ".mcp.json"
        config.write_text(json.dumps({"mcpServers": {"unity": {"url": "http://example.test/mcp"}}}), encoding="utf-8")
        vscode = root / ".vscode/mcp.json"
        vscode.parent.mkdir()
        vscode.write_text(json.dumps({"servers": {"unity": {
            "command": "bash", "args": ["-c", "exec npx -y mcp-remote 'http://example.test/mcp'"]
        }}}), encoding="utf-8")
        code = root / CODEX_TOML
        code.parent.mkdir()
        code.write_text('[mcp_servers.unity]\nurl = "http://example.test/mcp"\n', encoding="utf-8")
        with redirect_stdout(StringIO()):
            if check_ignored_documentation(root, documents):
                raise ValueError("Gitignore docs lint rejected its positive control")
        if inventory(root, documents) or present_mcp_problems(root, backend, toml):
            raise ValueError("MCP config lint rejected a valid ignored inventory")

        ignore.write_text(ignore.read_text(encoding="utf-8") + "/Documentation~/\n", encoding="utf-8")
        output = StringIO()
        with redirect_stdout(output):
            failed = check_ignored_documentation(root, documents)
        if not failed or "Protected file is gitignored: Documentation~/Reader.md" not in output.getvalue():
            raise ValueError("Gitignore docs lint missed a hidden product document")

        subprocess.run(["git", "-C", folder, "add", "-f", "--", ".mcp.json"], check=True)
        ignore.write_text(ignore.read_text(encoding="utf-8").replace("/.env.local\n", ""), encoding="utf-8")
        inventory_failures = inventory(root, documents)
        if not any(problem.startswith(".mcp.json:") for problem in inventory_failures) or not any(
            problem.startswith(".env.local:") for problem in inventory_failures
        ):
            raise ValueError("MCP config lint missed a tracked or unignored machine-local input")
        secret = "synthetic-credential-must-stay-private"
        config.write_text('{"mcpServers": {"unity": "' + secret, encoding="utf-8")
        code.write_text('[mcp_servers.unity]\nurl = "https://example.test/wrong?token=' + secret + '"\n', encoding="utf-8")
        invalid = present_mcp_problems(root, backend, toml)
        if len(invalid) != 2 or secret in "\n".join(invalid):
            raise ValueError("MCP config lint missed malformed JSON or an invalid Unity endpoint, or printed a value")


def run_git_checks(root: Path, modules: Path, upstream_tree: Path) -> list[str]:
    command = ["uv", "run", "--no-project", "--python", sys.executable, "--with", "pyyaml==6.0.3"]
    if sys.version_info < (3, 11):
        command += ["--with", "tomli==2.0.2"]
    result = subprocess.run(
        [*command, "python", str(Path(__file__).resolve()), str(modules), "--repo-root", str(root)],
        check=False,
    )
    return ["ignored documentation or generated MCP config validation failed"] if result.returncode else []


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("modules", type=Path, help="staged immutable unity-helpers script directory")
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    root = args.repo_root
    sys.dont_write_bytecode = True
    sys.path[:0] = [str(args.modules.resolve()), str(root / ".devcontainer")]
    documents = importlib.import_module("lint_gitignore_docs")
    backend = importlib.import_module("backend_state")
    toml = importlib.import_module("tomllib" if sys.version_info >= (3, 11) else "tomli")
    controls(documents, backend, toml)
    docs_status = check_ignored_documentation(root, documents)
    problems = inventory(root, documents) + present_mcp_problems(root, backend, toml)
    for issue in problems:
        print(issue)
    if not problems:
        print(f"[unity-helpers-mcp] checked {len(LOCAL_CONFIGS)} machine-local inputs and valid present config shapes")
    return int(bool(docs_status or problems))


if __name__ == "__main__":
    raise SystemExit(main())
