# Claude4Net Implementation Progress (D01-D10)

## Overview
- Start Date: 2024-05-22
- Current Base Commit: `889c4be` (K012 Final Handoff stable baseline)
- Status: Stable Baseline (K012 Complete)
- Target: Maintain the D01-D10 baseline and handle post-release fixes without reopening completed milestones.

## Domain Status

| Domain | Description | Status | Completion Range |
| --- | --- | --- | --- |
| D01 | Baseline, Project Safety, Build/Test Standards | Completed | P001-P005 (Release gate/docs baseline complete) |
| D02 | TeruTeruPandas Memory Sharing | Completed | P011-P020 (RAG, Migration, Memory Ops complete) |
| D03 | Sandboxing/Permission State Machine | Completed | P021-P030 (Evaluator & Audit logging complete) |
| D04 | Diagnostics, Source Guard, Masking | Completed | P031-P040 (Refined masking & Doctor diagnostics complete) |
| D05 | SmartRouter | Completed | P041-P050 (Cost-aware routing, Provider health monitoring complete) |
| D06 | Resources-Oriented Skills | Completed | P051-P060 (Loader, Caching, Prompt integration, Full templates complete) |
| D07 | Discord Async Orchestration | Completed | P061-P070 (Approval flow, retry logic, multi-handler complete) |
| D08 | Coordinate (/coordinate) | Completed | P071-P080 (Evidence, Merge Readiness, Gate enforcement complete) |
| D09 | Agent Trajectories & Self-Healing | Completed | P081-P090 (Intelligent classification, Retry policies, Pruning complete) |
| D10 | Testing, Documentation, Release | Completed | P091-P100 (Release suite & docs complete) |
| K001 | Workspace Hygiene | Completed | Temp logs removed |
| K002 | Progress SSOT | Completed | P001-P005 updated |
| K003 | Release Gate | Completed | --smoke-exit verification baseline |
| K004 | D10 Docs | Completed | Official Docs & Operations manual |
| K005 | D07 Discord Approval | Completed | Button-based approval, permission check, retry utils |
| K006 | D08 Coordinate Evidence | Completed | Evidence-backed gates, Merge Readiness scoring |
| K007 | D09 Self-Healing Quality | Completed | Regex-based taxonomy, Retry library, !prune command |
| K008 | D02 Memory Ops | Completed | Schema migration, RAG stability, Dimension safety |
| K009 | D05 Smart Router Ops | Completed | Cost-awareness, EMA tracking, Exponential backoff |
| K010 | D03/D04 Security Hardening | Completed | Audit logging, Refined masking, !doctor diagnostics |
| K011 | Performance & Release Confidence | Completed | Embedding cache, AgentLoop optimization, 76/76 pass |
| K012 | Final Stabilization and Handoff | Completed | Security audit, user manual, handoff docs, 77/77 pass |
| K013-1 | Local Model Protection Policy | Completed | Latency exemption, Local bonus (+2000), 78/78 pass |
| K013-2 | Explicit Model Routing Fix | Completed | Respect AppState.ActiveModel (81/81 pass) |
| K013-3 | Provider Logic & Naming Fix | Completed | Defend gemini-cli, real model names, 82/82 pass |
| K013-4 | Gemini Function Calling Fix | Completed | Fix 400 INVALID_ARGUMENT after tool call (85/85 pass) |
| K013-5 | Gemini Thought Signature Fix | Completed | Preserve Gemini thoughtSignature on functionCall history (86/86 pass) |
| K015 | P0 Reliability Preflight and Permission Foundation | Completed | 93/93 pass |
| K016 | P1 Session Store and Task Board | Completed | 118/118 pass |
| K017 | P2 Diff Approval Workflow | Completed | 123/123 pass |
| K018 | P3 Skill Registry Foundation | Completed | 138/138 pass |
| K021 | Gemini thought-signature/tool-turn compatibility hardening | Completed | 138/138 pass |
| K022 | Skill evolution proposal workflow | Completed | 145/145 pass |
| K023 | Context Window Management and Token Counting | Completed | 151/151 pass |
| K024 | Event-Sourced State and Resumable Sessions | Completed | 156/156 pass |
| K025 | Security Hardening and Symbolic Link Protection | Completed | 162/162 pass |
| K026 | Self-Healing Loop Hardening | Completed | 167/167 pass |
| K027 | Multi-Agent Coordination MVP | Completed | 173/173 pass |
| K028 | Monitoring Dashboard | Completed | 180/180 pass (dashboard startup, workspace replay, and payload preservation hotfixes) |
| K029 | Checkpoint and Rewind Foundation | Completed | 187/187 pass |
| K030 | State Machine and Oscillation Detection | Completed | 190/190 pass (rework included) |
| K2930-1 | Encoding & Corrupted String Fix (P1) | Completed | Build Pass, Korean strings restored |
| K032 | Verification Gate Hardening | Completed | 219/219 pass |
| K031 | Provider Descriptor and Router V2 | Completed | 233/233 pass |
| K034 | Event Store v2 and CQRS Projections | Completed | 246/246 pass |
| K033 | Skill and Hook Operations | Completed | 258/258 pass |
| K035 | Agentic Search, Memory Strategy, and Audit Traceability | Completed | 279/279 pass after P1 integration stabilization |
| K036 | Ollama ToolResult Grounding and Context Window Hotfix | Completed | 287/287 pass |
| K037 | Gemini Structured Tool Result Compatibility Hotfix | Completed | 290/290 pass |
| K038 | Project Lumen Bootstrap Foundation | Completed | 294/294 pass |
| K039 | AgentRunEvent Observer Foundation | Completed | 300/300 pass |
| K040 | Lumen State and History Cells | Completed | 308/308 pass |
| K041 | Spectre Renderer v1 | Completed | 310/310 pass |
| K042 | Lumen Output Bridge | Completed | 316/316 pass (including fail-safe and tool-id tests) |
| K043 | Prompt Composer Foundation | Completed | 328/328 pass (CLI input buffer & Key handling) |
| K044 | LumenCliApp v1 | Completed | 338/338 pass |
| K045 | Approval Dialog v1 | Completed | 359/359 pass |
| K049 | Lumen Release Gate | Completed | 378/378 pass (Lumen v1 Closure) |
| K051a | TerminalText and LumenFrame Foundation | Completed | 401/401 pass |
| K051b | Lumen Frame Builder and State Evolution | Completed | 417/417 pass |
| K051c | Lumen Terminal Renderer and Live Integration | Completed | 433/433 pass |
| K052 | Lumen v2 Search and Scroll Navigation | Completed | 441/441 pass |
| K053a | Lumen Render Fidelity Hotfix | Completed | 450/450 pass |
| K053b | Lumen Bottom Pane Anchoring Hotfix | Completed | 456/456 pass |
| K054 | Release Gate Stabilization | Completed | 461/461 pass |
| K055 | Lumen Approval Dialog Integration | Completed | 472/472 pass |
| K056a | Worktree Hygiene Pre-clean | Completed | Workspace inspected; unrelated dirty files preserved |
| K056b | Release Gate And Documentation Sync | Completed | 472/472 pass |
| K057 | Manual TUI Fidelity Pass | Completed | 472/472 pass |
| K058 | Lumen UX Polish | Completed | 476/476 pass |
| K059-K066 | Final-Control P1 Remediation | Completed | 502/502 pass |
| K067 | State Hygiene Completion | Completed | 502/502 pass |
| K068 | Memory Checkpoint Integration | Completed | 504/504 pass |
| K069 | SeedSpec Command Surface | Completed | 512/512 pass |
| K070 | Coordinate Spec Enforcement | Completed | 517/517 pass |
| K071 | Provider Descriptor V2 Model | Completed | 527/527 pass |
| K072 | Provider Settings Precedence | Completed | 530/530 pass |
| K073 | Provider Factory Preparation | Completed | 544/544 pass |
| K074 | Routine Command MVP | Completed | 553/553 pass |
| K075 | Routine Execution Integration | Completed | 559/559 pass |
| K076 | Routine Scheduler Hardening | Completed | 567/567 pass |
| K077 | Skill Proposal Lifecycle | Completed | 570/570 pass |
| K078 | Skill Apply Engine | Completed | 574/574 pass |
| K079 | Skill Trajectory Mining | Completed | 575/575 pass |
| K080 | Dashboard Read Models | Completed | 585/585 pass |
| K081 | Dashboard Typed Commands | Completed | 593/593 pass |
| K082 | Dashboard UI Completion | Completed | 595/595 pass |
| K083 | Release Gate Expansion | Completed | verify-release.ps1 expansion & env isolation verified (595/595 pass) |

