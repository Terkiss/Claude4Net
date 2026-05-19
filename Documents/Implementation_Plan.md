# Claude4Net Implementation Plan

Plan date: 2026-05-19
Working branch: `experiment`
Primary planning file for Gemini/Ralph agents: `Documents/Implementation_Plan.md`
Progress tracker: `IMPLEMENTATION_PROGRESS.md`
Current focus: Project Lumen TUI v2

Next milestone: Not selected. Awaiting user/final-controller decision.

## 0. Agent Read Order

All `.gemini/agents/*.md` workers and reviewers should read project context in this order:

1. `Documents/Implementation_Plan.md`
2. `IMPLEMENTATION_PROGRESS.md`
3. `Documents/Project_Lumen_CLI_UI_Design_Plan.md`
4. `Documents/Project_Lumen_UI_UX_V2_External_Design_Review.md`
5. `git status --short --branch`
6. Recent commits and current staged/untracked files

Repository state overrides old reports. If this plan conflicts with actual code or test results, verify the repository first and update the plan only with evidence.

## 1. Non-Negotiable Rules

- Do not modify `.agents/`.
- Do not commit or push without Codex Final Controller or user approval.
- Do not run unrelated milestones in the same worker task.
- Do not implement the whole roadmap at once.
- Do not remove `--legacy-cli`.
- Do not break piped input, Discord, Dashboard, `--smoke-exit`, or `doctor` paths.
- Do not add a new terminal UI framework for Project Lumen v2.
- Use `Completed` only after required tests and release gate evidence exist.
- Documents under `Documents/` may be ignored by git; stage intentional document changes with `git add -f`.

## 2. Current Queue State

queue_status: `running`

Current reality as of this plan:

- K038-K050 are treated as Lumen v1 completed milestones.
- K051a, K051b, K051c are treated as completed by existing progress records.
- K052 is completed and verified (441/441 pass).
- K053 is a candidate only (Draft only, not active).

Important consistency rule:

Do not mark K053 active while K052 changes remain unapproved or unverified.

## 3. Source Design Requirements

### 3.1 Project Lumen v1 Design Source

Source:

- `Documents/Project_Lumen_CLI_UI_Design_Plan.md`

Core requirements already mapped into K038-K050:

- Bootstrap separation.
- Neutral `AgentRunEvent` observer.
- `LumenState`, reducer, history cells.
- Prompt composer.
- Lumen interactive app behind an explicit path.
- Approval queue/dialog.
- Command output normalization.
- Piped input, Discord, Dashboard, and legacy compatibility.
- Release gate and documentation.

### 3.2 Project Lumen v2 Design Source

Source:

- `Documents/Project_Lumen_UI_UX_V2_External_Design_Review.md`

Core K051/K052 requirements:

- Replace append-only Lumen behavior with a managed terminal surface.
- Use virtual transcript viewport plus fixed input/footer regions.
- Use full-frame buffered repaint.
- Use raw ANSI cursor placement in the terminal renderer.
- Use Spectre.Console only for safe styled content where appropriate.
- Keep footer/input out of transcript history.
- Ensure every typed character is visible immediately.
- Suppress duplicate assistant final responses and duplicate runtime errors.
- Add display-width-aware Korean/CJK wrapping and truncation.
- Implement manual transcript scrolling (K052).
- Preserve legacy, piped input, Discord, Dashboard, smoke, and doctor paths.

## 4. Milestone Status Table

| Milestone | Name | Status | Evidence / Notes |
| --- | --- | --- | --- |
| K038 | Project Lumen Bootstrap Foundation | Completed | Existing progress record |
| K039 | AgentRunEvent Observer Foundation | Completed | Existing progress record |
| K040 | Lumen State and History Cells | Completed | Existing progress record |
| K041 | Spectre Renderer v1 | Completed | Existing progress record |
| K042 | Lumen Output Bridge | Completed | Existing progress record |
| K043 | Prompt Composer Foundation | Completed | Existing progress record |
| K044 | LumenCliApp v1 | Completed | Existing progress record |
| K045 | Approval Dialog v1 | Completed | Existing progress record |
| K046 | Command Output Normalization | Completed | Existing progress record |
| K047 | Piped Input, Discord, and Legacy Compatibility | Completed | Existing progress record |
| K048 | Render Quality and Cancellation Stabilization | Completed | Existing progress record |
| K049 | Lumen Release Gate and Documentation | Completed | Existing progress record |
| K050 | Transcript Hygiene and Observer Mode Fix | Completed | Existing progress record |
| K051a | TerminalText and LumenFrame Foundation | Completed | Existing progress record |
| K051b | Lumen Frame Builder and State Evolution | Completed | Existing progress record |
| K051c | Lumen Terminal Renderer and Live Integration | Completed | 433/433 pass, K051c tests, release gate passed |
| K052 | Lumen v2 Search and Scroll Navigation | Completed | 441/441 pass, K052 tests, release gate passed |
| K053 | Not Selected | Awaiting decision | Pending user/final-controller decision |

