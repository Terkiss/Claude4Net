# Claude4Net Implementation Plan

Plan date: 2026-05-22
Working branch: `experiment`
Primary planning file: `Documents/Implementation_Plan.md`
Progress tracker: `IMPLEMENTATION_PROGRESS.md`
Design source: `Documents/2026-05-21_Claude4Net-App_인사이트_기반_확장_설계.md`
Backup before SSOT clean: `Documents/backups/2026-05-22/Implementation_Plan.pre-ssot-clean.2026-05-22.md`

Current focus: K081 Dashboard Typed Commands
Next milestone: K082 Dashboard UI Completion
Queue status: running


## 0. SSOT Purpose

This document is the active implementation SSOT for the next development wave. It intentionally removes historical execution cards and older Lumen work logs from the active plan.

Historical detail remains in:

- `IMPLEMENTATION_PROGRESS.md`
- `Documents/backups/2026-05-22/Implementation_Plan.pre-ssot-clean.2026-05-22.md`
- `Documents/Project_Lumen_CLI_UI_Design_Plan.md`
- `Documents/Project_Lumen_UI_UX_V2_External_Design_Review.md`

Repository state overrides old reports. If this plan conflicts with actual code or test results, verify the repository first and update this plan only with evidence.

## 1. Agent Read Order

Workers and reviewers must read context in this order:

1. `Documents/Implementation_Plan.md`
2. `IMPLEMENTATION_PROGRESS.md`
3. `Documents/2026-05-21_Claude4Net-App_인사이트_기반_확장_설계.md`
4. `git status --short --branch`
5. Recent commits and current staged/untracked files

Do not use old plan sections from backup as active instructions. Use backup only for historical lookup.

## 2. Non-Negotiable Rules

- Do not modify `.agents/`.
- Do not commit or push without Codex Final Controller or user approval.
- Do not run unrelated milestones in the same worker task.
- Do not implement the whole roadmap at once.
- Do not remove `--legacy-cli`.
- Do not break piped input, Discord, Dashboard, `--smoke-exit`, or `doctor` paths.
- Do not treat K054-K066 scaffolding tests as product completion unless command surface, runtime integration, and release-gate evidence exist.
- Do not expose Dashboard write/control actions unless they pass `PermissionEnforcer`, append audit/event records, and have explicit tests.
- Do not enable recurring routines by default; new routine automation must be opt-in, permission-aware, and bounded.
- Use `Completed` only after required tests and release gate evidence exist.
- Documents under `Documents/` may be ignored by git; stage intentional document changes with `git add -f`.

## 3. Current Verified Baseline

Current reality after SSOT clean:

- K038-K058 Lumen work is completed and retained as historical context only.
- K059-K066 remediation is completed and verified in progress records.
- Current implementation contains partial scaffolding for SeedSpec, routines, state isolation, skill proposal apply, provider descriptor loading, and dashboard pages.
- These scaffolds are not product-complete.
- Latest verified baseline before this SSOT clean: `dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --no-restore` passed 502/502, and `.\scripts\verify-release.ps1` passed.

Completion must be evidence-based. Do not mark K067-K084 complete from file existence alone.

## 4. Target Completion Definition

The 2026-05-21 expansion design is complete only when all of the following are true:

1. Workspace/session memory is isolated and checkpoint-restorable.
2. Important coordinate work cannot enter Execution without a locked SeedSpec when a spec is required or attached.
3. Workspace provider descriptors can be added or overridden without code changes.
4. Routines can be listed, shown, manually run, and scheduled through permission/checkpoint/event/verification gates.
5. Skill proposals can be generated, validated, approved, applied with checkpoint protection, and verified.
6. Dashboard can read runtime state and trigger only explicitly allowed typed control actions.
7. `dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false` passes.
8. `.\scripts\verify-release.ps1` passes.
9. `.agents/` is not modified.

## 5. Milestone Map

