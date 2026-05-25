# Claude4Net Implementation Plan

Plan date: 2026-05-22
Working branch: `experiment`
Primary planning file: `Documents/Implementation_Plan.md`
Progress tracker: `IMPLEMENTATION_PROGRESS.md`
Design source: `Documents/2026-05-21_Claude4Net-App_인사이트_기반_확장_설계.md`
Backup before SSOT clean: `Documents/backups/2026-05-22/Implementation_Plan.pre-ssot-clean.2026-05-22.md`

Current focus: K091
Next milestone: K092
Queue status: active


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
| K081 | Dashboard Typed Commands | Completed | safe typed actions only; no arbitrary command execution (593/593 pass) |
| K082 | Dashboard UI Completion | Completed | functional pages for Providers, Skills, Routines, Checkpoints, Verification, State (595/595 pass) |
| K083 | Release Gate Expansion | Completed | expansion smoke tests added to `verify-release.ps1` (595/595 pass) |
| K084 | Final Integration and Documentation | Completed | full pass, docs/progress sync, final risk review (595/595 unit tests + 101 smoke tests pass) |
| K085 | Slash Command Palette | Completed | / 입력 시 실시간 필터링 가능한 명령어 팔레트 오버레이 표시 (601/601 pass) |
| K086 | CLI Startup Arguments Expansion | Completed | YOLO mode permission routing and workspace dir options implemented (609/609 pass) |
| K087 | Skill Store Scope Separation | Completed | 글로벌 및 로컬 스킬 저장소 구조 분리 구현 및 검증 완료 (613/613 pass) |
| K088 | Pandas Sync & Hygiene | Completed | TeruTeruPandas net10.0 동기화 & 저장소 위생 정리 |
| K089 | Usage Dashboard | Completed | /usage 실사용량·비용·성능 관측 대시보드 구현 (5e918ef) |
| K090 | LSP/MCP Integration | Completed | LSP/MCP 실전 연결 완성 및 Mock Coverage 강화 |
| K091 | Concurrency Hardening | Completed | 승인 대기열 동시성 하드닝 & Idempotent Approval Engine |
| K092 | Multi-Session Replay | Proposed | Dashboard Multi-Session Observatory & Replay View |
| K093 | Self-Healing v2 | Proposed | Self-Healing v2: 실패 분류 확장과 복구 전략 추천 엔진 |
| K094 | Self-Evolving Skills | Proposed | SkillUsageRecorder 실연결 & Self-Evolving Skills 루프 완성 |
| K095 | Security Profiles | Proposed | Security Policy Profiles & Red-Team Regression Harness |
| K096 | Plan/Dry-Run Mode | Proposed | Plan/Dry-Run 모드: 실행 전 영향 범위 분석과 변경 예측 |
| K097 | Release Control Tower | Proposed | Routine Scheduler v2 & Release Automation Control Tower |

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
21. K087 Skill Store Scope Separation
22. K088 Pandas Sync & Hygiene
23. K089 Usage Dashboard
24. K090 LSP/MCP Integration
25. K091 Concurrency Hardening
26. K092 Multi-Session Replay
27. K093 Self-Healing v2
28. K094 Self-Evolving Skills
29. K095 Security Profiles
30. K096 Plan/Dry-Run Mode
31. K097 Release Control Tower

Parallelization rules:

- K067 and K068 must not run in parallel because both touch memory/checkpoint state.
- K069 and K070 may be split only after K069 models/store/commands are stable.
- K071, K072, and K073 should run sequentially to avoid registry/factory conflicts.
- K074 can begin after K067 safety is complete, but K075 must wait for K074 command/store behavior.
- K077 can begin before K075, but K078 must wait for checkpoint safety from K068.
- K080 read models can begin after K071/K074/K077 are stable enough to expose state.
- K081 must wait for K080; K082 must wait for K080 and K081.

## 7. Active Execution Card: K091 (Completed)

Active card: K091 승인 대기열 동시성 하드닝 & Idempotent Approval Engine. All features are fully implemented, and verified via unit/integration tests.


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

### K087 Skill Store Scope Separation

Goal: 글로벌 스킬 저장소(실행 파일 경로의 `skills/`) 및 로컬 스킬 저장소(워크스페이스 `.claude4net/skills/`) 구조를 명확히 분리하고, 기술 검증 및 SSOT 연동을 완료한다.

