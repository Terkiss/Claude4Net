# Ralph Loop Queue State

- **Queue Status**: `active`
- **Active Card**: `K088`
- **Current Target**: K088
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
| K088 | TeruTeruPandas net10.0 동기화 & 저장소 위생 정리 | In Progress | Active Execution Card |

## Execution Card
- Active: K088
- Goal: TeruTeruPandas의 .NET 10.0 타겟 동기화 및 빌드/런타임 불필요 잔재 정리
- Allowed Files:
  - `TeruTeruPandas/TeruTeruPandas.csproj`
  - `Claude4Net.Cli/Claude4Net.Cli.csproj`
  - `Claude4Net.Runtime/Claude4Net.Runtime.csproj`
  - `Claude4Net.Tests/Claude4Net.Tests.csproj`
  - `Claude4Net.MyPlugins/Claude4Net.MyPlugins.csproj`
  - `Documents/Implementation_Plan.md`
  - `IMPLEMENTATION_PROGRESS.md`
- Forbidden Files: Do not modify `.agents/`
- Required Work:
  - 모든 프로젝트 파일의 target framework가 `net10.0`으로 완전히 동기화되었는지 검증 및 통일
  - 빌드 클린 타겟에 `db/*.db` 및 런타임 과도기적 파일 정리 규칙 선언
  - 동시 테스트 실행 시 SQLite 커넥션 풀 경합 및 락 현상을 예방하기 위해 리소스 해제 규칙 보강
- Required Tests:
  - warning-free 컴파일 및 `dotnet test` 통과 검증
- Done When:
  - Standard Build: `dotnet build -p:UseAppHost=false` succeeds without new target/compatibility issues.
  - Strict Build: `.\scripts\verify-release.ps1` succeeds.
  - `dotnet test` passes.
- Commit/Push:
  Commit is allowed only after Final Approach Control approval.
  Push is outside Ralph Loop and must not be performed here.