| Milestone | Design Area | Status | Target Completion |
| --- | --- | --- | --- |
| K067 | State Hygiene Completion | Completed | Remove app-base memory dependency from active paths; complete workspace/session scoped memory and snapshots |
| K068 | Memory Checkpoint Integration | Completed | File checkpoints and memory state snapshots restore together |
| K069 | SeedSpec Command Surface | Completed | `/spec new/show/question/answer/criteria/lock/attach` works |
| K070 | Coordinate Spec Enforcement | Completed | /coordinate start --spec, phase enforcement, AC-to-gate sync, blocking question policy |
| K071 | Provider Descriptor V2 Model | Completed | Endpoint, Headers, Metadata, validation, unknown category errors |
| K072 | Provider Settings Precedence | Completed | Built-in < system < user < workspace < env < CLI (530/530 pass) |
| K073 | Provider Factory Preparation | Completed | `IProviderFactory` and default factory registration without breaking current provider creation (544/544 pass) |
| K074 | Routine Command MVP | Completed | Expose routine definition management through slash commands; add path-safe ID checks (553/553 pass) |
| K075 | Routine Execution Integration | Completed | Make routine runs pass through safety layers, validation, checkpoint, hooks, event logging, verification (559/559 pass) |
| K076 | Routine Scheduler Hardening | Completed | Safety features, concurrency limit, DailyTime/Interval logic, timeouts, persistence (567/567 pass) |
| K077 | Skill Proposal Lifecycle | Completed | Active execution card completed |
| K078 | Skill Apply Engine | Completed | Active Execution Card completed |
| K079 | Skill Trajectory Mining | Completed | failure-pattern mining from trajectories/events/verification and proposal candidates (575/575 pass) |
| K080 | Dashboard Read Models | Completed | typed provider/coordinate/skill/routine/checkpoint/verification/state read APIs (585/585 pass) |
| K081 | Dashboard Typed Commands | In Progress | safe typed actions only; no arbitrary command execution |
| K082 | Dashboard UI Completion | Not Started | functional pages for Providers, Skills, Routines, Checkpoints, Verification, State |
| K083 | Release Gate Expansion | Not Started | expansion smoke tests added to `verify-release.ps1` |
| K084 | Final Integration and Documentation | Not Started | full pass, docs/progress sync, final risk review |
| K085 | Slash Command Palette | Not Started | `/` 입력 시 실시간 필터링 가능한 명령어 팔레트 오버레이 표시 |
| K086 | CLI Startup Arguments Expansion | Not Started | `--yolo`, `--setworkspace` 시작 인수 추가 및 YOLO 모드 권한 분기 |

## 6. Execution Order

Required order:

1. K067 State Hygiene Completion
2. K068 Memory Checkpoint Integration
3. K069 SeedSpec Command Surface
4. K070 Coordinate Spec Enforcement
5. K071 Provider Descriptor V2 Model
6. K072 Provider Settings Precedence
7. K073 Provider Factory Preparation
8. K074 Routine Command MVP
9. K075 Routine Execution Integration
10. K076 Routine Scheduler Hardening
11. K077 Skill Proposal Lifecycle
12. K078 Skill Apply Engine
13. K079 Skill Trajectory Mining
14. K080 Dashboard Read Models
15. K081 Dashboard Typed Commands
16. K082 Dashboard UI Completion
17. K083 Release Gate Expansion
18. K084 Final Integration and Documentation
19. K085 Slash Command Palette
20. K086 CLI Startup Arguments Expansion

Parallelization rules:

- K067 and K068 must not run in parallel because both touch memory/checkpoint state.
- K069 and K070 may be split only after K069 models/store/commands are stable.
- K071, K072, and K073 should run sequentially to avoid registry/factory conflicts.
- K074 can begin after K067 safety is complete, but K075 must wait for K074 command/store behavior.
- K077 can begin before K075, but K078 must wait for checkpoint safety from K068.
- K080 read models can begin after K071/K074/K077 are stable enough to expose state.
- K081 must wait for K080; K082 must wait for K080 and K081.

## 7. Active Execution Card: K081 Dashboard Typed Commands

Goal: Add safe control actions without restoring arbitrary remote command execution.

