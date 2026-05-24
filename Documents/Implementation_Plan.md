# Claude4Net Implementation Plan

Plan date: 2026-05-24
Working branch: `experiment`
Primary planning file: `Documents/Implementation_Plan.md`
Progress tracker: `IMPLEMENTATION_PROGRESS.md`
Design source: `Documents/2026-05-21_Claude4Net-App_인사이트_기반_확장_설계.md`

Current focus: None
Next milestone: Awaiting user/final-controller decision
Queue status: inactive
Latest verified commit: `185db17`
Latest release gate: 613/613 pass

## 0. SSOT Purpose

This file is the active implementation SSOT.

It keeps only:

- Current queue state
- Non-negotiable operating rules
- Reusable execution/review templates
- The latest completed milestone entry
- Verification and commit policy

Historical details belong in:

- `IMPLEMENTATION_PROGRESS.md`
- `Documents/backups/`
- dedicated design documents under `Documents/`
- git commit history

Repository state overrides old reports. If this file conflicts with code, tests, or git history, verify the repository first and update this file only with evidence.

## 1. Agent Read Order

Workers and reviewers must read context in this order:

1. `Documents/Implementation_Plan.md`
2. `IMPLEMENTATION_PROGRESS.md`
3. Relevant design or system prompt document explicitly named by the user
4. `git status --short --branch`
5. Recent commits, staged files, and untracked files

Do not use backup files as active instructions. Backup files are historical lookup only.

## 2. Non-Negotiable Rules

- Do not modify `.agents/`.
- Do not modify `.gemini/agents/` unless the user explicitly asks for agent prompt changes.
- Do not run unrelated milestones in the same worker task.
- Do not implement the whole roadmap at once.
- Do not select or activate the next milestone without user/final-controller approval.
- Do not remove `--legacy-cli`.
- Do not break piped input, Discord, Dashboard, `--smoke-exit`, or `doctor` paths.
- Do not expose Dashboard write/control actions unless they pass permission, audit/event, and test requirements.
- Do not enable recurring routines by default; routine automation must be opt-in, permission-aware, and bounded.
- Use `Completed` only after build, tests, release gate, and git hygiene evidence exist.
- Documents under `Documents/` may be ignored by git; stage intentional document changes with `git add -f`.
- Do not use `git add .` or `git add -A` in automated instructions.

## 3. Current Verified Baseline

Latest completed work:

| Milestone | Name | Status | Evidence |
| --- | --- | --- | --- |
| K087 | Skill Store Scope Separation | Completed | Global/local skill store separation implemented and verified; latest warning cleanup pushed at `185db17`; release gate 613/613 pass |

Current state:

- Branch `experiment` is synchronized with `origin/experiment` after commit `185db17`.
- `dotnet build -p:UseAppHost=false` completed with 0 warnings and 0 errors.
- `.\scripts\verify-release.ps1` passed with 613/613 unit and integration tests plus focused smoke checks.
- No K088+ milestone is selected.
- Active Execution Card is `None`.

## 4. Active Execution Card

Status: None

No active milestone is currently selected. The next milestone must be chosen by the user/final-controller before any worker task starts.

### Execution Card Template

Use this template only after a milestone is explicitly selected.

```markdown
## K### <Milestone Name>

Status: Active
Owner: <worker/reviewer/final-controller>
Dependency: <required completed milestones>

Goal:
- <one-sentence goal>

Allowed files:
- <exact file or directory>

Forbidden files:
- `.agents/**`
- unrelated runtime, test, document, or prompt files

Required work:
- <implementation item 1>
- <implementation item 2>

Required tests:
- <targeted test names>

Verification:
- `git status --short --branch`
- `git diff --cached --name-status`
- `git diff --check`
- `git diff --cached --check`
- `dotnet build -p:UseAppHost=false`
- `dotnet test Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "<filter>"`
- `.\scripts\verify-release.ps1`

Commit policy:
- Worker must not commit or push.
- Final controller may commit only after approval rules are satisfied.
- Push requires explicit user approval.
```

## 5. Latest Completed Entry

### K087 Skill Store Scope Separation

Status: Completed

Goal:
- Separate global skill storage from workspace-local skill storage.

Implemented behavior:
- Global skills are stored and discovered separately from workspace `.claude4net/skills`.
- Local skill application paths remain workspace-scoped.
- Skill apply logic handles global and local targets without corrupting checkpoint behavior.
- Self-evolving skill paths follow the separated store model.

Changed areas:
- `Claude4Net.Runtime/SelfEvolvingSkills.cs`
- `Claude4Net.Runtime/SkillApplyEngine.cs`
- `Claude4Net.Runtime/SkillRegistryService.cs`
- skill registry regression tests
- skill apply engine regression tests
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Verification evidence:
- `dotnet build -p:UseAppHost=false`: 0 warnings, 0 errors
- targeted skill-store and warning-regression tests: 29/29 pass
- `.\scripts\verify-release.ps1`: 613/613 pass
- Warning cleanup commit: `185db17 chore: remove warning regressions after K087`
- Branch pushed to `origin/experiment`

Residual risk:
- None known in K087 scope.

## 6. Review And Final-Control Template

### Worker Report Template

```markdown
## Worker Result

Status: Completed / Blocked / Rework Needed

Scope:
- <what was implemented>

Changed files:
- <file list>

Core behavior:
- <behavior summary>

Compatibility:
- <legacy/piped/dashboard/discord/provider impact>

Verification:
- Build: PASS/FAIL
- Targeted tests: N/N pass
- Release gate: PASS/FAIL
- Whitespace: Clean/Issues
- Git status: <summary>

Out-of-scope files preserved:
- <file list>

Commit/push:
- Not performed
```

### Review Checklist

- Staged files match the active execution card.
- No unrelated tracked or untracked files are included.
- No `.agents/` change unless explicitly authorized.
- `git diff --check` and `git diff --cached --check` are clean.
- Build has 0 warnings and 0 errors unless explicitly waived.
- Targeted tests prove the changed behavior.
- Release gate passes.
- Documentation numbers match actual evidence.
- No future milestone is activated without approval.

## 7. Verification Standard

Standard commands:

```powershell
git status --short --branch
git diff --name-status
git diff --cached --name-status
git diff --check
git diff --cached --check
dotnet build -p:UseAppHost=false
dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false
dotnet .\Claude4Net.Cli\bin\Debug\net10.0\Claude4Net.Cli.dll --smoke-exit
dotnet run --project Claude4Net.Cli -- doctor --output-format json
```

Official gate:

```powershell
.\scripts\verify-release.ps1
```

## 8. Documentation Synchronization Rules

- `IMPLEMENTATION_PROGRESS.md` records detailed historical completion evidence.
- `Documents/Implementation_Plan.md` records only current queue state, templates, and the latest completed entry.
- Do not keep long historical execution cards in this file.
- Do not keep conflicting status lines such as `Completed` and `pending` for the same milestone.
- If a milestone is implemented but not final-controlled, use `In Review / Final-Control Pending`.
- If no next milestone has explicit user/final-controller approval, keep Active Execution Card as `None`.

## 9. Branch And Commit Policy

- Feature work starts on `experiment`.
- Stable release branches are not direct implementation targets unless explicitly selected.
- Worker agents must not commit or push.
- Final controller may commit only after approval rules are satisfied.
- Push requires explicit user approval.
- Stage only files in the active execution card.

## 10. Completion Dashboard

Latest completed milestone:

- [x] K087 Skill Store Scope Separation

Next milestone:

- [ ] Not selected
