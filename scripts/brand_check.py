"""Check DxAudio brand assets. Use --write to regenerate them from docs/brand/tokens.json."""

from __future__ import annotations

import argparse
import json
import re
from html import escape, unescape
from pathlib import Path
from string import Template
from typing import TypedDict, cast

ROOT = Path(__file__).resolve().parents[1]
IMAGES = "docs/images/brand"
HEX = re.compile(r"#[0-9a-fA-F]{3,8}(?![\w-])")
SWATCH = re.compile(r"#[0-9a-f]{6}")
GENERATED = "Generated from docs/brand/tokens.json by scripts/brand_check.py."


class BrandTokens(TypedDict):
    colors: dict[str, str]
    aliases: dict[str, str]
    fonts: dict[str, str]
    textContrast: list[tuple[str, str]]
    graphicContrast: list[tuple[str, str]]


def luminance(color: str) -> float:
    channels = [int(color[start : start + 2], 16) / 255 for start in (1, 3, 5)]
    red, green, blue = [
        value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4
        for value in channels
    ]
    return 0.2126 * red + 0.7152 * green + 0.0722 * blue


def check_contrast(colors: dict[str, str], pairs: list[tuple[str, str]], minimum: float) -> None:
    if not pairs:
        raise ValueError(f"No pairs defined for the {minimum}:1 contrast check.")
    for foreground, background in pairs:
        dark, light = sorted((luminance(colors[foreground]), luminance(colors[background])))
        ratio = (light + 0.05) / (dark + 0.05)
        if ratio < minimum:
            raise ValueError(f"{foreground} on {background}: {ratio:.2f}:1, needs {minimum}:1.")
        print(f"{foreground} on {background}: {ratio:.2f}:1")


def stylesheet(selector: str, variables: dict[str, str]) -> str:
    declarations = "".join(f"    --dxa-{name}: {value};\n" for name, value in variables.items())
    return f"/* {GENERATED} */\n{selector} {{\n{declarations}}}\n"


def svg(template: str, values: dict[str, str]) -> str:
    source = (ROOT / ".github" / "brand" / f"{template}.svg.tmpl").read_text(encoding="utf-8")
    variables = {key.replace("-", "_"): escape(value, quote=True) for key, value in values.items()}
    return f"<!-- {GENERATED} -->\n" + Template(source).substitute(variables)


def rendered_assets(colors: dict[str, str], fonts: dict[str, str]) -> dict[str, str]:
    manifest = cast("dict[str, str]", json.loads((ROOT / "package.json").read_bytes()))
    css_fonts = {key: json.dumps(value) for key, value in fonts.items()}
    return {
        f"{IMAGES}/dxaudio-banner.svg": svg(
            "banner", colors | fonts | {"version": manifest["version"]}
        ),
        f"{IMAGES}/dxaudio-mark.svg": svg(
            "mark", {"ring": colors["accent"], "glyph": colors["accent-tint"]}
        ),
        f"{IMAGES}/dxaudio-mark-light.svg": svg(
            "mark", {"ring": colors["light-mark-ring"], "glyph": colors["light-mark-glyph"]}
        ),
        "Editor/DxAudio.uss": stylesheet(".dxaudio", colors),
        "docs/stylesheets/dxaudio.css": stylesheet(":root", colors | css_fonts),
    }


def stray_colors(path: str, content: str, palette: set[str]) -> list[str]:
    if path.endswith(".svg"):
        content = unescape(content)
    return [
        f"{path}:{line_number}: {match[0]} is outside the brand palette."
        for line_number, line in enumerate(content.splitlines(), 1)
        for match in HEX.finditer(line)
        if match[0].lower() not in palette
    ]


def check_assets(expected: dict[str, str], palette: set[str], *, write: bool) -> list[str]:
    problems = [
        problem
        for path, content in expected.items()
        for problem in stray_colors(path, content, palette)
    ]
    if problems:
        return problems
    if write:
        for name, content in expected.items():
            path = ROOT / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8", newline="\n")

    existing = {
        path.relative_to(ROOT).as_posix(): path.read_text(encoding="utf-8")
        for pattern in (f"{IMAGES}/*.svg", "Editor/**/*.uss", "docs/stylesheets/*.css")
        for path in sorted(ROOT.glob(pattern))
    }
    for name, content in existing.items():
        problems.extend(stray_colors(name, content, palette))
    for name, content in expected.items():
        if name not in existing:
            problems.append(f"Missing brand output: {name}")
        elif content != existing[name]:
            problems.append(f"Stale brand output: {name}")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="regenerate the assets")
    args = parser.parse_args()
    try:
        tokens = cast("BrandTokens", json.loads((ROOT / "docs/brand/tokens.json").read_bytes()))
        colors = tokens["colors"]
        if any(not SWATCH.fullmatch(value) for value in colors.values()):
            raise ValueError("Brand colors must be lowercase six-digit hex values.")
        if colors.keys() & tokens["aliases"].keys():
            raise ValueError("Brand aliases cannot redefine colors.")
        palette = set(colors.values())
        colors = colors | {key: colors[target] for key, target in tokens["aliases"].items()}
        check_contrast(colors, tokens["textContrast"], 4.5)
        check_contrast(colors, tokens["graphicContrast"], 3)
        assets = rendered_assets(colors, tokens["fonts"])
        problems = check_assets(assets, palette, write=args.write)
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"Brand check failed: {error}")
        return 1
    if problems:
        print("\n".join(problems))
        print("Regenerate missing or stale outputs with uv run scripts/brand_check.py --write.")
        return 1
    print("Brand assets match tokens.json and pass contrast checks.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
