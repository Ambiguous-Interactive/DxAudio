"""Run pinned unity-helpers documentation policies against DxAudio."""

from __future__ import annotations

import argparse
import importlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path

PORTABLE_DOC_PATHS = (
    ".markdownlint.json",
    "scripts/check_code_fence_syntax.py",
    "scripts/lint_asmdef.py",
    "scripts/lint_doc_links.py",
    "scripts/lint_doc_identifiers.py",
    "scripts/lint_markdown.py",
    "scripts/run_node_bin.py",
    "scripts/extract_code_samples.py",
)
SOURCE_ROOTS = ("Runtime", "Editor", "Tests", "Samples~")


def write_text(destination: Path, content: str) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(content, encoding="utf-8")


def stage_docs(root: Path, fixture: Path, visible_markdown: list[str], identifiers: object) -> None:
    for source_root in SOURCE_ROOTS:
        for source in identifiers.files_under(root / source_root, (".cs", ".asmdef")):
            write_text(fixture / source.relative_to(root), source.read_text(encoding="utf-8-sig"))
    for relative in visible_markdown:
        if relative == "README.md" or relative.startswith(("Documentation~/", "Samples~/")):
            write_text(fixture / "docs" / relative, (root / relative).read_text(encoding="utf-8-sig"))


def run_link_checks(root: Path, modules: Path) -> list[str]:
    links = importlib.import_module("lint_doc_links")
    with tempfile.TemporaryDirectory(prefix="dxaudio-doc-link-controls-") as folder:
        fixture = Path(folder)
        source = fixture / "index.md"
        write_text(source, "# Index\n\n[Guide](guide.md)\n")
        write_text(fixture / "guide.md", "# Guide\n")
        paths = ["index.md", "guide.md"]
        positive = links.scan(links.Repository(fixture, paths), paths, "Targets")
        if positive.sources_checked != len(paths) or positive.findings:
            raise ValueError("the documentation link scanner missed its positive control")
        write_text(source, "# Index\n\n[Guide](missing.md)\n")
        negative = links.scan(links.Repository(fixture, paths), paths, "Targets")
        if len(negative.findings) != 1 or "missing.md" not in negative.findings[0].message:
            raise ValueError("the documentation link scanner missed its negative control")
    # Upstream's Format mode sets Jekyll link conventions. Targets is the
    # portable repository link and code-reference check for this UPM package.
    command = [sys.executable, str(modules / "lint_doc_links.py"), "--repo-root", str(root), "--mode", "Targets"]
    result = subprocess.run(command, check=False)
    return ["upstream documentation link target validation failed"] if result.returncode else []


def extract_code_samples(fixture: Path, modules: Path, *, expect_samples: bool = True) -> int:
    report_dir = fixture / "extraction"
    command = ["uv", "run", "--no-project", "--with", "markdown-it-py==4.2.0", "--with", "mdurl==0.1.2", "python",
               str(modules / "extract_code_samples.py"), "--extract-only", "--scan-root", str(fixture / "docs"),
               "--output-dir", str(report_dir)]
    result = subprocess.run(command, check=False, capture_output=True, text=True)
    report = json.loads((report_dir / "extraction-report.json").read_text(encoding="utf-8"))
    samples = len(report["samples"])
    if (samples > 0) != expect_samples or (result.returncode == 0) != expect_samples:
        raise ValueError(f"C# sample extraction returned unexpected results. {result.stdout}{result.stderr}")
    return samples


