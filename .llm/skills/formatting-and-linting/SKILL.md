---
name: formatting-and-linting
description: Run CSharpier, Prettier, pre-commit, and the DxAudio Python lint gate; fix format-check or lint failures. Use when formatting code, adding a hook, or when a commit is rejected by csharpier, dotnet-tool-restore, or a lint step.
metadata:
  category: Core
---

# Formatting And Linting

## Toolchain

| Tool          | Source                      | Command                                      |
| ------------- | --------------------------- | -------------------------------------------- |
| CSharpier     | `.config/dotnet-tools.json` | `dotnet tool run csharpier format`           |
| Prettier      | `package.json` devDependency| `npm run format:md`                          |
| pre-commit    | `.pre-commit-config.yaml`   | `pre-commit run --all-files`                 |
| uv            | `pyproject.toml`            | `uv run scripts/<script>.py`                 |
| Full gate     | `scripts/gate.py`           | `npm run check:gate`                         |

Run `dotnet tool restore` first. The `dotnet-tool-restore` hook runs on every commit and fails fast when tools are missing.

## Formatting

1. Format only touched files: `dotnet tool run csharpier format <paths>`.
2. Whole-repo check: `dotnet tool run csharpier check .`.
3. Never hand-adjust CSharpier output. Fix the config upstream.
4. Markdown uses Prettier at the configured width. Tables align to their widest cell and may exceed the line width.

## Style contract

- `using` directives go inside the block-scoped namespace.
- Explicit types over `var`; explicit access modifiers on every member.
- Braces on every body, including forwarding methods and lambdas.
- Comparison operators read ascending: `<`, `<=`, `==` only.
- No LINQ in `Runtime/` or `Tests/`.
- Unity object null checks use `== null` / `!= null`.
- One top-level type per file.
- C# assets are UTF-8 with BOM and CRLF by repo convention; `.llm/**`, `scripts/**`, and `.devcontainer/**` are UTF-8 without BOM and LF. `.editorconfig` owns the split and
  `scripts/lint_line_endings.py` checks `.gitattributes` agrees.

## Lint commands

| Command                          | Checks                                                        |
| -------------------------------- | ------------------------------------------------------------- |
| `npm run lint:llm`               | skills index, frontmatter, version sync, entrypoint delegation|
| `npm run lint:csharp-usings`     | using placement                                               |
| `npm run lint:csharp-member-order` | member order                                                |
| `npm run lint:meta-companions`   | `.meta` companions and GUID uniqueness                        |
| `npm run lint:line-endings`      | `.editorconfig` vs `.gitattributes`                           |
| `npm run lint:changelog-length`  | CHANGELOG entry cap                                           |
| `npm run check:fast`             | all of the above, scoped to changed files                     |

## Pre-commit

The hook runs the uv environment repair, `dotnet tool restore`, CSharpier on staged `.cs`, the C# source policy lint, and the `.llm` lints. If a hook rewrites files, re-stage and
retry.

## Failure recovery

- `failed to locate pyvenv.cfg`: `npm run env:repair`.
- npm tool missing: rerun `uv run .devcontainer/install_npm_tools.py`; never use `sudo npm`.
- A lint that passes locally and fails in CI: run `npm run check:gate -- --report .artifacts/gate.json` and compare the step timing and output.