Dependency: K018, K078 (Completed)

Allowed files:

- `Claude4Net.Runtime/SelfEvolvingSkills.cs`
- `Claude4Net.Runtime/SkillApplyEngine.cs`
- `Claude4Net.Runtime/SkillRegistryService.cs`
- `Claude4Net.Tests/K018SkillRegistryTests.cs`
- `Claude4Net.Tests/K078SkillApplyEngineTests.cs`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:

- `SkillRegistryService` 저장소 경로 및 discovery 동작 확장
- `SkillApplyEngine`에서 글로벌/로컬 스킬 대상 경로 처리 보강 및 CheckpointStore 우회 추가
- `SelfEvolvingSkills`에서 구조 분리에 따른 자가 진화 경로 적용

Required tests:

- `K018SkillRegistryTests` 글로벌 및 로컬 구조 분리 동작 검증 추가
- `K078SkillApplyEngineTests` 글로벌 및 로컬 대상 경로 적용 동작 검증 추가

### K088 TeruTeruPandas net10.0 동기화 & 저장소 위생 정리

Goal: TeruTeruPandas의 .NET 10.0 타겟 동기화 및 빌드/런타임 불필요 잔재 정리

Dependency: K087

Allowed files:
- `TeruTeruPandas/TeruTeruPandas.csproj`
- `Claude4Net.Cli/Claude4Net.Cli.csproj`
- `Claude4Net.Runtime/Claude4Net.Runtime.csproj`
- `Claude4Net.Tests/Claude4Net.Tests.csproj`
- `Claude4Net.MyPlugins/Claude4Net.MyPlugins.csproj`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- 모든 프로젝트 파일의 target framework가 `net10.0`으로 완전히 동기화되었는지 검증 및 통일
- 빌드 클린 타겟에 `db/*.db` 및 런타임 과도기적 파일 정리 규칙 선언
- 동시 테스트 실행 시 SQLite 커넥션 풀 경합 및 락 현상을 예방하기 위해 리소스 해제 규칙 보강

Required tests:
- warning-free 컴파일 및 `dotnet test` 통과 검증

### K089 /usage 실사용량·비용·성능 관측 대시보드 구현

Goal: API 토큰 실사용량, latencies, 누적 비용을 분석하고 /usage 커맨드 및 Dashboard 전용 뷰 제공

Dependency: K080, K085

Allowed files:
- `Claude4Net.Commands/CommandRegistry.cs`
- `Claude4Net.Dashboard/Controllers/`
- `Claude4Net.Dashboard/Hubs/`
- `Claude4Net.Dashboard.Client/Pages/Usage.razor`
- `Claude4Net.Runtime/EventProjectionEngine.cs`
- `Claude4Net.Tests/`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- API 사용량, 토큰 수, 지연 시간(EMA) 및 비용 환산 집계용 Read Model/Projection 설계
- CLI에서 구동 가능한 `/usage` 슬래시 커맨드 추가 (Spectre.Console 테이블 형식 출력)
- Dashboard Client 프로젝트에 실시간 사용 데이터 가시화용 `Usage.razor` 및 요약 차트 개발
- ProviderDescriptor의 가격 정책 메타데이터 바인딩 연계

Required tests:
- 신규 `K089UsageTrackingTests` (누적 토큰 집계, latencies EMA 수렴 검증)

### K090 LSP/MCP 실전 연결 완성 및 Mock Coverage 강화

Goal: Model Context Protocol (MCP) 및 LSP 연동 클라이언트를 내재화하고 견고한 모의(Mock) 검증 구현

Dependency: K073

Allowed files:
- `Claude4Net.Runtime/Mcp/` (신규 폴더/파일 허용)
- `Claude4Net.Tools/`
- `Claude4Net.Tests/`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- MCP 사양에 따르는 클라이언트 모듈 개발 (도구/프롬프트/리소스 탐색 및 동적 바인딩)
- Dynamic Tool Registry에 MCP 도구 목록 동적 주입 및 ToolOrchestrator 위임 체계 연동
- 통신이 발생하지 않는 단위/통합 테스트를 위해 고성능 LSP/MCP 서버 모형 Mock 클래스 구현