def run_controls(fences: object, identifiers: object, markdown: object, config: Path, root: Path, modules: Path) -> None:
    with tempfile.TemporaryDirectory(prefix="dxaudio-doc-controls-") as folder:
        fixture = Path(folder)
        write_text(fixture / "Runtime" / "Control.cs", "namespace WallstopStudios.Control { class Valid {} }\n")
        write_text(fixture / "Runtime" / "Control.asmdef", '{"name":"WallstopStudios.Control"}\n')
        sample = fixture / "docs" / "Control.md"
        write_text(sample, (
            "# Valid sample\n\n"
            "```csharp\nusing WallstopStudios.Control;\n```\n\n"
            "```xml\n<assembly fullname=\"WallstopStudios.Control\" />\n```\n"
        ))
        _, fence_findings = fences.scan(fixture / "docs")
        result = identifiers.analyze(fixture)
        if fence_findings or result.violations or (result.usings, result.assemblies) != (1, 1):
            raise ValueError("documentation checks rejected their positive control or scanned nothing")
        arguments = ["markdownlint", "--config", str(config), "--", str(sample)]
        if markdown.run_node_bin(arguments, root=root):
            raise ValueError("Markdownlint rejected its positive control")
        if extract_code_samples(fixture, modules) != 1:
            raise ValueError("the C# sample extractor missed its positive control")

        write_text(sample, (
            "# Invalid sample\n\n"
            "```csharp,ignore\nusing WallstopStudios.Missing;\n```\n\n"
            "```xml\n<assembly fullname=\"WallstopStudios.Missing\" />\n```\n\n"
            "```\nMissing language\n```\n"
        ))
        _, fence_findings = fences.scan(fixture / "docs")
        result = identifiers.analyze(fixture)
        if len(fence_findings) != 1 or len(result.violations) != 2:
            raise ValueError("documentation checks missed their negative controls")
        if not markdown.run_node_bin(arguments, root=root):
            raise ValueError("Markdownlint accepted a fence without a language")
        write_text(sample, "# A page with no code sample\n")
        extract_code_samples(fixture, modules, expect_samples=False)


def run_doc_checks(root: Path, modules: Path, upstream_tree: Path) -> list[str]:
    if str(modules) not in sys.path:
        sys.path.insert(0, str(modules))
    sys.dont_write_bytecode = True
    fences = importlib.import_module("check_code_fence_syntax")
    identifiers = importlib.import_module("lint_doc_identifiers")
    markdown = importlib.import_module("lint_markdown")

    visible = [
        name for name in markdown.markdown_candidates(
            markdown.read_inventory(root), insensitive=sys.platform in ("win32", "darwin")
        )
        if (root / name).is_file() and not (root / name).is_symlink()
    ]
    if not visible:
        raise ValueError("Markdown inventory found no visible documentation")

    with tempfile.TemporaryDirectory(prefix="dxaudio-docs-") as folder:
        fixture = Path(folder)
        stage_docs(root, fixture, visible, identifiers)
        checked, findings = fences.scan(fixture / "docs")
        identifiers_result = identifiers.analyze(fixture)
        sample_count = extract_code_samples(fixture, modules)
        if identifiers_result.documents != checked or identifiers_result.namespaces == 0:
            raise ValueError("the fence and identifier scanners did not cover the staged sources")
        problems = [
            f"{finding.path.relative_to(fixture / 'docs').as_posix()}:{finding.line}: "
            f"code fence uses a comma-separated attribute ({finding.language},{finding.attributes})"
            for finding in findings
        ]
        problems.extend(problem.removeprefix("docs/") for problem in identifiers_result.violations)
    problems.extend(run_link_checks(root, modules))

    config = upstream_tree / ".markdownlint.json"
    run_controls(fences, identifiers, markdown, config, root, modules)
    for batch in markdown.batch_markdown_files(visible):
        if markdown.run_node_bin(["markdownlint", "--config", str(config), "--", *batch], root=root):
            problems.append("Markdownlint found structure violations in the visible Markdown inventory.")
    print(
        f"[unity-helpers-docs] checked {checked} user documents and {len(visible)} Markdown files "
        f"({identifiers_result.usings} package usings, {identifiers_result.assemblies} linker references, "
        f"{sample_count} C# samples)"
    )
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("upstream_tree", type=Path, help="staged immutable unity-helpers tree")
    args = parser.parse_args()
    problems = run_doc_checks(Path(__file__).resolve().parents[2], args.upstream_tree / "scripts", args.upstream_tree)
    for message in problems:
        print(message)
    return int(bool(problems))


if __name__ == "__main__":
    raise SystemExit(main())