Allowed files:
- `Claude4Net.Dashboard/Hubs/ControlPlaneHub.cs` (or similar hub/API files)
- `Claude4Net.Dashboard.Client/`
- `Claude4Net.Tests/K081DashboardTypedCommandTests.cs` (or similar)
- `IMPLEMENTATION_PROGRESS.md`
- `Documents/Implementation_Plan.md`
- `ralph-queue-state.md`

Forbidden files:
- `.agents/`
- `.gemini/agents/`

Required work:
- Add safe control actions without restoring arbitrary remote command execution.
- Allowed methods: RunRoutine, RestoreCheckpoint, ApproveSkillProposal, RejectSkillProposal, ApplySkillProposal, RunVerification.
- Keep `ExecuteCommand(string)` denied.
- Every write/control action evaluates permission and appends audit/event data.
- Restore/apply actions require approval-capable permission mode.
- Errors are structured and user-safe.

Required tests:
- `K066DashboardCommandPermissionTests`
- New `K081DashboardTypedCommandTests`

Done when:
- All required work implemented.
- Targeted tests pass.
- Release gate passes.



## 8. Backlog Cards

### K071 Provider Descriptor V2 Model

Goal: Complete descriptor schema and fail-closed validation.

Required work:

- Add `Endpoint`, `Headers`, and `Metadata` to `ProviderDescriptor`.
- Validate required fields and endpoint URI.
- Parse routing categories case-insensitively from JSON.
- Fail closed for invalid descriptors and report which file failed.
- Keep permissive non-strict load only where explicitly needed.

Required tests:

- `K056ProviderDescriptorLoadingTests`
- New `K071ProviderDescriptorV2Tests`

### K072 Provider Settings Precedence

Goal: Implement complete descriptor and config precedence.

Precedence:

```text
Built-in descriptors
< system descriptors
< user descriptors
< workspace descriptors
< environment overrides
< CLI overrides
```

Required work:

- Load system descriptors from `{AppBase}/providers/*.json`.
- Load user descriptors from `%USERPROFILE%/.claude4net/providers/*.json`.
- Load workspace descriptors from `{workspace}/.claude4net/providers/*.json`.
- Merge user and workspace config with workspace taking precedence.
- Apply environment override variables for active provider/model where defined.
- Ensure CLI active model/provider wins over all config.
- SmartRouter and doctor paths must use the same resolved registry/config.

Required tests:

- `K057SettingsPrecedenceTests`
- New `K072ProviderPrecedenceTests`

### K073 Provider Factory Preparation

Goal: Introduce provider factories without forcing a risky provider creation rewrite.

Required work:

- Add `IProviderFactory`.
- Add default factories for Anthropic, Gemini, Ollama, Gemini CLI, and OpenAI-compatible descriptors.
- Keep existing provider creation switch as fallback during transition.
- Register factories through DI.
- Ensure `OpenAiCompatProviderFactory` can validate endpoint and auth mode.

Required tests:

- New `K073ProviderFactoryTests`
- Existing provider creation tests

### K074 Routine Command MVP

Goal: Expose routine definition management through slash commands.

Required commands:

```text
/routine list
/routine show <id>
/routine add <id> <name>
/routine enable <id>
/routine disable <id>
/routine delete <id>
/routine run <id>
```

Required behavior:

- New routines default to disabled unless explicitly enabled.
- IDs are path-safe.
- `show` displays trigger, actions, permission mode, workspace, last run, and enabled state.
- Delete removes definition only, not historical run records.

Required tests:

- `K058RoutineStoreTests`
- New `K074RoutineCommandTests`

### K075 Routine Execution Integration

Goal: Make routine runs pass through the same safety layers as normal tool work.

Execution order:

1. Validate routine definition.
2. Validate workspace path.
3. Evaluate permission mode.
4. Create checkpoint when action may modify files or state.
5. Execute HookPipeline before routine action.
6. Execute action.
7. Record `RoutineRunRecord`.
8. Append event store event.
9. Execute HookPipeline after routine action.
10. Run verification action when configured.

Required behavior:

- Script action is denied outside workspace.
- Read-only mode denies write/script actions.
- Slash command action only permits allowlisted commands initially.
- Prompt action creates a run request record, not an uncontrolled background agent loop.
- Verification action stores structured verification evidence.

