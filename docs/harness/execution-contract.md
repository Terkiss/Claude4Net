# Execution Contract

This document defines the formal execution contract for tasks managed by the Terukirdo v5.3 harness.

## Execution Card Schema Reference

All tasks must be defined using an **Execution Card** that conforms to the JSON Schema at [.agents/schemas/execution-card.schema.json](file:///D:/Project/codex-flutter/newAntigravity/.agents/schemas/execution-card.schema.json).

### Key Fields

- `task_id`: Unique alphanumeric identifier (e.g., `Terukirdo-v5.3-migration-001`).
- `title`: Short descriptive title of the task.
- `objective`: High-level goal.
- `intent`: Step-by-step description of how to achieve the objective.
- `risk_level`: `low`, `medium`, or `high`.
- `tier`: Bounded routing tier (Tier 0 to Tier 3).
- `target_skill`: The primary skill from `.agents/skills/` assigned to this task (exactly one primary skill per atomic task).
- `scope`:
  - `allowed_files`: Explicit list or patterns of files the worker is permitted to modify.
  - `forbidden_files`: Files the worker must not touch.
  - `no_new_dependencies`: Boolean flag indicating if adding external packages is prohibited.
- `requirements`: Explicit list of required changes.
- `non_requirements`: Out of scope items.
- `acceptance_criteria`: List of criteria with descriptive verification methods.

## Policy Rules

1. **One Task, One Primary Skill**: An atomic task must specify exactly one target skill. If multiple skills are required, the task must be decomposed.
2. **Scope Constraint**: Workers must strictly edit files within `allowed_files` and avoid `forbidden_files`.
3. **No New Dependencies**: If `no_new_dependencies` is `true`, no packages can be installed without user permission.
