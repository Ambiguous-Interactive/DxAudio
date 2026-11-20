"""Check final newlines and the pinned formatter on repository inputs."""

from __future__ import annotations

import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FORMATTED = {".md", ".json", ".yaml", ".yml", ".js", ".cjs", ".mjs"}
TEXT = FORMATTED | {".cs", ".csproj", ".py", ".ps1", ".sh", ".meta", ".asmdef", ".asmref", ".unity", ".ruleset"}
GENERATED = {".github/checks/unity-helpers-inventory.json"}


def inputs() -> list[Path]:
    result = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=ROOT
    )
    return sorted({Path(path) for path in result.decode("utf-8").split("\0") if path and (ROOT / path).is_file()})


def generated(path: Path) -> bool:
    return path.as_posix() in GENERATED or path.name.endswith("lock.json")


def text_problems(data: bytes, *, final_newline: bool) -> list[str]:
    problems = []
    if data.startswith(b"\xef\xbb\xbf"):
        problems.append("unexpected UTF-8 BOM")
    if b"\r" in data:
        problems.append("expected LF line endings")
    if final_newline and data and not data.endswith(b"\n"):
        problems.append("missing final newline")
    try:
        data.decode("utf-8")
    except UnicodeDecodeError:
        problems.append("invalid UTF-8")
    return problems


def index_problems() -> list[str]:
    report = subprocess.check_output(["git", "ls-files", "--eol", "-z"], cwd=ROOT).decode("utf-8")
    problems = []
    for row in report.split("\0"):
        status, _, path = row.partition("\t")
        if status.startswith(("i/crlf ", "i/mixed ")):
            problems.append(f"{path}: index is not normalized to LF")
    return problems


def main() -> int:
    paths = inputs()
    problems = index_problems()
    for path in paths:
        if path.suffix in TEXT:
            for problem in text_problems((ROOT / path).read_bytes(), final_newline=not generated(path)):
                problems.append(f"{path}: {problem}")
    for problem in problems:
        print(problem, flush=True)
    formatted = [str(path) for path in paths if path.suffix in FORMATTED and not generated(path)]
    prettier = ROOT / "node_modules" / "prettier" / "bin" / "prettier.cjs"
    result = subprocess.run(
        ["node", str(prettier), "--check", "--", *formatted], cwd=ROOT
    )
    return int(bool(problems or result.returncode))


if __name__ == "__main__":
    raise SystemExit(main())