Required tests:

- `K059RoutineRunnerPermissionTests`
- New `K075RoutineExecutionIntegrationTests`

### K076 Routine Scheduler Hardening

Goal: Make manual, interval, and daily routines safe for long-running use.

Required work:

- Support `Manual`, `Interval`, and `DailyTime`.
- Do not schedule disabled routines.
- Add next-run calculation.
- Add max concurrent runs per routine: 1.
- Add minimum interval floor to avoid hot loops.
- Add run timeout support.
- Persist last run and next run metadata.
- Reject unsupported `Webhook` and `Event` triggers for now.

Required tests:

- `K060RoutineSchedulerTests`
- New `K076RoutineSchedulerHardeningTests`

### K077 Skill Proposal Lifecycle

Goal: Complete lifecycle states and command group around skill proposals.

Required commands:

```text
/skill analyze
/skill proposals
/skill propose <skillId_or_path> <summary>
/skill validate <proposalId>
/skill approve <proposalId>
/skill reject <proposalId>
/skill apply <proposalId>
```

Required states:

```text
Draft -> Proposed -> Approved -> Applied -> Verified
Draft -> Proposed -> Rejected
Approved -> Superseded
Applied -> Failed
Failed -> Superseded
```

Required behavior:

- Existing `!skills`, `!skill-proposals`, and `!skill-propose` remain aliases.
- `validate` checks metadata, target path, status, and applyability.
- `approve` requires validation success.
- `apply` refuses anything not Approved.

Required tests:

- `K022SkillProposalTests`
- New `K077SkillProposalLifecycleTests`

### K078 Skill Apply Engine

Goal: Apply approved skill proposals safely.

Required work:

- Add patch preview or structured file change preview.
- Require approval before write.
- Create checkpoint before apply.
- Reject direct `.agents/` mutation.
- Permit only approved projection/safe paths.
- Apply patch or generated file changes.
- Record diff/evidence.
- Run verification after apply.
- Mark `Verified` on pass and `Failed` on fail.

Required tests:

- `K061SkillProposalApplyTests`
- New `K078SkillApplyEngineTests`

### K079 Skill Trajectory Mining

Goal: Generate proposal candidates from repeated failures.

Required work:

- Record skill usage success/failure and score.
- Mine `agent_trajectories`, event store, and verification results.
- Detect repeated failure classes by skill/tool/path/error.
- Generate proposal candidates with metadata linking evidence.
- Deduplicate similar proposal candidates.
- Do not auto-approve or auto-apply generated proposals.

Required tests:

- `K062SkillTrajectoryMiningTests`
- New `K079SkillTrajectoryMiningIntegrationTests`

### K080 Dashboard Read Models

Goal: Expose typed read APIs for the control plane.

Required hub/API methods:

```csharp
Task<ProviderControlPlaneState> GetProviders();
Task<CoordinateControlPlaneState> GetCoordinateTasks();
Task<CheckpointControlPlaneState> GetCheckpoints(string sessionId);
Task<VerificationControlPlaneState> GetVerification(string sessionId);
Task<SkillControlPlaneState> GetSkills();
Task<RoutineControlPlaneState> GetRoutines();
Task<StateControlPlaneState> GetState(string sessionId);
```

Required behavior:

- Read state primarily from event store, projections, registry services, and store services.
- Do not execute arbitrary commands.
- Return serializable DTOs only.
- Handle missing workspace/session gracefully.

Required tests:

- `K065DashboardControlPlaneTests`
- New `K080DashboardReadModelTests`

### K081 Dashboard Typed Commands

Goal: Add safe control actions without restoring arbitrary remote command execution.

Allowed methods:

```csharp
Task<CommandResult> RunRoutine(string routineId);
Task<CommandResult> RestoreCheckpoint(string checkpointId);
Task<CommandResult> ApproveSkillProposal(string proposalId);
Task<CommandResult> RejectSkillProposal(string proposalId, string reason);
Task<CommandResult> ApplySkillProposal(string proposalId);
Task<CommandResult> RunVerification(string sessionId);
```