Required tests:
- 신규 `K090McpLspTests` (Mock 기반 도구 등록, 스키마 변환 및 호출 E2E 검증)

### K091 승인 대기열 동시성 하드닝 & Idempotent Approval Engine

Goal: CLI/WebUI/Discord 다중 승인 채널의 동시 요청 충돌을 방어하고 멱등적(Idempotent) 승인 엔진 구축

Dependency: K075, K081

Allowed files:
- `Claude4Net.Runtime/ToolOrchestrator.cs`
- `Claude4Net.Cli/Ui/LumenApprovalHandler.cs`
- `Claude4Net.Dashboard/Hubs/AgentHub.cs`
- `Claude4Net.Tests/`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- 다중 채널에서 동시 접근 시 상태 경합을 제어하는 `ApprovalQueue` 락 동시성 제어 고도화
- 동일 RequestId에 대해 중복 승인/반려 시그널이 도달할 때, 최초 결정을 유지하고 중복 처리를 방지하는 Idempotency 로직 구현
- 충돌성 결정(Conflicting Decision) 발생 시 명확한 로그 기록 및 사용자 채널 알림/예외 통제

Required tests:
- 신규 `K091ApprovalConcurrencyTests` (동시 multi-channel 시그널 인입 멱등성 검증)

### K092 Dashboard Multi-Session Observatory & Replay View

Goal: 대시보드에 다중 세션 탐색 및 JSONL 기반 타임트래블 리플레이 슬라이더 뷰 추가

Dependency: K082

Allowed files:
- `Claude4Net.Dashboard/`
- `Claude4Net.Dashboard.Client/Pages/Sessions.razor` (신규)
- `Claude4Net.Dashboard.Client/Pages/Replay.razor` (신규)
- `Claude4Net.Tests/`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- 세션 저장소 디렉토리의 전체 리스트 조회 API 구축 및 Sessions 페이지 구현
- JSONL 기반 과거 이벤트를 재생하는 타임트래블 Replay Slider 프론트엔드 컴포넌트 개발
- 특정 시점의 State Reconstruction 데이터를 브라우징할 수 있는 스냅샷 뷰어 구현
- 실시간 연결 상태 유지하며 세션 스위칭 시 메모리 누수 방지

Required tests:
- 신규 `K092DashboardReplayTests` (JSONL 파싱 복원력 및 read-model 복원 정확성 검증)

### K093 Self-Healing v2: 실패 분류 확장과 복구 전략 추천 엔진

Goal: 에러 Taxonomy를 세분화하고 대안 모델 라우팅/프롬프트 동적 조정 복구 전략 추천 엔진 설계

Dependency: K079

Allowed files:
- `Claude4Net.Runtime/SelfHealingService.cs`
- `Claude4Net.Runtime/ErrorClassifier.cs`
- `Claude4Net.Tests/`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- JSON 스키마 미스매치, Rate Limit, Context Limit Over, Symlink 탈출 위반 등 구체적 에러 카테고리 세분화
- 복구 전략 추천 엔진(Recovery Strategy Recommender)을 구축하여 dynamic retry parameters, prompt injections 처방
- AgentLoop에 복구 처방을 피드백하여 실행 중 자동 복구 복잡도 개선

Required tests:
- 신규 `K093SelfHealingV2Tests` (에러별 추천 전략 적합성 및 런타임 적용 복구 시나리오 검증)

### K094 SkillUsageRecorder 실연결 & Self-Evolving Skills 루프 완성

Goal: 스킬 실사용 성공률/지연시간 기록 체계를 활성화하고, 지속적 오류 감지 시 자동 개선 제안 루프 완성

Dependency: K079, K087

Allowed files:
- `Claude4Net.Runtime/SelfEvolvingSkills.cs`
- `Claude4Net.Runtime/SkillUsageRecorder.cs` (신규)
- `Claude4Net.Runtime/ToolOrchestrator.cs`
- `Claude4Net.Tests/`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- `ToolOrchestrator` 실행 단계에 `SkillUsageRecorder` 인터셉터를 부착하여 실데이터 누적
- 영속 저장소 `.claude4net/skill-usage.jsonl`에 성과 메타데이터 로깅
- 특정 스킬의 오류 빈도 임계치 도달 시, `SelfEvolvingSkills` 서비스가 자동으로 `SkillProposal` 생성 트리거하도록 구성

