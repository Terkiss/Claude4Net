# Ralph Loop Queue State

- **Queue Status**: `active`
- **Active Card**: `K090`
- **Current Target**: K090 (LSP/MCP 실전 연결 완성 및 Mock Coverage 강화)
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
| K079 | Skill Trajectory Mining | Completed | Verified by First Reviewer & Final Controller & Final Approach Control |
| K080 | Dashboard Read Models | Completed | Verified by First Reviewer & Final Controller |
| K081 | Dashboard Typed Commands | Completed | Verified by Unit & Integration Tests and Release Gate |
| K082 | Dashboard UI Completion | Completed | Connected and Interactive UI Views |
| K083 | Release Gate Expansion | Completed | verify-release.ps1 expansion & env isolation verified |
| K084 | Final Integration and Documentation | Completed | Verified by release-gate pass (595/595 unit tests + 101 smoke tests pass) |
| K085 | Slash Command Palette | Completed | Interactive filtering command overlay popup implemented (601/601 pass) |
| K086 | CLI Startup Arguments Expansion | Completed | YOLO mode permission routing and workspace dir options implemented (609/609 pass) |
| K087 | Skill Store Scope Separation | Completed | Global/local skill store separation implemented and verified (613/613 pass) |
| K088 | TeruTeruPandas net10.0 동기화 & 저장소 위생 정리 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (5a74b2f) |
| K089 | /usage 실사용량·비용·성능 관측 대시보드 구현 | Completed | Verified by First Reviewer, Final Controller & Final Approach Control (5e918ef) |
| K090 | LSP/MCP 실전 연결 완성 및 Mock Coverage 강화 | In Progress | Active Milestone |

## Execution Card
- Active: K090
- Goal: MCP 사양에 따르는 클라이언트 모듈 개발, Dynamic Tool Registry에 MCP 도구 목록 동적 주입 및 ToolOrchestrator 위임 체계 연동, 고성능 LSP/MCP 서버 모형 Mock 클래스 구현
- Allowed Files:
  - `Claude4Net.Runtime/Mcp/` (신규 폴더/파일 허용)
  - `Claude4Net.Tools/`
  - `Claude4Net.Tests/`
  - `Documents/Implementation_Plan.md`
  - `IMPLEMENTATION_PROGRESS.md`
- Forbidden Files: Any files outside the allowed paths.
- Done When: 신규 `K090McpLspTests` (Mock 기반 도구 등록, 스키마 변환 및 호출 E2E 검증)가 작성되고, 모든 tests 및 verify-release.ps1 gate가 에러 없이 성공적으로 통과함.
- Commit/Push:
  Commit is allowed only after Final Approach Control approval.
  Push is outside Ralph Loop and must not be performed here.
