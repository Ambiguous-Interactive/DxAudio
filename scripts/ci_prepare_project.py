#!/usr/bin/env python3
"""Create a throwaway Unity project that references this package for CI tests.

The package repository has no enclosing Unity project on a CI runner, so the
EditMode job builds one in a temporary directory and points its manifest at the
checkout with a ``file:`` reference.

Example:
    uv run scripts/ci_prepare_project.py \
        --package . \
        --output "$RUNNER_TEMP/dxaudio-ci" \
        --unity-version 6000.6.7f1
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Optional, Sequence

TEST_FRAMEWORK_VERSION = "1.8.0"


def _manifest(package: Path) -> dict:
    reference = package.resolve().as_posix()
    return {
        "dependencies": {
            "com.unity.modules.audio": "1.0.0",
            "com.unity.test-framework": TEST_FRAMEWORK_VERSION,
            "com.wallstop-studios.dxaudio": f"file:{reference}",
        },
        "testables": ["com.wallstop-studios.dxaudio"],
    }


def prepare(package: Path, output: Path, unity_version: str) -> Path:
    if not (package / "package.json").is_file():
        raise SystemExit(f"Not a UPM package (missing package.json): {package}")
    if not unity_version.strip():
        raise SystemExit("--unity-version must not be empty.")
    (output / "Assets").mkdir(parents=True, exist_ok=True)
    (output / "Packages").mkdir(parents=True, exist_ok=True)
    (output / "ProjectSettings").mkdir(parents=True, exist_ok=True)
    manifest_path = output / "Packages" / "manifest.json"
    manifest_path.write_text(json.dumps(_manifest(package), indent=2) + "\n", encoding="utf-8", newline="\n")
    (output / "ProjectSettings" / "ProjectVersion.txt").write_text(
        f"m_EditorVersion: {unity_version}\n",
        encoding="utf-8",
        newline="\n",
    )
    return output


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="ci-prepare-project", description=__doc__)
    parser.add_argument("--package", default=".", help="Package checkout to reference")
    parser.add_argument("--output", required=True, help="Directory for the throwaway project")
    parser.add_argument("--unity-version", required=True, help="Editor version, e.g. 6000.6.7f1")
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    options = build_parser().parse_args(argv)
    project = prepare(Path(os.path.abspath(options.package)), Path(os.path.abspath(options.output)), options.unity_version)
    print(f"Prepared Unity project at {project}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
