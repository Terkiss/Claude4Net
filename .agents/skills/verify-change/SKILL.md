---
name: verify-change
description: Add, update, or run project verification, including unit tests, UI component tests, integration tests, snapshot/golden tests, mocks, CI checks, project-specific static analysis, project-specific tests, regression checks, failure triage, and coverage. Use when behavior changes, tests are requested, CI fails, or completion depends on evidence; do not use as the primary skill for designing product/UI/architecture or implementing code unless verification work is needed.
---

# Verify Change

테스트 전략, unit/UI 컴포넌트/integration test, mock, CI 확인, 최종 검증에 사용한다. 변경이 완료되었다고 말할 수 있는 증거를 만드는 작업을 담당한다.

## Use When

- 동작 변경, UI 변경, test 추가/수정, CI 실패, release verification 확인이 필요할 때.
- 기능 구현이 완료되었는지 검증해야 할 때.
- 어떤 테스트를 추가하거나 실행해야 하는지 불명확할 때.
- 검증을 실행하지 못한 이유와 남은 risk를 정리해야 할 때.

## Context Loading

먼저 변경 diff, 관련 코드, 실패 로그를 확인한다. 아래 문서와 script는 조건이 맞을 때만 사용한다.

- 테스트 전략이나 test type 선택이 필요할 때: `docs/development/testing.md`
- 분석, 테스트, 빌드 명령이 필요할 때: `docs/development/commands.md`
- 변경 유형별 gate가 필요할 때: `docs/harness/quality-gates.md`
- risk별 검증 강도가 필요할 때: `docs/harness/risk-policy.md`
- 일반 테스트/회귀 검증이 필요할 때: `.agents/skills/verify-change/scripts/verify.ps1`
- UI, widget, golden, visual 검증이 필요할 때: `.agents/skills/design-ui/scripts/verify.ps1`
- release readiness 검증이 필요할 때: `.agents/skills/prepare-release/scripts/verify.ps1`

모든 문서와 script를 한 번에 사용하지 않는다. 변경 종류와 risk에 맞는 가장 좁고 신뢰 가능한 검증부터 선택한다.

## Workflow

1. 변경 유형을 분류한다: `unit`, `component`, `integration`, `visual`, `analysis`, `build`, `release`, `final-state`.
2. scope와 risk에 맞는 검증 깊이를 정한다.
3. 기존 테스트 패턴과 fixture/mock 구조를 확인한다.
4. 필요한 테스트를 추가하거나 수정한다.
5. 가장 좁은 신뢰 가능한 명령부터 실행한다.
6. 실패하면 원인과 다음 액션을 분리해 기록한다.
7. 검증하지 못한 항목은 이유, 대체 검증, 남은 risk와 함께 보고한다.
8. 완료 전 `.agents/skills/verify-change/scripts/verify.ps1`을 실행하거나, 실행하지 못한 이유를 남긴다.

## Decision Rules

- 국소 동작은 집중 테스트를 우선한다.
- shared logic, auth, data, payment, release, migration은 검증 범위를 넓힌다.
- UI 변경은 visual verification 또는 `.agents/skills/design-ui/scripts/verify.ps1`을 고려한다.
- release 변경은 `.agents/skills/prepare-release/scripts/verify.ps1`와 rollback 기준을 확인한다.
- 테스트가 없는 영역에 동작 변경이 들어가면 최소한의 회귀 검증 방법을 제안한다.
- 건너뛴 검증을 숨기지 않는다.

## Porting / Migration Verification

포팅, 마이그레이션, 대규모 코드 이관 검증 시에는 빌드 성공만으로 완료로 보지 않는다.

1. **구조 대조**: 원본과 이관본의 파일/디렉터리 수를 robocopy dry-run 또는 동등한 도구로 대조한다.
2. **실제 호출 추적**: 프론트엔드가 실제로 `fetch`/`axios` 등으로 호출하는 API를 grep으로 추출하여, 백엔드 엔드포인트와 교차 대조한다.
3. **스토리지 방식 확인**: 데이터가 REST API, IndexedDB, localStorage, OPFS 중 어디에 저장되는지 소스코드에서 확인한다.
4. **외부 의존성 경계**: 원본 범위 내에 핸들러가 없는 API 호출은 "외부 의존성"으로 분류하고, 포팅 누락과 구분한다.
5. **변경 파일 목록**: 의도적 변경과 비의도적 변경을 명확히 구분하여 보고한다.

## Documentation Updates

문서 갱신이 필요하면 먼저 필요성을 보고하고, 사용자 요청 또는 승인 후 갱신한다.

- 테스트 전략 변경: `docs/development/testing.md`
- 검증 명령 변경: `docs/development/commands.md`
- 품질 gate 변경: `docs/harness/quality-gates.md`
- 미완료 검증/다음 액션: `docs/handoff/next-actions.md`

단순 테스트 실행 결과만으로 문서를 갱신하지 않는다. 반복 가능한 새 검증 절차가 생기면 갱신 필요성을 보고하고 사용자 요청 또는 승인 후 문서화한다.

## Handoff

- 구현 결함이 확인되면 `implement-feature`로 되돌린다.
- UI 상태나 시각 기준이 불명확하면 `design-ui`로 넘긴다.
- API/data/auth 검증 범위가 불명확하면 `plan-architecture`로 넘긴다.
- release verification이나 rollback 검증이 필요하면 `prepare-release`로 넘긴다.

## Completion Output

최종 응답에는 필요한 범위 안에서 다음을 포함한다.

- verification: 실행한 명령과 결과.
- tests changed: 추가/수정한 테스트.
- skipped: 실행하지 못한 검증과 이유.
- failures: 실패가 있다면 원인과 다음 액션.
- confidence: 완료 판단과 남은 risk.
- verification script: 실행한 local script와 결과 또는 미실행 이유.