Required tests:
- 신규 `K094SkillEvolutionTests` (누적 사용에 따른 자동 제안 트리거 루프 테스트)

### K095 Security Policy Profiles & Red-Team Regression Harness

Goal: 보안 등급 프로파일(Strict/Permissive/Development) 파서 추가 및 회귀 방지 레드팀 시뮬레이터 구축

Dependency: K086

Allowed files:
- `Claude4Net.Runtime/PermissionEnforcer.cs`
- `Claude4Net.Runtime/SecurityPolicyConfig.cs` (신규)
- `Claude4Net.Tests/` (신규 하네스 테스트 폴더 허용)
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- 보안 프로파일 설정을 나타내는 JSON 스키마 및 파일 바인딩 기능 제공
- PermissionEnforcer에 로드된 프로파일 매핑 적용 (허용 명령어/경로 규칙 등 세부 통제)
- 디렉토리 탈출 시도, 비인가 명령어 주입 등의 행위를 재현하고 자동 모니터링하는 레드팀 리그레션 하네스(Security Harness) 구현

Required tests:
- 신규 `K095RedTeamSecurityTests` (공격 기법별 차단 및 프로파일별 예외 정책 확인)

### K096 Plan/Dry-Run 모드: 실행 전 영향 범위 분석과 변경 예측

Goal: 파일 수정이나 상태 변경 액션을 수반하는 커맨드 실행 전 가상 변경 범위를 리포팅하는 Dry-run 엔진 개발

Dependency: K086

Allowed files:
- `Claude4Net.Commands/CommandRegistry.cs`
- `Claude4Net.Runtime/AgentLoop.cs`
- `Claude4Net.Runtime/DryRunEngine.cs` (신규)
- `Claude4Net.Tests/`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- CLI 기동 시 `--dry-run` 및 슬래시 커맨드 `/plan` 지원 추가
- 실제 파일 디스크 기록과 상태 스토어 저장을 가상 격리 처리하는 DryRunEngine 구축
- 변경 발생 대상 파일 경로, ToolCall 목록, 예상 영향 범위 보고서(Impact Report) 생성
- 터미널 출력용 포맷팅 패널 구현

Required tests:
- 신규 `K096DryRunTests` (실제 파일 미변경 여부 및 예측 보고 데이터 무결성 검증)

### K097 Routine Scheduler v2 & Release Automation Control Tower

Goal: 크론(CRON) 트리거 스케줄링 완성과 함께 대시보드 내 중앙 릴리스 자동화 관제탑 화면 제공

Dependency: K076, K082, K083

Allowed files:
- `Claude4Net.Runtime/RoutineSchedulerService.cs`
- `Claude4Net.Commands/CommandRegistry.cs`
- `Claude4Net.Tests/`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Required work:
- RoutineSchedulerService에 standard 5-field CRON 식 해석기 탑재
- 스케줄링된 주기마다 릴리스 테스트 스크립트(`verify-release.ps1`)를 백그라운드 구동 가능하도록 자동화 연동
- 대시보드 내 통합 Control Tower 뷰 개발 (빌드 상태, 루틴 스케줄 캘린더, 릴리스 게이트 상태 가시화)
- 안전 스레드 스케줄링 락 및 중복 동작 방어 보강

Required tests:
- 신규 `K097SchedulerV2Tests` (CRON 파싱 정확도 및 Control Tower DTO 전송 연동 검증)

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
- [x] K081 Dashboard Typed Commands
- [x] K082 Dashboard UI Completion
- [x] K083 Release Gate Expansion
- [x] K084 Final Integration and Documentation
- [x] K085 Slash Command Palette
- [x] K086 CLI Startup Arguments Expansion
- [x] K087 Skill Store Scope Separation
- [x] K088 Pandas Sync & Hygiene
- [x] K089 Usage Dashboard
- [x] K090 LSP/MCP Integration
- [x] K091 Concurrency Hardening
- [ ] K092 Multi-Session Replay
- [ ] K093 Self-Healing v2
- [ ] K094 Self-Evolving Skills
- [ ] K095 Security Profiles
- [ ] K096 Plan/Dry-Run Mode
- [ ] K097 Release Control Tower

The expansion roadmap is complete only when every item above is checked and K084 records a full test and release-gate pass.
