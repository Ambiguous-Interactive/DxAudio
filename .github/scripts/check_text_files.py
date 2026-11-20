"""Check final newlines and the pinned formatter on repository inputs."""

from __future__ import annotations

import argparse
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FORMATTED = {".md", ".json", ".yaml", ".yml", ".js", ".cjs", ".mjs"}
TEXT = FORMATTED | {".cs", ".py", ".ps1", ".sh"}
GENERATED = {".github/checks/unity-helpers-inventory.json"}


def inputs() -> list[Path]:
    result = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=ROOT
    )
    return sorted({Path(path) for path in result.decode("utf-8").split("\0") if path and (ROOT / path).is_file()})


def generated(path: Path) -> bool:
    return path.as_posix() in GENERATED or path.name.endswith("lock.json")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    paths = [path for path in inputs() if not generated(path)]
    missing = []
    for path in paths:
        if path.suffix not in TEXT:
            continue
        data = (ROOT / path).read_bytes()
        if data and not data.endswith(b"\n"):
            missing.append(path)
            print(f"{path}: missing final newline")
    formatted = [str(path) for path in paths if path.suffix in FORMATTED]
    prettier = ROOT / "node_modules" / "prettier" / "bin" / "prettier.cjs"
    result = subprocess.run(
        ["node", str(prettier), "--write" if args.write else "--check", "--", *formatted], cwd=ROOT
    )
    return int(bool(missing or result.returncode))


if __name__ == "__main__":
    raise SystemExit(main())
