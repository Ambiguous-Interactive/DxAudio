---
name: plan-hygiene
description: Keep PLAN.md lean - ordered open tasks, blockers, and acceptance criteria only. Move standing rules to context/skills and completed work to progress/ logs. Use when editing PLAN.md, ending a session, or removing plan bloat.
metadata:
  category: Core
---

# Plan Hygiene

`PLAN.md` answers: what remains, what blocks it, and how will we know it is done?

## Content ownership

| Content                                                       | Home                              |
| ------------------------------------------------------------- | --------------------------------- |
| Ordered open tasks, blockers, dependencies, acceptance        | `PLAN.md`                         |
| Session scope and work style                                  | `GOAL.md`                         |
| Standing repository rules                                     | `.llm/context.md`                 |
| Durable constraints, roadmap, decisions                       | `.llm/references/`                |
| Reusable procedures                                           | `.llm/skills/*/SKILL.md`          |
| Completed work, gate results, measurements, audit trail       | `progress/session-NNN-slug.md`    |
| Raw captures and benchmark output                             | `.artifacts/`                     |

`PLAN.md`, `GOAL.md`, `progress/`, and their Unity metas are local-only and gitignored. Never force-add, package, or publish them.

## Plan contract

- One current task, or `No task selected.`
- Each task states the remaining action and its acceptance. Include a blocker only when it changes execution.
- Delete completed tasks immediately. Do not keep checked boxes, landed PRs, or session summaries.
- Omit standing constraints, command catalogs, and repeated status sections.
- Do not copy an issue or skill into the plan. Link the authoritative source.
- Target 60 lines; review before exceeding 80.

## Session close

1. Read the whole plan and the session evidence.
2. Map each old requirement to an open task, durable reference, or completed-work record.
3. Move history into the session log; route durable facts to their owner.
4. Rewrite the plan; confirm every unresolved requirement and blocker survives.
5. Regenerate the skills index if skills changed, then run `npm run lint:llm`.