| K084 | Final Integration and Documentation | Completed | Full release-gate pass (595/595 unit tests + 101 smoke tests pass) |
| K085 | Slash Command Palette | Completed | Interactive filtering command overlay popup implemented (601/601 pass) |
| K086 | CLI Startup Arguments Expansion | Completed | YOLO mode permission routing and workspace dir options implemented (609/609 pass) |
| K087 | Skill Store Scope Separation | Completed | Global/local skill store separation implemented and verified (613/613 pass) |
| K090 | LSP/MCP Integration | Completed | 620/620 pass (Mock MCP/LSP transport, Registry integration, E2E validation) |
| K091 | 승인 대기열 동시성 하드닝 & Idempotent Approval Engine | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (da45a19) |
| K092 | Dashboard Multi-Session Observatory & Replay View | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (530f18c) |
| K093 | Self-Healing v2: 실패 분류 확장과 복구 전략 추천 엔진 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (fe5c583) |
| K094 | SkillUsageRecorder 실연결 & Self-Evolving Skills 루프 완성 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (94b3989) |
| K095 | Security Policy Profiles & Red-Team Regression Harness | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (82fb468) |
| K096 | Plan/Dry-Run 모드: 실행 전 영향 범위 분석과 변경 예측 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (0a3a1be) |
| K097 | Release Control Tower / Routine Scheduler v2 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (85ef5ea) |
| K098 | API Startup & Infrastructure | Completed | Parse `--api` arguments, configure hosting, isolate AppState snapshot. Verified by First Reviewer, Final Controller & Final Approach Control (74a7c2b) |
| K099 | TeruTeruPandas Auth Database | Completed | Create pairing/token database schemas, implement HMAC-SHA256 hashing. Verified by First Reviewer, Final Controller & Final Approach Control (f8d0c86) |
| K100 | Pairing & LAN Auth Endpoints | Completed | Connect pairing routes, implement LAN Auto-Connect terminal prompt. Verified by First Reviewer, Final Controller & Final Approach Control (8900586) |
| K101 | Job Queue & Isolated Execution | Completed | Build the Single-Worker Job queue, spawn workspaces, wrap AppState. Verified by First Reviewer, Final Controller & Final Approach Control (6d33227) |
| K102 | Live Frame Delta API & Commands | Completed | Implement Delta Polling (seq tracking) and command processor. Verified by First Reviewer, Final Controller & Final Approach Control (a683bcb) |
| K103 | Android App Bootstrap & Auth UI | Proposed | Set up Compose project, Retrofit, EncryptedSharedPreferences, Auth UI |
| K104 | Android Dashboard & Detail Screen | Proposed | Build job list/creation forms, live 15fps polling logs terminal view |
| K105 | Android Approval & Tabbed Viewer | Proposed | Build approval dialog modal and tabbed view for Logs/Diffs |
| K106 | End-to-End Release Validation | Proposed | Connect app and server E2E, check release gates |



## Official Verification Commands
- Standard Build: `dotnet build -p:UseAppHost=false`
- Strict Build: `.\scripts\verify-release.ps1`
- Test: `dotnet test`
- CLI Smoke: `dotnet .\Claude4Net.Cli\bin\Debug\net10.0\Claude4Net.Cli.dll --smoke-exit`

## Detailed Progress

### D11: Project Lumen CLI Redesign

### K051a: TerminalText and LumenFrame Foundation
- [x] Implement display-width-aware `TerminalText`
- [x] Add immutable `LumenFrame`, `FooterState`, and `TerminalMetrics` models
- [x] Add `K051aTerminalTextTests`
- [x] Official Release Gate passed (401/401 pass)

### K051b: LumenFrameBuilder and State Model Evolution
- [x] Implement `LumenFrameBuilder` for viewport and fixed region layout
- [x] Implement viewport scrolling and truncation logic
- [x] Implement history cell rendering into display lines
- [x] Add `K051bLumenFrameBuilderTests`
- [x] Official Release Gate passed (417/417 pass)

### K051c: Lumen Terminal Renderer and Live Integration
- [x] Implement `LumenTerminalRenderer` with ANSI cursor control and buffered repaint
- [x] Implement fallback rendering for redirected output (IsRedirected)
- [x] Integrate `LumenTerminalRenderer` into `LumenRenderer` facade
- [x] Update `LumenCliApp` to support real-time input refresh in Lumen mode
- [x] Add `K051cLumenTerminalRendererTests` (16 tests covering repaint, cursor, fallback, and delegation)
- [x] Official Release Gate passed (433/433 pass)

### K052: Lumen v2 Search and Scroll Navigation
- [x] Extend `ViewportScrollState` with `AutoScroll` and `ScrollOffset`
- [x] Implement scroll navigation (PageUp/Down, Ctrl+Home/End)
- [x] Update `LumenFrameBuilder` to respect scroll offset for transcript
- [x] Ensure input and footer regions remain fixed during scrolling
- [x] Add `K052LumenScrollNavigationTests` (8 tests)
- [x] Official Release Gate passed (441/441 pass)

### K053a: Lumen Render Fidelity Hotfix
- [x] Prevent Spectre markup raw tag leakage in `MarkupCell`
- [x] Implement ANSI-aware display width, wrap, and truncate in `TerminalText`
- [x] Add Korean/CJK render preservation tests (mojibake prevention)
- [x] Fix footer first-character regression (repaint logic)
- [x] Official Release Gate passed (450/450 pass)

### K053b: Lumen Bottom Pane Anchoring Hotfix
- [x] input/footer bottom-region anchoring
- [x] footer first-character preservation
- [x] cursor clamping inside input region
- [x] empty input not added to transcript
- [x] Official Release Gate passed (456/456 pass)

### K054: Release Gate Stabilization
- [x] Implement non-discarding async ResetAndFlushForTestAsync in PandasUniverseManager via transaction queue serialization
- [x] Add float[] support (VectorColumn) with naMask preservation in DataFrame Concat logic to prevent type exceptions during merge
- [x] Update D02MemoryTests to implement IAsyncLifetime with async reset hook and verify initial clean state
- [x] Add direct regression test (VectorColumnConcat_ShouldWorkAndPreserveValues) to verify float[] concat and null preservation
- [x] Eliminate concurrency-driven data universe and temporary SQLite file pollution across test cases
- [x] Official Release Gate passed (461/461 pass)

### K055: Lumen Approval Dialog Integration
- [x] Integrate interactive approval dialog into the Lumen v2 terminal frame builder rendering pathway
- [x] Implement CJK and ANSI safety rules for formatting dialog text inside framed borders
- [x] Preserve viewport and fixed bottom region layout heights strictly (Lines.Count == Height)
- [x] Log final approval outcomes as single durable NoticeCells inside the transcript history
- [x] Add K055LumenApprovalFrameTests covering 8 comprehensive UI and behavior scenarios
- [x] Official Release Gate passed (472/472 pass)

### K056a: Worktree Hygiene Pre-clean
- [x] Inspect workspace and preserve unrelated dirty/untracked files (do not stage or delete):
  - `Documents/USER_MANUAL.md` (Deleted)
  - `안정화계획.md` (Modified)
  - `.gemini/agents/dotge-planner.md` (Untracked)
  - `.gemini/agents/lumen-fidelity-specialist.md` (Untracked)
- [x] Ensure correct stage state of only approved target files

### K056b: Release Gate And Documentation Sync
- [x] Sync SSOT progress logs in IMPLEMENTATION_PROGRESS.md and Implementation_Plan.md
- [x] Perform final release gate run verifying all 472 unit and integration tests pass successfully

### K057: Manual TUI Fidelity Pass
- [x] Verify compatibility paths (--smoke-exit, doctor, piped, legacy)
- [x] Validate terminal height invariant safety under extremely low heights (1~2 lines)
- [x] Apply absolute height safety padding to LumenFrameBuilder
- [x] Perform full release gate run verifying all 472 tests pass successfully