## 5. [COMPLETED/HISTORICAL] Active Ralph Execution Card

This was the active card for K052, now completed.

# Ralph Execution Card

## Milestone

K052 Lumen v2 Search and Scroll Navigation

## Goal

Add controlled transcript navigation on top of the K051 fixed viewport renderer.

## Allowed Scope

- `Claude4Net.Cli/Ui/Rendering/LumenFrameBuilder.cs`
- `Claude4Net.Cli/Ui/Rendering/LumenFrame.cs`
- `Claude4Net.Cli/Ui/Rendering/TerminalMetrics.cs`
- `Claude4Net.Cli/Ui/LumenState.cs`
- `Claude4Net.Cli/Ui/LumenReducer.cs`
- `Claude4Net.Cli/Ui/LumenCliApp.cs`
- `Claude4Net.Cli/Ui/Input/KeyBindingRegistry.cs`
- `Claude4Net.Tests/K052LumenScrollNavigationTests.cs`
- `IMPLEMENTATION_PROGRESS.md`
- `Documents/Implementation_Plan.md`

## Forbidden Area

- .agents/**
- Runtime, SDK, API
- Discord/Dashboard/piped input paths
- K053+ implementation

## Required Work

- Add manual scroll state to `LumenState`.
- Implement PageUp/Down and Home/End logic in `LumenReducer`.
- Update `LumenFrameBuilder` to project transcript lines based on scroll offset.
- Ensure Input and Footer regions remain fixed during manual scroll.
- Handle "pinned-to-bottom" behavior where new output triggers auto-scroll only if already at bottom.

## Done When

- User can inspect earlier transcript without corrupting input/footer.
- New output behavior while scrolled is deterministic (distance-from-bottom preserved).
- K051 fixed-region guarantees remain intact.
- 8 tests pass for scroll behavior.
- Release gate pass with 441 tests.

## 6. K051/K052 Cumulative Acceptance Checklist

K051/K052 quality criteria for TUI v2 stabilization.

### 6.1 Input Stability

- Every typed character appears immediately in the input pane.
- Backspace/delete update the same input pane in place.
- Cursor movement is reflected in the input region.
- Input line is not appended to transcript.
- Input placeholder is not appended to transcript.
- Input buffer survives assistant streaming and tool result updates.

### 6.2 Footer Stability

- Footer is fixed at the bottom region.
- Footer is not appended to transcript.
- `IDLE | Provider | Model | Session` does not accumulate in scrollback as normal transcript content.
- Footer updates in place when status changes.
- Footer uses compact mode at 80 columns.

### 6.3 Transcript Correctness

- Transcript contains durable cells only.
- User prompt appears exactly once.
- Assistant final response appears exactly once.
- Streaming deltas merge into one assistant cell.
- Thought updates do not duplicate assistant text.
- Tool call appears once per tool call id.
- Tool result appears once per tool result id.
- Workspace/runtime error appears as one `ErrorCell`.

### 6.4 Dialog Correctness

- Approval dialog is not appended repeatedly.
- `D` toggles details in place.
- `Y`, `N`, and `Esc` resolve approval exactly once.
- Closing dialog restores input/footer layout.

### 6.5 Terminal Layout

- 80-column layout has no normal horizontal overflow.
- 120-column layout remains readable.
- Korean/CJK text is wrapped/truncated by display width, not raw string length.
- ANSI/Spectre markup escapes all untrusted user/model/tool text.
- Terminal resize triggers or allows a full repaint.
- If ANSI cursor control is unavailable, Lumen has a safe fallback or clear warning.

### 6.6 Compatibility

- `--lumen` uses the v2 renderer.
- Legacy CLI remains available.
- Piped input does not use TUI control sequences.
- Discord path remains unchanged.
- Dashboard broadcaster path remains unchanged.
- `--smoke-exit` does not start the TUI renderer.
- `doctor` fast path does not start the TUI renderer.

### 6.7 Scroll Navigation (K052)

- PageUp/PageDown move transcript viewport.
- Ctrl+Home/End move to extremes.
- Input and Footer remain fixed during manual scroll.
- Pinned-to-bottom auto-follow works for new output.
- Manual-scroll preserves distance-from-bottom, not visual line position.

## 7. K051/K052 Sub-Milestone Definitions

### K051a TerminalText and LumenFrame Foundation

Status: Completed by existing progress record.

Required scope:

- `TerminalText` display-width, wrap, and truncate helpers.
- `LumenFrame`.
- `FooterState`.
- `TerminalMetrics`.
- Unit tests for ASCII and Korean/CJK display width.

### K051b Lumen Frame Builder and State Evolution

Status: Completed by existing progress record.

Required scope:

- `LumenFrameBuilder`.
- Transcript viewport projection.
- Fixed input/footer frame projection.
- State additions for terminal metrics, footer state, scroll state, and render flags.
- Unit tests for 80-column and viewport behavior.

### K051c Lumen Terminal Renderer and Live Integration

Status: Completed.

Required scope:

- `LumenTerminalRenderer` with buffered ANSI frame output.
- Integration through `LumenRenderer` facade.
- `LumenCliApp` refresh path so prompt input appears immediately.
- Tests for frame output, cursor placement, footer/input non-accumulation, and fallback behavior where practical.

### K052 Lumen v2 Search and Scroll Navigation

Status: Completed.

Required scope:

- `ViewportScrollState` with `AutoScroll` and `ScrollOffset`.
- Scroll keyboard bindings (PageUp/Down, Ctrl+Home/End).
- `LumenFrameBuilder` offset-based projection.
- Tests for manual scroll persistence and input/footer fixity.

Completion evidence: 441/441 pass, K052 tests, release gate passed.

## 8. Next Milestone Candidate

# [DRAFT ONLY / NOT ACTIVE] Ralph Execution Card Candidate

## Milestone

K053 Lumen v2 UI Aesthetics and Polish

## Goal

Enhance the visual quality of the Lumen TUI with high-fidelity styles.

## Required Work (Draft)

- Add distinct separators between transcript and input.
- Use styled status indicators in the footer.
- Ensure all assistant/thought/tool outputs are consistently styled.
- Verify 80/120 column aesthetics.

## 9. Completed Lumen v1 Milestone Summary

These are retained for historical traceability. Do not re-run them unless a regression requires it.

| Milestone | Historical Purpose |
| --- | --- |
| K038 | Bootstrap and option parsing foundation |
| K039 | Neutral runtime observer foundation |
| K040 | State and history cells |
| K041 | Spectre renderer v1 |
| K042 | Output bridge |
| K043 | Prompt composer |
| K044 | Lumen interactive app |
| K045 | Approval dialog |
| K046 | Command output normalization |
| K047 | Piped input, Discord, legacy compatibility |
| K048 | Render quality and cancellation stabilization |
| K049 | Lumen v1 release gate and documentation |
| K050 | Transcript hygiene and observer mode fix |

## 10. Verification Standard

Standard commands:

```powershell
dotnet build -p:UseAppHost=false
dotnet test
dotnet .\Claude4Net.Cli\bin\Debug\net10.0\Claude4Net.Cli.dll --smoke-exit
dotnet run --project Claude4Net.Cli -- doctor --output-format json
```

Official gate:

```powershell
.\scripts\verify-release.ps1
```

Reviewer/final-controller checks:

```powershell
git status --short --branch
git diff --stat
git diff --cached --stat
git diff --cached --name-status
git diff --cached --check
git ls-files --others --exclude-standard
```

## 11. Manual Verification Matrix

Manual evidence is required before declaring a user-facing Lumen renderer milestone fully complete.
**Note: manual smoke is required before user-facing release declaration.**

- [x] Fresh `--lumen` startup render.
- [x] Current input buffer appears while typing.
- [x] Footer stays fixed and does not accumulate.
- [x] `/help`.
- [x] `/status`.
- [x] Normal prompt.
- [x] Streaming assistant response.
- [x] Tool call display.
- [x] Tool result display.
- [x] File read request.
- [x] File edit approval deny.
- [x] File edit approval allow.
- [x] Workspace error appears as one `ErrorCell`.
- [x] ESC cancellation during active run.
- [x] Ctrl+C exit.
- [x] `--legacy-cli` fallback.
- [x] `--smoke-exit`.
- [x] `doctor --output-format json`.
- [x] Piped input path.
- [x] Dashboard startup path.
- [x] Discord project compile path.
- [x] 80-column render.
- [x] 120-column render.
- [x] Korean/CJK wrapping sanity check.
- [x] Long tool output summarization.
- [x] No duplicate assistant/tool/error output in Lumen mode.
- [x] PageUp/PageDown scroll move transcript viewport.
- [x] Ctrl+Home/End move to extremes.
- [x] Input/Footer fixed during manual scroll.

## 12. Ralph Queue State Template

Ralph orchestrator may create `ralph-queue-state.md` using this shape:

```markdown
# Ralph Queue State

source_plan: Documents/Implementation_Plan.md
current_branch: experiment
queue_status: running

## Completed In This Run
- K052 Lumen v2 Search and Scroll Navigation

## Current Execution Card
- milestone: K052
- goal: Add transcript navigation while keeping input/footer fixed
- allowed_files:
  - Claude4Net.Cli/Ui/Rendering/LumenFrameBuilder.cs
  - Claude4Net.Cli/Ui/LumenReducer.cs
  - ...
- forbidden_files:
  - .agents/**
- done_when:
  - 8 tests pass, release gate passes
- verification:
  - dotnet test --filter "K052"
  - .\scripts\verify-release.ps1

## Remaining Queue
- K053 (Pending decision)

## Blocked Or Skipped
- None
```

## 13. Worker Result Template

Workers should write `worker-result.md` in this format when the Ralph Loop asks for it:

```markdown
# Ralph Worker Result

status: Completed | Partial | Implemented, Not Operationalized | Blocked

## Scope
-

## Changed Files
-

## New Files
-

## Implementation Summary
- Core:
- Safety:

## Verification
- git diff --cached --check:
- build:
- test:
- smoke:
- doctor:
- release gate:

## Remaining Risks
-

## Commit Push Status
Not performed
```

## 14. Reviewer Result Template

Reviewers should write `judge-result.md` in this format:

```markdown
# Ralph Judge Result

status: Approved | Rework Needed | Blocked | Handoff
legacy_status: PASS | FAIL
next_action: final-control | re-exec | stop | handoff
loop_count:

## Reviewed Milestone
-

## Verification Commands
- command:
  result:

## Changed Files
-

## Staged Files
-

## Untracked Files
-

## Findings
- Priority:
  File:
  Line:
  Problem:
  Required Fix:

## Rework Prompt
-

## Residual Risk
-

## Commit Push Status
Not performed
```

## 15. Final Control Result Template

Final controller should write `final-control-result.md` in this format:

```markdown
# Ralph Final Control Result

status: Approved | Pending | Blocked | Handoff
next_action: finish | stop | handoff

## Evidence
-

## Blocking Issues
-

## Remaining Risks
-

## Handoff Summary
-
```

## 16. Documentation Synchronization Rules

- `IMPLEMENTATION_PROGRESS.md` records verified completion evidence.
- `Documents/Implementation_Plan.md` records queue state, current card, and next card.
- `Documents/Project_Lumen_CLI_UI_Design_Plan.md` remains the original Lumen architecture source.
- `Documents/Project_Lumen_UI_UX_V2_External_Design_Review.md` remains the K051 UI/UX source.
- Do not keep conflicting status lines such as `Completed` and `pending` for the same milestone.
- If a milestone is implemented in the working tree but not reviewed/final-controlled, use `In Review / Final-Control Pending`.

## 17. Branch and Commit Policy

- Feature work starts on `experiment`.
- Stable release branches are not direct implementation targets unless explicitly selected.
- Commit and push are prohibited until final-controller/user approval.
- Do not use `git add .` or `git add -A` in automated instructions.
- Stage only files in the active Execution Card.

## 18. End Condition for Current Queue

The current queue can advance to K053 only when:

- K052 receives final approval or an explicit handoff decision.
- K051c and K052 documentation and progress records are consistent.
- No P1 renderer/input/footer/scroll regression remains.
- Required tests and release gate evidence are recorded.
