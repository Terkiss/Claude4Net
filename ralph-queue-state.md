# Ralph Loop Queue State

- **Queue Status**: `active`
- **Active Card**: `K089`
- **Current Target**: `K089`
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
| K089 | /usage 실사용량·비용·성능 관측 대시보드 구현 | Active | Initializing Ralph Loop execution |

## Execution Card

# Ralph Execution Card

## Milestone
K089: /usage 실사용량·비용·성능 관측 대시보드 구현

## Goal
API 토큰 실사용량, latencies, 누적 비용을 분석하고 /usage 커맨드 및 Dashboard 전용 뷰 제공

## Allowed Files
- `Claude4Net.Commands/CommandRegistry.cs`
- `Claude4Net.Dashboard/Controllers/`
- `Claude4Net.Dashboard/Hubs/`
- `Claude4Net.Dashboard.Client/Pages/Usage.razor`
- `Claude4Net.Runtime/EventProjectionEngine.cs`
- `Claude4Net.Tests/`
- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`
- `ralph-queue-state.md`

## Forbidden Files
- `.agents/**`
- `.gemini/**`

## Required Work
1. **API Token 사용량 및 Latency(EMA) 집계용 Read Model/Projection 구현**:
   - `EventProjectionEngine.cs` 또는 관련 파일에 API 사용량, 토큰 수(입력/출력), 지연 시간(EMA) 및 비용 환산 집계용 Read Model / Projection 설계 및 구현.
   - ProviderDescriptor의 가격 정책 메타데이터와 바인딩 연계하여 누적 비용 계산 및 모델별 단가 정보 반영.
2. **`/usage` 슬래시 명령어 구현**:
   - CLI에서 구동 가능한 `/usage` 슬래시 커맨드를 `CommandRegistry.cs`에 추가하고, Spectre.Console 테이블 형식으로 실사용량, 비용, 성능(Latency EMA 등) 지표 출력.
3. **대시보드 실시간 사용량 요약 페이지 추가**:
   - `Claude4Net.Dashboard` 컨트롤러/허브에 필요한 API/SignalR 메소드 연결.
   - Dashboard Client 프로젝트에 실시간 사용 데이터를 가시화하기 위한 `Usage.razor` 및 요약 차트/지표 패널 컴포넌트 개발.

## Required Tests
- 신규 `K089UsageTrackingTests` 추가 (누적 토큰 집계, latencies EMA 수렴 및 비용 계산 검증)

## Verification Commands
- `dotnet build -p:UseAppHost=false`
- `dotnet test`
- `.\scripts\verify-release.ps1`
- `git status --short --branch`

## Done When
- `/usage` 슬래시 명령어와 대시보드 Usage.razor가 정상 연동되어 지표를 출력하며, `K089UsageTrackingTests`를 포함한 전체 테스트 게이트(`verify-release.ps1`)가 성공적으로 통과할 때.

## Commit/Push
Commit is allowed only after Final Approach Control approval.
Push is outside Ralph Loop and must not be performed here.
