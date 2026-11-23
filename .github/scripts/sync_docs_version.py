"""Check or update the install examples and banner from package.json."""

from __future__ import annotations

import argparse
import json
import re
from typing import cast

from package_check import NAME, ROOT, manifest_object, version_problems

INSTALL_PINS = (
    ("npm pin", re.compile(rf'(?m)^[ \t]*"{re.escape(NAME)}":[ \t]*"([^"\r\n]*)"')),
    (
        "git pin",
        re.compile(r"(?m)^https://github\.com/Ambiguous-Interactive/DxAudio\.git#v([^\r\n]*)\r?$"),
    ),
)
BANNER_CHIP = re.compile(r'(?m)^[ \t]*<text\b[^>]*\bdata-sync="version"[^>]*>v([^<]*)</text>')
DOC_PINS = {
    "README.md": INSTALL_PINS,
    "Documentation~/install.md": INSTALL_PINS,
    "docs/images/brand/dxaudio-banner.svg": (("banner chip", BANNER_CHIP),),
    "docs/images/brand/dxaudio-banner-light.svg": (("banner chip", BANNER_CHIP),),
}


def sync_pins(path: str, text: str, version: str) -> tuple[str, list[str]]:
    problems: list[str] = []
    for label, pattern in DOC_PINS[path]:
        matches = list(pattern.finditer(text))
        if len(matches) != 1:
            raise ValueError(f"{path}: expected one {label}, found {len(matches)}")
        match = matches[0]
        if match[1] != version:
            start, end = match.span(1)
            line = text.count("\n", 0, start) + 1
            problems.append(f"{path}:{line}: {label} is {match[1]!r}, expected {version!r}")
            text = text[:start] + version + text[end:]
    return text, problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fix", action="store_true", help="rewrite the pins")
    args = parser.parse_args()
    try:
        manifest = json.loads(
            (ROOT / "package.json").read_bytes(), object_pairs_hook=manifest_object
        )
        version = (
            cast("dict[str, object]", manifest).get("version")
            if isinstance(manifest, dict)
            else None
        )
        if not isinstance(version, str) or version_problems(version, None):
            raise ValueError("package.json version must be a complete release version")
        updates = {
            path: sync_pins(path, (ROOT / path).read_bytes().decode("utf-8"), version)
            for path in DOC_PINS
        }
        for path, (text, problems) in updates.items():
            if args.fix and problems:
                (ROOT / path).write_bytes(text.encode("utf-8"))
            for problem in problems:
                print(f"{'Fixed: ' if args.fix else ''}{problem}")
        if not args.fix and any(problems for _, problems in updates.values()):
            print("Run python3 .github/scripts/sync_docs_version.py --fix")
            return 1
    except (OSError, ValueError) as error:
        print(error)
        return 1
    print(f"Documentation pins match package.json {version}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
