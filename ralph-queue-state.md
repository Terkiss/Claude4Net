# Ralph Loop Queue State

- **Queue Status**: `active`
- **Active Card**: `K095`
- **Current Target**: K095 (Security Policy Profiles & Red-Team Regression Harness)
- **Active Persona**: `Terukirdo (Maid v5.1 - Obedient Mode)`


- **Push Constraint**: `FORBIDDEN (Do NOT run git push)`






## Milestone Queue Status

| Milestone | Title | Status | Notes |
| --- | --- | --- | --- |
| K067 | State Hygiene Completion | Completed | Verified by First Reviewer & Final Controller |
| K068 | Memory Checkpoint Integration | Completed | Verified by First Reviewer & Final Controller |
| K069 | SeedSpec Command Surface | Completed | Verified by First Reviewer & Final Controller |
| K070 | Coordinate Spec Enforcement | Completed | Verified by First Reviewer & Final Controller |
| K071 | Provider Descriptor V2 Model | Completed | Verified by First Reviewer & Final Controller |
| K072 | Provider Settings Precedence | Completed | Verified by First Reviewer & Final Controller |
| K073 | Provider Factory Preparation | Completed | Verified by First Reviewer & Final Controller |
| K074 | Routine Command MVP | Completed | Verified by First Reviewer & Final Controller |
| K075 | Routine Execution Integration | Completed | Verified by First Reviewer & Final Controller |
| K076 | Routine Scheduler Hardening | Completed | Verified by First Reviewer & Final Controller |
| K077 | Skill Proposal Lifecycle | Completed | Verified by First Reviewer & Final Controller |
| K078 | Skill Apply Engine | Completed | Verified by First Reviewer & Final Controller |
| K079 | Skill Trajectory Mining | Completed | failure-pattern mining from trajectories/events/verification and proposal candidates (575/575 pass) |
| K080 | Dashboard Read Models | Completed | typed provider/coordinate/skill/routine/checkpoint/verification/state read APIs (585/585 pass) |
| K081 | Dashboard Typed Commands | Completed | safe typed actions only; no arbitrary command execution (593/593 pass) |
| K082 | Dashboard UI Completion | Completed | Connected and Interactive UI Views |
| K083 | Release Gate Expansion | Completed | verify-release.ps1 expansion & env isolation verified |
| K084 | Final Integration and Documentation | Completed | Verified by release-gate pass (595/595 unit tests + 101 smoke tests pass) |
| K085 | Slash Command Palette | Completed | Interactive filtering command overlay popup implemented (601/601 pass) |
| K086 | CLI Startup Arguments Expansion | Completed | YOLO mode permission routing and workspace dir options implemented (609/609 pass) |
| K087 | Skill Store Scope Separation | Completed | Global/local skill store separation implemented and verified (613/613 pass) |
| K088 | TeruTeruPandas net10.0 동기화 & 저장소 위생 정리 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (5a74b2f) |
| K089 | /usage 실사용량·비용·성능 관측 대시보드 구현 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (5e918ef) |
| K090 | LSP/MCP 실전 연결 완성 및 Mock Coverage 강화 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (c6db878) |
| K091 | 승인 대기열 동시성 하드닝 & Idempotent Approval Engine | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (da45a19) |
| K092 | Dashboard Multi-Session Observatory & Replay View | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (530f18c) |
| K093 | Self-Healing v2: 실패 분류 확장과 복구 전략 추천 엔진 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (fe5c583) |
| K094 | SkillUsageRecorder 실연결 & Self-Evolving Skills 루프 완성 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (94b3989) |
| K095 | Security Policy Profiles & Red-Team Regression Harness | In Progress | Active Milestone |

## Execution Card
- Active: K095
- Goal: 보안 등급 프로파일(Strict/Permissive/Development) 파서 추가 및 회귀 방지 레드팀 시뮬레이터 구축
- Allowed Files:
  - `Claude4Net.Runtime/PermissionEnforcer.cs`
  - `Claude4Net.Runtime/SecurityPolicyConfig.cs` (신규)
  - `Claude4Net.Tests/` (신규 하네스 테스트 폴더 허용)
  - `Documents/Implementation_Plan.md`
  - `IMPLEMENTATION_PROGRESS.md`
- Forbidden Files: Any files outside the allowed paths.
- Done When: 신규 `K095RedTeamSecurityTests`가 작성되고, 모든 tests 및 verify-release.ps1 gate가 에러 없이 성공적으로 통과함.
- Commit/Push:
  Commit is allowed only after Final Approach Control approval.
  Push is outside Ralph Loop and must not be performed here.
