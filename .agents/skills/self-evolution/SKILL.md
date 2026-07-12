---
name: self-evolution
description: Nightly background maintenance skill that uses SkillOpt to automatically harvest logs, evaluate, and optimize agent skills in .agents/skills/. Must only be triggered upon explicit user permission.
---

# Self-Evolution (SkillOpt)

## Purpose
This skill serves as the orchestrator for **SkillOpt-Sleep**, a framework that treats `.md` agent skills as trainable parameters. It runs in the background (typically at night or when idle) to harvest recent session logs, mine them for failures/successes, replay scenarios, and safely optimize the prompt instructions in `.agents/skills/`.

## Rules
1. **Explicit Trigger Only:** Do not run this engine automatically without the user's explicit consent. The user must trigger it explicitly, e.g., using `/goal 스킬 최적화` or `/schedule`. The system MUST NOT auto-trigger evolution based on error counts, hooks, or any automated mechanism.
2. **Validation Gate:** Any proposed changes to skill files MUST NOT be immediately applied to the working branch. They are saved as a diff report in `scripts/evolution_reports/` and presented to the user via an Artifact for approval.
3. **Sandboxed Environment:** The `skillopt-engine` relies on a Python environment. Do not install its dependencies globally in the project. Use a dedicated `venv` inside `scripts/skillopt-engine/`.
4. **Immutable Core:** The evolution engine may ONLY propose changes to files inside `.agents/skills/`. It MUST NOT modify orchestrator prompts, hooks, router code, or any files outside `.agents/skills/`.

## Scripts

This skill contains 3 components in `scripts/`:

| Script | Purpose |
|--------|---------|
| `skill_indexer.py` | **FAISS Vector Indexer** — Parses all SKILL.md files, embeds them with `all-MiniLM-L6-v2`, and builds a FAISS `IndexFlatIP` index. Used by the SAD routing architecture in `ralph-orchestrator.md` to dynamically search for relevant skills. Supports `--build`, `--search "<query>"`, and `--daemon` modes. |
| `auto_evolve.py` | **Evolution Wrapper (Manual Only)** — Orchestrates the SkillOpt-Sleep pipeline. Records a safe point, runs harvest/mine/rollout, then generates a diff report for human review. Does NOT auto-commit or auto-apply changes. |
| `skillopt-engine/` | **SkillOpt Core** — The embedded Microsoft SkillOpt framework. Requires its own Python venv for isolation. |

## Setup & Execution

### 1. Set up the SkillOpt sandbox (run once):
```powershell
cd .agents/skills/self-evolution/scripts/skillopt-engine
python -m venv venv
.\venv\Scripts\activate
pip install -e .
```

### 2. Set up the FAISS indexer (run once, requires separate deps):
```powershell
pip install sentence-transformers faiss-cpu watchdog numpy
python .agents/skills/self-evolution/scripts/skill_indexer.py --build
```

### 3. To trigger a self-evolution cycle (when authorized by user):
```powershell
python .agents/skills/self-evolution/scripts/auto_evolve.py
```
The script will produce a report in `scripts/evolution_reports/` — review and apply manually.

### 4. To start the skill index auto-refresh daemon (optional):
```powershell
python .agents/skills/self-evolution/scripts/skill_indexer.py --daemon
```

## Output & Approval
When the engine proposes new skill versions, the `auto_evolve.py` wrapper saves a timestamped diff report. The agent reads the changes, summarizes the "Why" (why the engine thought this change was necessary based on past failures/successes), and asks the user for approval before applying any changes to the actual skill files.