- [x] K038 Project Lumen Bootstrap Foundation (Option parsing & DI cleanup)
- [x] K039 AgentRunEvent Observer Foundation (Neutral run events)
- [x] K040 Lumen State and History Cells (UI model & History cells)
- [x] K041 Spectre Renderer v1 (Stable append rendering)
- [x] K042 Lumen Output Bridge (Bridge events to state with fail-safe)
- [x] Official Release Gate passed (316/316 pass)
- [x] K043 Prompt Composer Foundation (CLI input buffer & Key handling)
- [x] Official Release Gate passed (328/328 pass)
- [x] K044 LumenCliApp v1 (Interactive loop orchestration)
- [x] Official Release Gate passed (338/338 pass)
- [x] K045 Approval Dialog v1 (Integration with Tool approval flow)
- [x] Official Release Gate passed (359/359 pass)
- [x] K046 Command Output Normalization (Convert command results to rich history cells)
- [x] K047 Piped Input, Discord, and Legacy Compatibility (Verified decoupling and external channels)
- [x] K048 Render Quality and Cancellation Stabilization (Implemented ESC cancellation and Column Defense)
- [x] K049 Lumen Release Gate and Documentation (Completed UI redesign wave and updated guides)
- [x] Official Release Gate passed (378/378 pass)

### D01: Project Setup & Standard Build
- [x] Solution structure refinement
- [x] Standard build pipeline verification
- [x] Base security guidelines implementation

### D02: TeruTeruPandas Memory Sharing
- [x] Align schemas for `agent_memory` and `agent_trajectories`
- [x] Implement `PandasAgentMemoryUpsertTool`, `QueryTool`, `ClearTool`
- [x] Secure `PandasSnapshotTool` and `PandasRestoreTool`
- [x] Implement memory schema migration logic (Reinforced for operational safety)
- [x] Fix D02MemoryTests regression (Synchronize initialization & Ensure baseline after restore)
- [x] Improve RAG retrieval stability (Dimension consistency filtering & Keyword fallback)
- [x] Tighten memory clear scope (Session isolation safety)
- [x] Add K008MemoryOpsTests (Migration, Dimension Safety, Clear Scope)
- [x] Total 69/69 tests pass

### D03: Sandboxing & Permissions
- [x] Extract path safety logic into PathSafetyEvaluator
- [x] Implement PathSafetyResult enum
- [x] Refactor ToolOrchestrator to use PathSafetyEvaluator
- [x] Enforce manual approval for outside-access in YOLO mode
- [x] Detailed Audit Logging for sensitive tool calls (audit_logs table)
- [x] !audit command to view recent security events
- [x] Add PathSafetyTests and verify Unix path detection

### D04: Diagnostics & Source Guard
- [x] !doctor command implementation (Runtime, OS, Keys, DB, Integrity)
- [x] Comprehensive sensitive info masking in !env (Pattern + Key based)
- [x] Source Guard filtering pipeline (API Key, AWS, SSH, Bearer, etc.)
- [x] No-Phone-Home baseline (Outbound masking)
- [x] Added K010SecurityTests for audit and masking validation
- [x] Total 74/74 tests pass

### D05: SmartRouter
- [x] Implement SmartRouter with EMA latency tracking
- [x] Intent-based routing logic
- [x] Circuit breaker and fallback chain
- [x] Implement exponential backoff for Circuit Breaker recovery (Half-Open)
- [x] Implement accumulated cost tracking and cost-aware routing penalty
- [x] Add D05SmartRouterTests (Verified health recovery and cost-based decisions)
- [x] Total 71/71 tests pass

### D06: Resources-Oriented Skills
- [x] SkillResourceLoader with filesystem discovery and caching
- [x] SkillResourceManifest for Checklist, Playbook, Protocol, and Examples
- [x] SystemPromptBuilder integration for tool-specific instructions
- [x] Standard resource templates for `PandasDbTool` and `weather_search`
- [x] Robust path discovery for both runtime and test environments
- [x] Total 77/77 tests pass (including D06 resource integrity checks)

### D07: Discord Async Orchestration
- [x] DiscordListenerService and ChannelBroker
- [x] Button-based approval workflow (DiscordApprovalHandler)
- [x] Multitasking with handler-scoped state
- [x] Retry utilities for transient network issues
- [x] Job status query and permission checks

### D08: Coordinate (/coordinate)
- [x] Coordinate command for state synchronization
- [x] Evidence-backed phase gates
- [x] Merge readiness scoring logic
- [x] Blocker identification and reporting

### D09: Agent Trajectories & Self-Healing
- [x] Implement Self-Healing Service and ErrorClassifier
- [x] Regex-based error taxonomy (Quota, Network, Timeout, Logic, etc.)
- [x] Recommended Retry Policies library (Exponential Backoff, Fixed, etc.)
- [x] Automatic SELF_HEAL_GUIDE enhancement with retry policies
- [x] Trajectory Pruning logic and `!prune` command
- [x] Add D09SelfHealingTests with regression cases (Reinforced pruning verification)
- [x] Fix async test warnings (xUnit1031) in D07/D09
- [x] Total 66/66 tests pass

### D10: Final Stabilization & Performance
- [x] Official Release Gate (scripts/verify-release.ps1)
- [x] Strict build mode (warnings as errors)
- [x] L1/L2 Embedding Caching (GeminiEmbeddingProvider & AgentLoop)
- [x] Parallel tool execution in ToolOrchestrator
- [x] Performance Benchmarking & Stress Testing (Documents/PERFORMANCE.md)
- [x] Technical Debt: Regex optimization in keyword extraction
- [x] Total 76/76 tests pass


