---
name: manage-skills
description: Create, edit, validate, and index Agent Skills in .llm/skills for DxAudio - frontmatter rules, category values, the generated index, size limits, and the lint commands that enforce all of them. Use when adding a skill, changing a skill description, editing .llm/context.md, or when lint:llm reports index drift or invalid frontmatter.
metadata:
  category: Core
---

# Manage Skills

Skills live at `.llm/skills/<kebab-name>/SKILL.md` and follow the [Agent Skills](https://agentskills.io) format. `.llm/context.md` is the single source of truth for repository
rules; skills are procedures.

## Frontmatter contract

```yaml
---
name: kebab-name
description: What it does and when to use it, one ASCII line, up to 1024 characters.
metadata:
  category: Core
---
```

- `name` is lowercase letters, digits, and single hyphens, at most 64 characters, and equals the directory name.
- `description` is one line, ASCII only, keyword-rich. Name the trigger conditions; agents select skills by this text.
- `category` is `Core`, `Workflow`, or `Feature`. Missing means `Feature`.
- The body stays at or below 300 lines. Move bulk examples to `.llm/references/`.

## Workflow

1. Create or edit the directory and `SKILL.md`.
2. Regenerate the index: `uv run --no-project scripts/uv_environment.py --repair && uv run scripts/generate_skills_index.py`.
3. Lint: `npm run lint:llm` (adds `--fix` through `npm run lint:llm:fix`).
4. Run `npm run check:fast`.

Never hand-edit `.llm/skills/index.md`; it is generated. Never add `.meta` files under `.llm/`; Unity ignores dot paths.

## Size and content

- Prefer procedures and verified facts over narrative. Link the owning source instead of copying it.
- One rule, one home. If a fact belongs in `.llm/context.md`, link it instead of restating it.
- Record measurements with their host, Unity version, and sample count.

## Related

- [ship-changes](../ship-changes/SKILL.md) - the validation ladder