Required behavior:

- Keep `ExecuteCommand(string)` denied.
- Every write/control action evaluates permission.
- Every write/control action appends audit/event data.
- Restore/apply actions require approval-capable permission mode.
- Errors are structured and user-safe.

Required tests:

- `K066DashboardCommandPermissionTests`
- New `K081DashboardTypedCommandTests`

### K082 Dashboard UI Completion

Goal: Replace placeholder pages with usable views.

Required views:

- Providers: descriptors, health/local/remote, default model, category, routing preview.
- Skills: registry, metrics, proposals, validate/approve/reject/apply buttons.
- Routines: list, enabled/disabled, trigger, last run, next run, manual run.
- Checkpoints: session checkpoint list, changed files, memory snapshot flag, restore action.
- Verification: latest result, checks, evidence, run verification action.
- State: memory table summary, session record count, snapshot list, restore action if allowed.

Required UI constraints:

- No arbitrary command input box.
- Disable buttons when permission is insufficient.
- Show clear pending/success/failure states.
- Avoid direct runtime state scraping from the client.

Required tests:

- Blazor build passes.
- Hub call failures are rendered safely.
- Important buttons call typed hub methods, not `ExecuteCommand`.

### K083 Release Gate Expansion

Goal: Make release verification catch regressions in the new architecture.

Required release steps:

```powershell
Run-Step "State Isolation Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K063|FullyQualifiedName~K064"
}

Run-Step "Spec Gate Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K054|FullyQualifiedName~K055|FullyQualifiedName~K069|FullyQualifiedName~K070"
}

Run-Step "Provider Descriptor Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K056|FullyQualifiedName~K057|FullyQualifiedName~K071|FullyQualifiedName~K072"
}

Run-Step "Routine Permission Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K058|FullyQualifiedName~K059|FullyQualifiedName~K060|FullyQualifiedName~K074|FullyQualifiedName~K075|FullyQualifiedName~K076"
}

Run-Step "Dashboard Control Plane Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K065|FullyQualifiedName~K066|FullyQualifiedName~K080|FullyQualifiedName~K081"
}
```

Required behavior:

- Keep the full unit/integration test step.
- Add focused smoke steps after full tests or in a clearly named architecture smoke section.
- Do not make the release script depend on network or real provider credentials.

### K084 Final Integration and Documentation

Goal: Close the roadmap with evidence and synchronized documentation.

Required work:

- Update `IMPLEMENTATION_PROGRESS.md` with K067-K084 evidence only after verification.
- Update this plan's milestone table with final statuses.
- Record final test counts and release gate output.
- List any known residual risk.
- Confirm `.agents/` was not modified.
- Confirm no unrelated dirty files were staged or reverted.

### K085 Slash Command Palette

Goal: `/` 입력 시 등록된 슬래시 명령어를 실시간 필터링 가능한 팝업 오버레이로 표시하여 명령어 검색성과 접근성을 개선한다.

Dependency: K071 (Completed)

Allowed files:

- `Claude4Net.Cli/Ui/LumenCliApp.cs`
- `Claude4Net.Cli/Ui/Input/PromptComposer.cs`
- `Claude4Net.Cli/Ui/Rendering/LumenFrameBuilder.cs`
- `Claude4Net.Cli/Ui/LumenState.cs`
- `Claude4Net.Cli/Ui/Events/` (신규 이벤트 파일 허용)
- `Claude4Net.Tests/` (신규 테스트 파일 허용)
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:

- `PromptComposer`에 `/` 입력 감지 시 팔레트 모드 전환 로직 추가
- `LumenState`에 `IsCommandPaletteVisible`, `PaletteFilterText`, `PaletteSelectedIndex` 상태 추가
- 팔레트 활성화 시 ArrowUp/ArrowDown을 메뉴 항목 선택으로 리다이렉트하는 모달 입력 상태 기계 구현
- `CommandRegistry.All`에서 동적 필터링된 명령 목록을 `LumenFrameBuilder`에서 프롬프트 영역 위에 오버레이 패널로 렌더링
- Enter 키로 선택된 명령어 자동완성, Escape로 팔레트 닫기
- 최대 표시 행 수 5개로 제한하고 스크롤 래핑 지원