### K012: Final Stabilization and Handoff
- [x] Perform final security audit of all tool handlers (Verified PathSafety & Audit logging)
- [x] Create comprehensive User Manual (Documents/USER_MANUAL.md)
- [x] Verify operational documentation and READMEs
- [x] Perform final repository hygiene (Zero untracked/temp files)
- [x] Prepare handoff documentation (Documents/HANDOFF.md)
- [x] Final release gate verification (85/85 tests pass)
- [x] [Maintenance] K013-5: Preserve Gemini functionCall thoughtSignature metadata for Gemini 3 tool-use continuity (86/86 tests pass)
- [x] [Maintenance] 명시???로바이???택 가중치 조정 ?`gemini-cli` 초기 ?보 ?록 버그 ?정
- [x] [Maintenance] K013-2: 명시??모델 ?택 ?류 ?결 (Respect AppState.ActiveModel)
- [x] [Maintenance] K013-3: gemini-cli ?로바이???환 방어 ?모델 명칭 최신??(gemini-2.0-flash ??
- [x] [Maintenance] K013-4: Gemini function calling 400 ?러 ?정 (Continuation prompt ?략)

### K003: Release Gate Baseline
- [x] Create `scripts/verify-release.ps1` with strict error handling
- [x] Add `--smoke-exit` non-interactive CLI verification path in `Program.cs`
- [x] Verify DLL-direct CLI smoke test with shutdown message
- [x] Establish `Documents/RELEASE_GATE.md` checklist
- [x] Verified 66/66 tests pass under strict nullable rules

### K015: P0 Reliability Preflight and Permission Foundation
- [x] Extended `PermissionMode` with ReadOnly, WorkspaceWrite, Prompt, and DangerFullAccess while preserving legacy Default/Yolo/BypassPermissions compatibility
- [x] Added `PermissionEnforcer` for centralized path, command-risk, and sensitive-tool policy decisions
- [x] Added `CommandRiskClassifier` for dangerous terminal command detection
- [x] Added CLI `doctor --output-format json` and `--permission-mode` support
- [x] Added regression coverage for outside-workspace write blocking, symlink escape detection where supported, dangerous command classification, and doctor JSON output
- [x] P1 fix: deny `PermissionDecision.RequireApproval` when no approval handler is available
- [x] P1 fix: resolve parent-chain symlinks for nonexistent child paths before workspace classification
- [x] Worker verification: standard build, strict nullable build, and `dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false` passed with 93/93 tests
- [x] First reviewer verification approved
- [x] Final controller verification approved

### K016: P1 Session Store and Task Board
- [x] Define `.claude4net/sessions/{sessionId}/` layout (session.json, task-board.json, progress.jsonl, result.md)
- [x] Implement `AgentSessionStore` for file-based persistence
- [x] Integrate real-time progress logging in `AgentLoop.RunAsync` (Thinking, ToolCall, Result)
- [x] Implement task board synchronization with `CoordinatorStore`
- [x] Add system commands: `/status` (visual summary) and `/resume` (metadata loading)
- [x] Add `K016SessionTests` (Directory creation, Roundtrip, JSONL append, Result write, path traversal guards)
- [x] Official Release Gate passed (118/118 tests)

### K017: P2 Diff Approval Workflow
- [x] Define `FileDiffPreview` and `IRichApprovalHandler` in SDK
- [x] Implement `DiffService` for Unified Diff generation and binary detection
- [x] Refactor `FileWriteTool` and `FileEditTool` to implement `IPreviewableTool`
- [x] Integrate Diff-based approval flow in `ToolOrchestrator`
- [x] Implement rich diff visualization in `CliUserApprovalHandler`
- [x] Add `K017DiffTests` (Unified Diff, Preview generation, Approval/Deny logic)
- [x] Official Release Gate passed (123/123 tests)

### K018: P3 Skill Registry Foundation
- [x] Define `SkillRegistryRecord` and `SkillQualityMetrics` models in SDK
- [x] Implement `SkillRegistryService` for file-backed storage (.claude4net/skill-registry.json)
- [x] Support `.skill_id` sidecar identification without mutating `.agents/`
- [x] Integrate with `!skills` command and `!doctor` diagnostics
- [x] Implement path traversal protection for skill registration
- [x] Add `K018SkillRegistryTests` (Registration, Discovery, Metrics, Security)
- [x] Official Release Gate passed (138/138 tests)

### K021: Gemini thought-signature/tool-turn compatibility hardening
- [x] Refactor SSE parser to preserve all candidate content part properties (including `thought_signature`)
- [x] Fix function response turn sequence in `GeminiProvider.AddMessage`
- [x] Implement synthetic ID to original function name mapping for multi-turn tool calling
- [x] Add end-to-end integration tests using mocked HttpClient and SSE streams
- [x] Verify `thought_signature` preservation and synthetic ID resolution via provider history
- [x] Official Release Gate passed (138/138 tests)

### K022: Skill evolution proposal workflow
- [x] Define `SkillProposalRecord`, `SkillProposalStatus`, and `SkillProposalRoot` in SDK
- [x] Implement `SkillProposalService` for metadata-only proposal management
- [x] Store proposals in `.claude4net/skill-proposals.json` (repo-local safe area)
- [x] Implement path safety validation reusing `SkillRegistryService` logic
- [x] Integrate `SkillProposalService` into DI container in `Program.cs`
- [x] Add CLI commands: `!skill-proposals` (list) and `!skill-propose` (create)
- [x] Add `K022SkillProposalTests` (Creation, Persistence, Path Safety, No Mutation)
- [x] Official Release Gate passed (145/145 tests)

### K023: Context Window Management and Token Counting
- [x] Define `ITokenCounter` and `DefaultTokenCounter` in SDK for heuristic token estimation
- [x] Implement `ContextCompressor` with tool-call preservation logic (Anthropic & Gemini styles)
- [x] Integrate `ContextLimit` and `TokenCounter` properties into `ILLMProvider` interface
- [x] Implement `ContextLimit` in all providers (Claude: 200k, Gemini: 1M, Ollama: 8k)
- [x] Integrate automated compression in `AgentLoop.RunAsync` (Trigger at 80%, Target 60%)
- [x] Add `K023ContextCompressionTests` for token counting, preservation, and summarization
- [x] Official Release Gate passed (151/151 tests)

### K024: Event-Sourced State and Resumable Sessions
- [x] Define `AgentEvent` model and `IAgentEventStore` in SDK
- [x] Implement `FileAgentEventStore` for local append-only JSONL storage
- [x] Implement `AgentStateReconstructor` for building session state from event history
- [x] Implement `SnapshotPolicy` for periodic state checkpointing
- [x] Integrate event sourcing into `AgentLoop` for resumable execution
- [x] Add `K024EventSourcedStateTests` for append, reconstruction, and resume logic
- [x] Official Release Gate passed (156/156 tests)

### K025: Security Hardening and Symbolic Link Protection
- [x] Enhance `PathSafetyEvaluator` with `ResolveFinalPath` for symlink/reparse-point escape detection
- [x] Implement circular symlink detection and depth-limit (10) to prevent denial-of-service
- [x] Expand `SourceGuard` masking for environment variables and command-line secrets
- [x] Update `ToolOrchestrator` audit logs with detailed denial reasons
- [x] Fix regression in `K010SecurityTests` due to audit message format changes
- [x] Fix test isolation issues by adding `[Collection("AppState")]` to `K025SecurityHardeningTests`
- [x] Official Release Gate passed (162/162 tests)

### K026: Self-Healing Loop Hardening
- [x] Define failure pattern models (Infinite Loop, Hallucination, Security Rejection)
- [x] Implement trajectory-based pattern classifier in `SelfHealingService`
- [x] Implement deterministic healing directive generation and persistence
- [x] Integrate automated instruction injection and reflection depth limits (Max: 3)
- [x] Implement strategy switch trigger for repeated failures
- [x] Add `K026SelfHealingLoopTests` for classifier, depth, and injection validation

### K027: Multi-Agent Coordination MVP
- [x] Define `AgentRole`, `AgentProfile`, `ITaskBoard`, and `TaskAssignment` models in SDK
- [x] Update `CoordinateTask` with dependency and hierarchy support
- [x] Implement `PandasTaskBoard` for shared-memory task management
- [x] Implement `AgentCoordinator` for task dispatching and circular dependency detection
- [x] Implement `MultiAgentOrchestrator` for goal decomposition into subtasks
- [x] Support result handoff between tasks via context appending
- [x] Add `K027MultiAgentCoordinationTests` (Dependency, Matching, Deadlock, Decomposition, Handoff, E2E)
- [x] Official Release Gate passed (173/173 tests)

### K028: Monitoring Dashboard
- [x] Create `Claude4Net.Dashboard` (ASP.NET Core Server) and `Claude4Net.Dashboard.Client` (Blazor WASM)
- [x] Implement `AgentHub` for real-time agent event streaming via SignalR
- [x] Implement `ThoughtStream.razor` for visual thought/tool-call monitoring
- [x] Implement `Approvals.razor` for web-based manual approval queue
- [x] Integrate `DashboardServer` into CLI and Runtime
- [x] Add `K028DashboardTests` (Hub broadcast, Approval flow, Client-Server connectivity)
- [x] Add dashboard startup, route ambiguity, and port-conflict coverage
- [x] Fix dashboard history replay to use active workspace session events
- [x] Preserve concrete event payloads in `RecentEvents` replay JSON
- [x] Official Release Gate passed (180/180 tests)

### K029: Checkpoint and Rewind Foundation
- [x] Create checkpoint storage layer under `.claude4net/sessions/{sessionId}/checkpoints/`
- [x] Capture file state before write/edit operations
- [x] Capture tool-call metadata, diff preview, provider/model, and prompt summary
- [x] Add CLI commands for listing and restoring checkpoints
- [x] Add session handoff and evidence files
- [x] Add `K029CheckpointRewindTests` (Created before write/edit, Restore, No-Op, Path Traversal)
- [x] Official Release Gate passed (187/187 tests)

### K030: State Machine and Oscillation Detection
- [x] Define `AgentRunState` and `AgentRunStateModel` in SDK
- [x] Implement `OscillationDetector` with pattern recognition (Stagnation, Repetition, Alternation)
- [x] Fix: Add detection for repeated `ToolCalledEvent` with same `ToolName`
- [x] Integrate state machine transitions in `AgentLoop`
- [x] Capture per-attempt goals and success/failure records
- [x] Add `K030StateMachineTests` (Oscillation detection, State transition, Attempt tracking)
- [x] Official Release Gate passed (190/190 tests)

### K032: Verification Gate Hardening
- [x] Define `VerificationVerdict` enum (Pass, Fail, Partial) with default-fail policy
- [x] Define `VerificationCheck` record with evidence, notes, skipped tracking
- [x] Define `VerificationResult` record for structured verification output
- [x] Define `VerifierSessionRecord` for independent read-only verification sessions
- [x] Implement `VerificationOrchestrator` with default-fail check execution
- [x] Implement evidence file verification and command output-based pass/fail judgment
- [x] Implement explicit skipped check recording
- [x] Implement machine-readable JSON result storage (.claude4net/sessions/{id}/verification-result.json)
- [x] Implement CLI result formatting with verdict display
- [x] Add `EvaluateForVerifier` to `PermissionEnforcer` for read-only verification sessions
- [x] Add verification result save/load methods to `AgentSessionStore`
- [x] Add `/verify` command to `CommandRegistry`
- [x] Add `K032VerificationGateTests` (Default-fail, Pass/Fail/Partial, Skipped, JSON roundtrip, Evidence)
- [x] Add `K032VerifierPermissionTests` (ReadOnly, WriteBlocking, PathTraversal, Session independence)
- [x] Official Release Gate passed (219/219 tests)

### K031: Provider Descriptor and Router V2
- [x] Define `ProviderDescriptor` record with capabilities, auth, default models, cost, categories
- [x] Define `ProviderCapabilities` record (ToolCalling, Vision, ThoughtSignature, Streaming, Embeddings, Local)
- [x] Define `ProviderAuth` record and `ProviderDefaultModels` record
- [x] Define `RoutingCategory` enum (QuickFix, DeepCode, Planner, Verifier, VisualEngineering, Librarian, LocalPrivate, CheapUtility)
- [x] Implement `ProviderRegistry` with default descriptors for Claude, Gemini, Ollama, Gemini-CLI
- [x] Add capability check, category filtering, default model lookup, local provider detection
- [x] Integrate `ProviderRegistry` into `SmartRouter` (constructor injection, descriptor-based initialization)
- [x] Refactor `SmartRouter.DefaultModelFor()` to use registry descriptors with fallback
- [x] Refactor `SmartRouter.IsLocalProvider()` to use registry capability check
- [x] Add `K031ProviderDescriptorTests` (Load, Reject, Capability, Local, DefaultModel, Category, Custom)
- [x] Add `K031RoutingV2Tests` (DescriptorModel, ForcedProvider, Registry, CustomRegistry, LocalOnly, LocalPrivate)
- [x] Official Release Gate passed (233/233 tests)

### K034: Event Store v2 and CQRS Projections
- [x] Define `IEventProjection` interface (Apply, Reset, Name)
- [x] Define `SessionSummaryReadModel` and `ToolUsageReadModel` read models
- [x] Implement `SessionSummaryProjection` (prompt count, tool calls, errors, provider, model tracking)
- [x] Implement `ToolUsageProjection` (per-tool call count, success/error tracking with ToolUseId correlation)
- [x] Implement `EventProjectionEngine` (Replay, Rebuild, CatchUp, ApplyEvents, GetProjection)
- [x] Add `VerificationCompletedEvent` to AgentEvents (K032 integration)
- [x] Extend `FileAgentEventStore` with v2 query methods (GetEventCount, GetEventsByTimeRange, GetEventsByType)
- [x] Add StateTransition, TaskAttempt, VerificationCompleted deserialization to FileAgentEventStore
- [x] Add `K034EventStoreV2Tests` (13 tests: Projection, Summary, ToolUsage, Replay, Filter, Roundtrip)
- [x] Official Release Gate passed (246/246 tests)

### K033: Skill and Hook Operations
- [x] Define `HookTiming` enum (BeforeToolExecution, AfterToolExecution, OnToolError)
- [x] Define `HookContext` (ToolName, Arguments, Result, IsError, ElapsedMs, SessionId, Metadata)
- [x] Define `HookResult` with factory methods (Ok, Fail, Abort)
- [x] Define `IToolHook` interface (Name, Timing, Priority, IsEnabled, ExecuteAsync)
- [x] Implement `HookPipeline` (Register, Execute by timing, priority-ordered chaining)
- [x] Implement Before hook abort support (ShouldAbort stops pipeline)
- [x] Implement fail-safe exception handling (individual hook failure doesn't crash pipeline)
- [x] Implement dynamic hook enable/disable (EnableHook, DisableHook, FindHook)
- [x] Add `K033SkillHookTests` (12 tests: Register, Abort, Allow, Metrics, Error, Chain, FailSafe, Disable, Find, Mixed, Factory, Metadata)
- [x] Official Release Gate passed (258/258 tests)

### K035: Agentic Search, Memory Strategy, and Audit Traceability
- [x] Define `MemoryStrategyType` enum (FullHistory, SlidingWindow, SummaryBased, Hybrid)
- [x] Define `MemoryConfig`, `ConversationMessage`, `MemoryWindow`, `IMemoryStrategy` records
- [x] Implement `FullHistoryStrategy` (retain all messages)
- [x] Implement `SlidingWindowStrategy` (system/pinned message preservation, recent N window)
- [x] Implement `SummaryBasedStrategy` (old messages replaced with summary)
- [x] Implement `MemoryStrategyManager` with default strategy registration and config updates
- [x] Define `AuditCategory` enum (8 categories), `AuditSeverity` enum (3 levels), `AuditEntry` record
- [x] Implement `AuditTrailService` with category/severity/session/time filtering and circular buffer
- [x] Add `K035MemoryAndAuditTests` (16 tests: Memory strategies, Audit filters, Buffer, Metadata)
- [x] Official Release Gate passed (274/274 tests)
- [x] P1 Integration Stabilization regression coverage added and release gate verified (279/279 tests)

### K036: Ollama ToolResult Grounding and Context Window Hotfix
- [x] Preserve structured tool result payloads in `AgentLoop` (removed `.ToString()` collapse)
- [x] Implement `GetRawText()` handling for non-string tool results in `OllamaProvider.AddMessage`
- [x] Explicitly set `num_ctx` in Ollama API requests (StreamQueryAsync)
- [x] raise Ollama context limit to 256k (262144) in Provider and Registry
- [x] Add `K036OllamaTests` and `K036ToolResultTests` (Context, Payload preservation, num_ctx verification)
- [x] Official Release Gate passed (287/287 tests)

### K037: Gemini Structured Tool Result Compatibility Hotfix
- [x] Fix `GeminiProvider.AddMessage` to handle non-string tool result content using `GetRawText()`
- [x] Preserve structured data (objects/arrays) in Gemini `functionResponse` payloads
- [x] Implement safe fallback in `AddMessage` to avoid storing illegal raw Anthropic messages (prevents API rejection)
- [x] Add `K037GeminiTests` (Structured payload preservation, Safe fallback verification)
- [x] Official Release Gate passed (290/290 tests)

### K038: Project Lumen Bootstrap Foundation
- [x] Extract bootstrap and option parsing into `Claude4Net.Cli/Bootstrap/CliOptions.cs`
- [x] Extract service registration into `Claude4Net.Cli/Bootstrap/CliServiceRegistration.cs`
- [x] Refactor `Program.cs` to use `CliOptions` and `CliServiceRegistration`
- [x] Reserve `--legacy-cli` flag (fallback for future UI migration)
- [x] Preserve existing interactive/piped CLI behavior, dashboard, doctor, and smoke-exit
- [x] Add `K038LumenBootstrapTests` (Option parsing, Service registration)
- [x] Official Release Gate passed (294/294 tests)

### K039: AgentRunEvent Observer Foundation
- [x] Define `IAgentRunEvent` marker interface and structured event records in SDK
- [x] Define `IAgentRunObserver` and `NullAgentRunObserver` in SDK
- [x] Integrate optional `IAgentRunObserver` injection into `AgentLoop`
- [x] Implement `ReportAsync` helper in `AgentLoop` for safe event reporting
- [x] Report key events: `RunStarted`, `RoutingSelected`, `ThinkingStarted`, `ThinkingDelta`, `TextDelta`, `ToolCallQueued`, `ToolResultReceived`, `AssistantMessageCompleted`, `RunError`, `RunCompleted`
- [x] Add `K039AgentRunObserverTests` for event sequence and content verification
- [x] Official Release Gate passed (300/300 tests)

### K040: Lumen State and History Cells
- [x] Define `LumenState` and `LumenReducer` for UI state management
- [x] Implement base `HistoryCell` and specialized cells (User, Assistant, Thinking, ToolCall, ToolResult, Notice, Error, Approval)
- [x] Support plain-text and Spectre-renderable outputs for all cells
- [x] Implement UI events for state transitions (RunStarted, UserPrompt, AssistantText, Thinking, etc.)
- [x] Add `K040LumenStateTests` for state transition and cell logic verification
- [x] Official Release Gate passed (308/308 tests)

### K041: Spectre Renderer v1
- [x] Implement `LumenRenderer` using `Spectre.Console` for stable append-oriented rendering
- [x] Implement `ChatSurface` for transcript rendering
- [x] Implement `BottomPane` and `FooterRenderer` for input area and status bar
- [x] Implement `DialogLayer` for modal-style overlays (Approvals)
- [x] Support automatic width adaptation and markup escaping
- [x] Add `K041LumenRendererTests` using `Spectre.Console.Testing`
- [x] Official Release Gate passed (310/310 tests)

### K042: Lumen Output Bridge
- [x] Implement `LumenRunObserver` to bridge `IAgentRunEvent` to `LumenState`
- [x] Support real-time streaming of text and thinking deltas
- [x] Implement tool-call and result preservation in history cells
- [x] Add fail-safe error reporting via notice cells
- [x] Add `K042OutputBridgeTests` for state-observer synchronization
- [x] Official Release Gate passed (316/316 pass)

### K043: Prompt Composer Foundation
- [x] Implement `PromptBuffer` for text manipulation and cursor navigation
- [x] Implement `PromptHistory` for up/down command history navigation
- [x] Implement `CommandSuggester` for tab-based command auto-completion from `CommandRegistry`
- [x] Implement `KeyBindingRegistry` for mapping keys (Ctrl+L, Ctrl+C, Esc, etc.) to actions
- [x] Implement `PromptComposer` as the main CLI input orchestrator
- [x] Implement `PromptComposerState` and `PromptComposerResult` models
- [x] Add `K043PromptComposerTests` covering 13 scenarios (Insert, Backspace, Delete, Nav, History, Suggestion, etc.)
- [x] Official Release Gate passed (328/328 pass)

### K044: LumenCliApp v1
- [x] Implement `LumenCliApp` with interactive loop orchestration
- [x] Support background agent run task with cancellation
- [x] Integrate neutral `IAgentRunObserver` via constructor injection
- [x] Expose internal seams for testability without reflection
- [x] Verify DiscordListenerService is started from the shared interactive code path before Lumen/Legacy branching
- [x] Official Release Gate passed (338/338 pass)

### K045: Approval Dialog v1
- [x] Implement `ApprovalDialogState`, `ApprovalDialogAction`, and `ApprovalQueue` (Task synchronization)
- [x] Implement `LumenApprovalHandler` (IRichApprovalHandler) for tool approval integration
- [x] Implement `DialogLayer` with Spectre-based rendering and markup escaping
- [x] Fix LumenCliApp state ownership using `_observer.State` as single source of truth
- [x] Ensure approval dialog has key handling priority and unknown keys are NoOps
- [x] Preserve `LastAction` state after dialog closure for decision traceability
- [x] Implement full roundtrip approval flow from background AgentLoop to UI loop
- [x] Official Release Gate passed (359/359 pass)

### K046: Command Output Normalization
- [x] Convert command results to rich history cells

### K047: Piped Input, Discord, and Legacy Compatibility
- [x] Verified decoupling and external channels

### K048: Render Quality and Cancellation Stabilization
- [x] Implemented ESC cancellation and Column Defense

### K049: Lumen Release Gate and Documentation
- [x] Completed UI redesign wave and updated guides
- [x] Official Release Gate passed (378/378 pass)

### K058: Lumen UX Polish
- [x] Refactor ToolCall and ToolResult cells (Decouple unified state and make individual collapsible entities)
- [x] Support toggle keybind `T` to fold/unfold Thought, ToolCall, and ToolResult blocks
- [x] Update FooterRenderer width calculation to safely avoid negative index or arithmetic overflow
- [x] Resolve Spectre.Console markup tag parser error (change [+] and [-] to + and -)
- [x] Integrate interactive `/help` commands and slash documentation
- [x] Extend existing K040 Lumen state reducer tests for K058 clear/theme/model/collapsible behavior
- [x] Official Release Gate passed (476/476 pass)

### K059-K066: Final-Control P1 Remediation
- [x] P1-1: Dashboard ControlPlaneHub Security Fix
- [x] P1-2: RoutineSchedulerService Fix
- [x] P1-3: xUnit Parallelization Consolidation
- [x] P1-4: Whitespace Cleanup
- [x] P1-5: SSOT Doc Sync
- [x] Official Release Gate passed (502/502 pass)
- [x] [K055, K064 Test Fix] K055 K064 P1 test fixes applied and all 502/502 tests passing.

### K067: State Hygiene Completion
- [x] Complete workspace/session-scoped memory as the default runtime path.
- [x] Remove active reliance on app-base `db/memory.db`.
- [x] Refactor `PandasUniverseManager` to act as a compatibility-only delegating facade.
- [x] Implement read-only fallback to legacy database if active database not found.
- [x] Redirect snapshots and restores to the scoped workspace state directory instead of system base directory.
- [x] Maintain isolation for parallel test runs using separate workspace contexts.
- [x] Ensure deterministic baseline table creation.
- [x] Official Release Gate passed (502/502 pass).

### K068: Memory Checkpoint Integration
- [x] Add `StateSnapshotId` and `IncludesMemoryState` properties to `CheckpointManifest` (`Claude4Net.SDK/CheckpointModels.cs`).
- [x] Detect memory-modifying tools such as `pandas_agent_memory_upsert`, `pandas_agent_memory_clear`, `pandas_restore`, and `pandas_import` in `ToolOrchestrator.cs`.
- [x] Automatically trigger memory state snapshot creation immediately prior to the execution of memory-modifying tools.
- [x] Persist memory state snapshot ID and toggle in the checkpoint manifest file.
- [x] Restore memory snapshot synchronously alongside standard file recovery during checkpoint rollback.
- [x] Ensure seamless backward compatibility with legacy checkpoints that only contain file records.
- [x] Implement robust error handling throwing descriptive exceptions when memory snapshots are missing or corrupted.
- [x] Resolve SQLite file locking issues during test setup by utilizing connection pool clearings.
- [x] Add `K064MemoryCheckpointTests` to verify happy path and descriptive error paths.
- [x] Official Release Gate passed (504/504 pass).

### K069: SeedSpec Command Surface
- [x] Register `/spec` slash command group in `Claude4Net.Commands/CommandRegistry.cs`
- [x] Implement `/spec list` to display specifications cleanly inside a Spectre Table
- [x] Implement `/spec new <id> <title>` to create new draft specifications in the workspace
- [x] Implement `/spec show <id>` to display goal, status, acceptance criteria, and questions
- [x] Implement `/spec question <id> <question>` to append auto-incremented blocking questions (Q-n)
- [x] Implement `/spec answer <id> <questionId> <answer>` to add answers to clarifying questions
- [x] Implement `/spec criteria add <id> <description>` to append required acceptance criteria (AC-n)
- [x] Implement `/spec lock <id>` with validation verifying at least one criterion exists and no blocking questions are unanswered
- [x] Implement `/spec attach <specId> <coordinateTaskId>` to synchronize acceptance criteria to task gates via `CoordinatorStore`
- [x] Implement path traversal defense for all `specId` inputs to prevent file system traversal
- [x] Add 8 unit test scenarios in `Claude4Net.Tests/K069SeedSpecCommandTests.cs`
- [x] Official Release Gate passed (512/512 pass).

### K070: Coordinate Spec Enforcement
- [x] Integrate `--spec <specId>` argument support in `/coordinate start <id> <title> [--spec <specId>]` command.
- [x] Lock task creation by rejecting unknown, non-existent, or invalid Spec IDs.
- [x] Reject unlocked draft specifications from initiating coordinated tasks in `Execution` phase.
- [x] Automatically synchronize and convert required acceptance criteria to evidence-required gates (`Spec-AC-n`).
- [x] Automatically synchronize and convert optional/clarification criteria to non-blocking gates.
- [x] Enforce blocking policy: reject transition to `Execution` phase if the attached specification contains any unanswered clarifying questions.
- [x] Enforce phase gate transitions ensuring task remains in Planning phase until specification criteria and questions are fully resolved.
- [x] Add comprehensive unit tests verifying spec enforcement, invalid ID paths, unlocked spec rejection, and blocking question verification.
- [x] Official Release Gate passed (517/517 pass).

### K071: Provider Descriptor V2 Model
- [x] Add `Endpoint`, `Headers`, and `Metadata` fields to `ProviderDescriptor` record (`Claude4Net.SDK/ProviderModels.cs`).
- [x] Implement robust validation of required fields and absolute HTTP/HTTPS URI format for `Endpoint` where provided.
- [x] Return descriptive error messages including target file path or provider ID when loading invalid descriptors.
- [x] Parse routing categories case-insensitively and fail-closed on unknown categories using custom JSON converter (`RoutingCategoryJsonConverter`).
- [x] Enforce fail-closed policy on deserialization or schema errors, bubble up errors, and prevent loading of malformed descriptor files.
- [x] Provide backward compatibility by adding default Ollama local endpoint configuration in defaults setup and ensuring built-in providers (claude, gemini, gemini-cli, ollama) load successfully.
- [x] Adjust existing K056 tests to match fail-closed expectations (assert throwing exceptions on invalid json).
- [x] Add K071ProviderDescriptorV2Tests covering 8 test methods / 10 cases
- [x] Official Release Gate passed (527/527 pass).

### K072: Provider Settings Precedence
- [x] Extend `ProviderRegistry` to load descriptors in precedence order: Built-in defaults < System < User < Workspace.
- [x] Implement config and setting merge precedence in `SettingsManager` (Config < Environment variables < CLI overrides).
- [x] Fix JSON merging default property overwrite bug using JsonDocument parsing.
- [x] Integrate precedence resolution logic at the application entry point (`Program.cs`).
- [x] Create comprehensive `K072ProviderPrecedenceTests` verifying all load levels and variable resolution precedence.
- [x] Official Release Gate passed (530/530 pass).

### K073: Provider Factory Preparation
- [x] Define `IProviderFactory` and specialized implementations for Anthropic, Gemini, Ollama, Gemini CLI, and OpenAI Compatible providers.
- [x] Register all provider factories in Dependency Injection (`CliServiceRegistration.cs`).
- [x] Update `ProviderRegistry` to resolve provider instances via registered provider factories with a legacy fallback mechanism.
- [x] Integrate factory resolution into `AgentLoop.cs` and `Program.cs` execution paths.
- [x] Add comprehensive suite of unit and integration tests (`K073ProviderFactoryTests.cs`) covering endpoint parsing, authorization validations, registry resolution, and fallbacks.
- [x] Official Release Gate passed (544/544 pass).

### K074: Routine Command MVP
- [x] Register `/routine` slash command group and operations (`list`, `show <id>`, `add <id> <name>`, `enable <id>`, `disable <id>`, `delete <id>`, `run <id>`).
- [x] New routines default to disabled unless explicitly enabled.
- [x] Validate IDs to be path-safe (no directory traversal or illegal characters).
- [x] `/routine show` displays trigger, actions, permission mode, workspace, last run, and enabled state.
- [x] Delete removes definition only, not historical run records.
- [x] Add `K074RoutineCommandTests`
- [x] Official Release Gate passed (553/553 pass)

### K075: Routine Execution Integration
- [x] Validate routine definition, workspace paths, and permission mode dynamically before running.
- [x] Implement pre-run checkpoint trigger for any routine action modifying files or memory.
- [x] Wire up HookPipeline execution (Before / After tool hooks).
- [x] Append RoutineRunRecord and Event Store events upon execution.
- [x] Implement and test read-only mode workspace protections for routine runner.
- [x] Add `K075RoutineExecutionIntegrationTests`
- [x] Official Release Gate passed (559/559 pass)

### K076: Routine Scheduler Hardening
- [x] Implement manual, interval, and daily routine triggers using RoutineSchedulerService.
- [x] Calculate next-run timestamps based on configured triggers (DailyTime, Interval).
- [x] Enforce safety limits: maximum 1 concurrent execution per routine and minimum interval floors (e.g. 5 seconds).
- [x] Add execution run timeout constraints.
- [x] Persist routine scheduler execution state (last-run and next-run metadata).
- [x] Reject webhook/event triggers with warnings or exceptions.
- [x] Add `K076RoutineSchedulerTests`
- [x] Official Release Gate passed (567/567 pass)

### K077: Skill Proposal Lifecycle
- [x] Extend SkillProposalStatus enum with Failed and Verified states.
- [x] Implement state machine rules in SkillProposalService to restrict state transitions.
- [x] Add Status Mutation helpers (Approve, Reject, Apply, Verify, Fail, Supersede) throwing InvalidOperationException on failure.
- [x] Register /skill slash commands (analyze, proposals, propose, validate, approve, reject, apply) with backward compatible aliases.
- [x] Add K077SkillProposalLifecycleTests verifying state machine transitions and command handler outcomes.
- [x] Official Release Gate passed (570/570 pass)

### K078: Skill Apply Engine
- [x] Implement the 9-step Skill Apply Engine pipeline (Status check, Path validation, Patch preview, Pre-apply checkpoint, User approval, Apply changes, Evidence record, Post-apply verification, Mutation & Rollback).
- [x] Integrate `/skill apply <proposalId>` command.
- [x] Fix test isolation by adding `[Collection("AppState")]` to `K050LumenTranscriptHygieneTests`.
- [x] Add `K078SkillApplyEngineTests` verifying all pipeline branches and rollback safety.
- [x] Official Release Gate passed (574/574 pass)

### K079: Skill Trajectory Mining
- [x] Record skill usage success/failure and score.
- [x] Mine `agent_trajectories`, event store, and verification results.
- [x] Detect repeated failure classes by skill/tool/path/error.
- [x] Generate proposal candidates with metadata linking evidence.
- [x] Deduplicate similar proposal candidates.
- [x] Do not auto-approve or auto-apply generated proposals.
- [x] Add `K079SkillTrajectoryMiningIntegrationTests`
- [x] Official Release Gate passed (575/575 pass)

### K080: Dashboard Read Models
- [x] Fix nullable warnings in `ControlPlaneHub.cs` (e.g. `manifest.Provider ?? string.Empty`, `manifest.Model ?? string.Empty`, `prop.SkillId ?? string.Empty`, `prop.TargetPath ?? string.Empty`).
- [x] Implement the 7 typed read APIs for providers, coordinate tasks, checkpoints, verification, skills, routines, and state.
- [x] Read state from event store, projections, registry services, and store services.
- [x] Ensure only serializable DTOs are returned and no arbitrary commands are executed.
- [x] Add path-traversal validation for session/workspace inputs.
- [x] Implement `K080DashboardReadModelTests.cs` under Claude4Net.Tests project, covering 10 test scenarios.
- [x] Official Release Gate passed (585/585 pass)

### K081: Dashboard Typed Commands
- [x] Keep `ExecuteCommand` interface denied and enforce strict command sanitization and evaluation.
- [x] Implement safe control actions: `RunRoutine`, `RestoreCheckpoint`, `ApproveSkillProposal`, `RejectSkillProposal`, `ApplySkillProposal`, `RunVerification`.
- [x] Integrate permission level checking in all writable control actions.
- [x] Append detailed audit trail and event store logs for every control operation.
- [x] Add comprehensive tests (`K081DashboardTypedCommandTests`) verifying authorization, validation, and rejection paths.
- [x] Official Release Gate passed (593/593 pass).

### K082: Dashboard UI Completion
- [x] Replace UI placeholders in Blazor WASM with production-ready operational views.
- [x] Implement UI pages: Providers routing category, Skills lifecycle control panel, Routines scheduler triggers and execution, Checkpoint history comparison, Verification session logging.
- [x] Bind Blazor UI buttons directly to safe typed Hub methods rather than arbitrary CLI commands.
- [x] Implement client-side permission-aware button enabling/disabling states.
- [x] Add Blazor rendering tests and verification path integration coverage.
- [x] Official Release Gate passed (595/595 pass).

### K083: Release Gate Expansion
- [x] Update `scripts/verify-release.ps1` to include focused integration smoke test groups.
- [x] Add steps: "State Isolation Smoke", "Spec Gate Smoke", "Provider Descriptor Smoke", "Routine Permission Smoke", and "Dashboard Control Plane Smoke".
- [x] Ensure that external system environment variable overrides are mock-isolated at the script level.
- [x] Maintain full test coverage baseline of 595 tests along with the individual smoke filter checks.
- [x] Verify CLI direct dll execution and exit checks in non-interactive mode.
- [x] Official Release Gate passed (595/595 pass).

### K084: Final Integration and Documentation
- [x] Execute `verify-release.ps1` to verify clean build, nullable warnings compliance, and 595 tests pass.
- [x] Update roadmap tables and status trackers in `IMPLEMENTATION_PROGRESS.md`, `Documents/Implementation_Plan.md`, and `ralph-queue-state.md` to Completed.
- [x] Synchronize milestone queue and verify directory status.
- [x] Confirm no modifications have occurred under `.agents/` or other forbidden paths.
- [x] Official Release Gate passed (595/595 unit tests + 101 smoke tests pass).

### K085: Slash Command Palette
- [x] `PromptComposer`에 `/` 입력 감지 시 팔레트 모드 전환 로직 추가
- [x] `LumenState`에 `IsCommandPaletteVisible`, `PaletteFilterText`, `PaletteSelectedIndex` 상태 추가
- [x] 모달 입력 상태 기계 (ArrowUp/Down 리다이렉트) 구현
- [x] `LumenFrameBuilder`에서 명령어 팔레트 오버레이 패널 렌더링
- [x] Enter 자동완성, Escape 닫기, 최대 5행 제한 및 스크롤 래핑
- [x] `K085SlashCommandPaletteTests` 추가 (Sequential execution guaranteed with `[Collection("AppState")]` collection integration)
- [x] Official Release Gate passed (601/601 unit tests + 101 smoke tests pass)

### K086: CLI Startup Arguments Expansion
- [x] `CliOptions.cs`에 `--yolo` 플래그 파싱 추가
- [x] `CliOptions.cs`에 `--setworkspace <경로>` 옵션 파싱 추가
- [x] `Program.cs`에서 `--setworkspace` 경로 유효성 검증 및 `AppState.CurrentCwd` 설정
- [x] `PermissionEnforcer.Evaluate()`에서 YOLO 모드 내부 Allow / 외부 RequireApproval 분기
- [x] `K086CliStartupArgsTests` 추가 (YOLO 권한 분기 검증)
- [x] `K086WorkspaceArgTests` 추가 (`--setworkspace` 경로 검증)
- [x] Official Release Gate passed (609/609 unit tests + 101 smoke tests pass)

### K087: Skill Store Scope Separation
- [x] 글로벌 및 로컬 스킬 저장소 구조 분리 구현 (Global: App execution path `skills/`, Local: workspace `.claude4net/skills/`)
- [x] `SkillRegistryService` 저장소 경로 및 discovery 동작 확장
- [x] `SkillApplyEngine`의 대상 경로/저장소 처리 보강 (글로벌과 로컬 타겟 경로 안전 처리 및 CheckpointStore 우회 추가)
- [x] 관련 `K018SkillRegistryTests` 및 `K078SkillApplyEngineTests` 테스트 보강
- [x] Official Release Gate passed (613/613 unit tests + 101 smoke tests pass)

### K088: TeruTeruPandas net10.0 동기화 & 저장소 위생 정리
- [x] TeruTeruPandas 및 하위 프로젝트 net10.0 Target Framework 동기화 설정
- [x] 빌드/클린 단계에서 불필요한 임시 DB 파일(.db, .sqlite) 제거 규칙 정의
- [x] 다중 테스트 병렬 실행 시 SQLite 커넥션 락 방지 로직 개선
- [x] 빌드 경고 제거 및 의존성 위생 정돈

### K089: /usage 실사용량·비용·성능 관측 대시보드 구현
- [x] /usage 슬래시 명령어 등록 및 CLI 포맷팅 출력 (Spectre Table)
- [x] API Token 사용량 및 Latency(EMA) 집계용 Read Model/Projection 구현
- [x] Dashboard 내 실시간 사용량 요약 페이지(Usage.razor) 및 차트 컴포넌트 추가
- [x] 누적 비용 계산 및 모델별 단가 정보 바인딩

### K090: LSP/MCP 실전 연결 완성 및 Mock Coverage 강화
- [x] Model Context Protocol(MCP) 클라이언트 구현 및 외부 도구 디스커버리 연동
- [x] 테스트 환경용 MCP/LSP Mock 서버 패키지 구축 및 주입
- [x] Dynamic Tool Registry를 통한 MCP 도구 로드 및 ToolOrchestrator 위임 검증
- [x] Mock Coverage를 통한 외부 통신 없는 통합 테스트 시나리오 확보


### K091: 승인 대기열 동시성 하드닝 & Idempotent Approval Engine
- [x] Multi-channel(CLI, Web UI, Discord) 동시 승인 요청 처리용 동시성 제어 락 도입
- [x] 승인 요청에 대한 멱등성(Idempotency) 검증 엔진 구현 (동일 요청 중복 응답 방어)
- [x] Conflicting Approval Decisions 발생 시 예외 처리 및 사용자 안전 피드백
- [x] 승인 대기열 타임아웃 및 데드락 방지 검증 테스트 케이스 작성

### K092: Dashboard Multi-Session Observatory & Replay View
- [x] 다중 세션 목록 리트리브 API 및 Dashboard Sessions 페이지 구현
- [x] 특정 세션의 Event Log(JSONL) 파싱 및 타임트래블 Replay Slider 컨트롤 개발
- [x] Replay View 내 Dynamic State Reconstruction 데이터 브라우징 기능 추가
- [x] 실시간 세션 스위칭 시 UI 및 SignalR 연결 해제/재연결 안정성 검증

### K093: Self-Healing v2: 실패 분류 확장과 복구 전략 추천 엔진
- [x] ErrorClassifier 내 Schema Mismatch, Rate Limit, Context Over 등 신규 에러 분류 추가
- [x] 복구 전략 추천 엔진(Recovery Strategy Recommender) 및 전략 처방 DTO 설계
- [x] AgentLoop 실행 단계에서 추천 전략 동적 수용 및 복구 시도 연계
- [x] 복구 성공률 지표 로깅 및 Trajectory 기록 추가

### K094: SkillUsageRecorder 실연결 & Self-Evolving Skills 루프 완성
- [x] ToolOrchestrator 실행 경로에 SkillUsageRecorder 데코레이터/인터셉터 연결
- [x] 기술 실행 결과 메타데이터 수집 및 `.claude4net/skill-usage.jsonl` 영속화
- [x] 실패 빈도가 높은 기술 감지 시 자동으로 SkillProposal 생성 유도 로직 개발
- [x] 자가 진화 루프(자가 학습-검증-제안) 연동 E2E 통합 테스트 검증

### K095: Security Policy Profiles & Red-Team Regression Harness
- [x] Strict/Permissive/Development 보안 정책 프로파일 설정 스키마 및 파일 바인딩
- [x] 디렉토리 탐색(Traversal), 임의 명령어 실행 방어 등 Red-Team 공격 시나리오 자동 검증 하네스 개발
- [x] PermissionEnforcer 내 Dynamic Policy Profile 매핑 및 정책 전환 로직 적용
- [x] Red-Team 하네스를 통한 Regression 방어 테스트 커버리지 구축
- [x] Official Release Gate passed (Passed all unit, integration, and security regression harness tests)

### K096: Plan/Dry-Run 모드: 실행 전 영향 범위 분석과 변경 예측
- [x] CLI 시작 인수 및 슬래시 명령어에 Plan/Dry-Run 모드 플래그(--dry-run, /plan) 추가
- [x] 가상 파일 시스템 변경 추적 및 상태 변경 임시 기록용 DryRunEngine 구현
- [x] 실제 쓰기 동작 차단 및 예측 변경 이력(Impact Report) 생성
- [x] 터미널 출력용 포맷팅 패널 구현 및 테스트 코드 작성 (646/646 pass)

### K097: Routine Scheduler v2 & Release Automation Control Tower
- [x] Routine Scheduler 내 5필드 표준 CRON 표현식 해석기 도입 및 스케줄 등록
- [x] 중앙 관제탑(Control Tower) 대시보드 페이지 구현 및 루틴 이력 모니터링
- [x] verify-release.ps1 실행 루틴 스케줄 연동 및 자동화 릴리스 빌드 상태 리포팅
- [x] 루틴 동시 실행 락 및 스레드 풀 안정성 보강 테스트 케이스 추가

### K098: API Startup & Infrastructure
- [x] Parse `--api [true/false]`, `--api-host`, `--api-port` startup options in CLI
- [x] Implement `AppStateSnapshot` context backup/restore
- [x] Conditionally bind WebHost to specified API address / interface


### K099: TeruTeruPandas Auth Database
- [x] Create `android_pairing_requests` schema
- [x] Create `android_auth_tokens` schema
- [x] Implement secure HMAC-SHA256 hashing for codes/tokens


### K100: Pairing & LAN Auth Endpoints
- [x] Implement Pairing code generation (10-digit PIN)
- [x] Implement Bearer token verification middleware
- [x] Implement LAN auto-connect 10-second prompt approval logic


### K101: Job Queue & Isolated Execution
- [x] Implement FIFO sequential Job Queue
- [x] Implement workspaces isolation worktree setup
- [x] Capture AppState context snapshot before execution and restore on exit
- [x] Trigger automatic code compilation, tests, and verify-release.ps1


### K102: Live Frame Delta API & Commands
- [x] Implement delta frame polling endpoint
- [x] Implement command dispatching API (idempotency, cancellation, push approval)


### K103: Android App Bootstrap & Auth UI
- [ ] Create Kotlin Compose application
- [ ] Implement Retrofit client and encrypted preferences
- [ ] Create Pairing PIN input screen and LAN connection trigger

### K104: Android Dashboard & Detail Screen
- [ ] Implement Job List and Creation forms
- [ ] Implement 15fps polling live ViewModel terminal view

### K105: Android Approval & Tabbed Viewer
- [ ] Implement overlay confirmation AlertDialog for pending approvals
- [ ] Implement tabbed layout for logs, diffs, and verification metrics

### K106: End-to-End Release Validation
- [ ] Verify full Android-server pairing, polling, and workspace checkout flows
- [ ] Clean up local testing worktrees and run standard verify-release.ps1