Required tests:

- 신규 `K085SlashCommandPaletteTests` (팔레트 열기/닫기/필터링/선택 동작 검증)

### K086 CLI Startup Arguments Expansion

Goal: CLI 시작 시 `--yolo`, `--setworkspace <경로>` 등의 시작 인수를 추가하여 파이프라인 통합과 자동화 환경 설정을 간소화한다.

Dependency: K071 (Completed)

Allowed files:

- `Claude4Net.Cli/Bootstrap/CliOptions.cs`
- `Claude4Net.Cli/Program.cs`
- `Claude4Net.Runtime/PermissionEnforcer.cs`
- `Claude4Net.Tests/` (신규 테스트 파일 허용)
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:

- `CliOptions.cs`에 `--yolo` 플래그 파싱 추가 (내부적으로 `PermissionModeArg = "yolo"` 매핑)
- `CliOptions.cs`에 `--setworkspace <경로>` 옵션 파싱 추가 (`WorkspaceDir` 프로퍼티)
- `Program.cs`에서 `--setworkspace` 경로 유효성 검증 및 `AppState.CurrentCwd` 설정
- `PermissionEnforcer.Evaluate()`에서 `DangerFullAccess` 모드일 때 워크스페이스 내부 동작은 즉시 `Allow` 반환 (승인 창 스킵)
- `PermissionEnforcer.Evaluate()`에서 `DangerFullAccess` 모드라도 `PathSafetyResult.Outside`는 `RequireApproval` 유지
- 일반 모드에서 Outside는 기존대로 `Deny`

Required tests:

- 신규 `K086CliStartupArgsTests` (YOLO 모드 내부 Allow / 외부 RequireApproval 검증)
- 신규 `K086WorkspaceArgTests` (`--setworkspace` 경로 검증)

## 9. Verification Standard

Standard commands:

```powershell
dotnet build -p:UseAppHost=false
dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false
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

## 10. Documentation Synchronization Rules

- `IMPLEMENTATION_PROGRESS.md` records verified completion evidence.
- `Documents/Implementation_Plan.md` records queue state, current card, and next card.
- Historical execution details belong in backups or progress logs, not in the active SSOT.
- Do not keep conflicting status lines such as `Completed` and `pending` for the same milestone.
- If a milestone is implemented in the working tree but not reviewed/final-controlled, use `In Review / Final-Control Pending`.
- When a milestone completes, update this file by moving the next K-card into section 7 as the active execution card.

## 11. Branch and Commit Policy

- Feature work starts on `experiment`.
- Stable release branches are not direct implementation targets unless explicitly selected.
- Commit and push are prohibited until final-controller/user approval.
- Do not use `git add .` or `git add -A` in automated instructions.
- Stage only files in the active Execution Card.

## 12. Completion Dashboard

Do not mark any item checked until implementation, tests, and release evidence exist.

- [x] K067 State Hygiene Completion
- [x] K068 Memory Checkpoint Integration
- [x] K069 SeedSpec Command Surface
- [x] K070 Coordinate Spec Enforcement
- [x] K071 Provider Descriptor V2 Model
- [x] K072 Provider Settings Precedence
- [x] K073 Provider Factory Preparation
- [x] K074 Routine Command MVP
- [x] K075 Routine Execution Integration
- [x] K076 Routine Scheduler Hardening
- [x] K077 Skill Proposal Lifecycle
- [x] K078 Skill Apply Engine
- [x] K079 Skill Trajectory Mining
- [x] K080 Dashboard Read Models
- [ ] K081 Dashboard Typed Commands
- [ ] K082 Dashboard UI Completion
- [ ] K083 Release Gate Expansion
- [ ] K084 Final Integration and Documentation
- [ ] K085 Slash Command Palette
- [ ] K086 CLI Startup Arguments Expansion

The expansion roadmap is complete only when every item above is checked and K084 records a full test and release-gate pass.
