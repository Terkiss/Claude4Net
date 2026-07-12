# Prompt Routing

이 문서는 사용자 요청을 intent, scope, risk로 분류하고 필요한 skill과 문서를 선택하는 기준이다. 중앙 router skill을 강제하지 않는다. Antigravity/AGY는 각 skill의 `name`과 `description`을 보고 필요한 최소 skill을 선택한다.

## Routing Order (SAD Architecture)

SKILLWEAVER의 **SAD (Skill-Aware Decomposition)** 파이프라인에 따라 동적이고 유연하게 스킬을 라우팅한다.

1. **[Pass 1: Decompose]** `AGENTS.md`를 읽고 사용자 요청을 원자 단위의 하위 작업(Atomic sub-tasks)으로 초기 분해한다.
2. **[Retrieve]** 1차 분해된 쿼리를 바탕으로 백그라운드 벡터 인덱서(`.agents/skills/self-evolution/scripts/skill_indexer.py --search`)를 호출하여 상위 15개의 후보 스킬 힌트를 검색한다.
3. **[Pass 2: Compose]** 검색된 15개 스킬 어휘(Vocabulary)에 맞춰 하위 작업을 재분해하고, 각 원자 작업에 정확히 1개의 실제 스킬을 매핑하여 DAG(실행 계획)를 확정한다.
4. 사용자 요청의 intent, scope, risk를 분류하고 `docs/harness/risk-policy.md`를 확인한다.
5. 확정된 DAG에 따라 Worker에게 단일 작업/단일 스킬 컨텍스트만을 부여해 실행을 지시한다.
6. 작업 후 `docs/harness/quality-gates.md`와 스킬 스크립트 기준에 따라 검증한다.
7. 완료 응답에는 변경 내용, 검증 결과, 남은 위험을 짧게 보고한다.

## Intent

| Intent | Use when | Primary skill |
| --- | --- | --- |
| `product` | 문제, 사용자, MVP, 우선순위, 지표, 로드맵을 정리할 때 | `.agents/skills/plan-product/SKILL.md` |
| `design` | UX, UI, 화면 흐름, 컴포넌트, 상태, 접근성이 바뀔 때 | `.agents/skills/design-ui/SKILL.md` |
| `architecture` | 앱 아키텍처 패턴, API, data, auth, storage, module boundary가 바뀔 때 | `.agents/skills/plan-architecture/SKILL.md` |
| `implementation` | 코드, 설정, 리팩터링, 기능 구현이 필요할 때 | `.agents/skills/implement-feature/SKILL.md` |
| `test` | 테스트 작성, 회귀 검증, 실패 triage, CI 검증이 필요할 때 | `.agents/skills/verify-change/SKILL.md` |
| `deploy` | release, signing, rollout, deploy, rollback readiness가 필요할 때 | `.agents/skills/prepare-release/SKILL.md` |
| `operations` | monitoring, incident, support, post-release feedback가 필요할 때 | `.agents/skills/operate-app/SKILL.md` |
| `harness` | `AGENTS.md`, `.agents/skills/`, `docs/harness/`를 바꿀 때 | 직접 파일과 관련 script를 확인한다 |

## Scope

| Scope | Meaning | Default behavior |
| --- | --- | --- |
| `tiny` | 오탈자, 한 문장, 작은 문서 정리, 국소 스타일 수정 | 필요한 파일만 수정하고 좁은 검증을 한다. |
| `small` | 단일 화면, 단일 함수, 단일 문서 묶음, 작은 테스트 추가 | 관련 skill 하나와 해당 script를 우선한다. |
| `medium` | 여러 파일, 기능 흐름, API/data/UI 상태 영향 | 여러 skill을 순서대로 사용하고 관련 문서를 확인한다. |
| `large` | 앱 구조, 큰 기능, release, migration, 운영 절차 영향 | 작업을 단계로 나누고 검증과 handoff를 명확히 남긴다. |

## Skill Composition

기본 순서는 `product -> design -> architecture -> implementation -> test -> deploy -> operations`이다. 모든 요청에 모든 skill을 쓰지 않는다. 앞 단계 결정이 이미 명확하면 필요한 skill부터 시작한다.

| Request shape | Typical order |
| --- | --- |
| 새 기능이 모호함 | `product -> design/architecture -> implementation -> test` |
| UI만 바뀜 | `design -> implementation -> test 또는 visual verification` |
| API/data/auth가 바뀜 | `architecture -> implementation -> test` |
| 버그 수정 | `implementation -> test` |
| 테스트 보강 | `test` |
| release 준비 | `deploy -> test -> operations` |
| incident 대응 | `operations -> deploy` when rollback or production config may change |
| harness 변경 | related harness files -> syntax/path checks -> skill script checks |

## Document Loading

`AGENTS.md`는 전체 문서 목록을 담지 않는다. 문서 선택은 선택된 skill의 `Context Loading`을 따른다.

- 먼저 관련 코드와 현재 변경사항을 확인한다.
- **[Reference-First Policy]** 프로젝트 내에 `reference/` 디렉토리가 존재하면, 구현 착수 전에 반드시 해당 레퍼런스의 핵심 구조와 기술 선택을 분석한다.
- 문서는 요청과 직접 연결된 것만 읽는다.
- 문서와 코드가 충돌하면 코드를 확인하고 충돌을 보고한다.
- 앱별 사실을 추측으로 채우지 않는다.
- 미확정 질문은 먼저 대화에서 확인하고, 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 남긴다.

## Exploration & Spike Policy (New)

새로운 기술 도메인(처음 도입하는 기술, 아키텍처 결정 등)에 진입할 때 다음 기준에 따라 분기한다:

1. **사전 조사가 가능한 알려진 기술 (Spike Policy)**:
   - 구현 전 반드시 웹 검색 및 오픈소스 조사를 통해 성숙한 대안을 비교한다.
   - 불확실성이 크면 `scratch/`에서 PoC를 진행한다.
   - 결과를 `docs/architecture/`에 기록 후 본 구현에 착수한다.
2. **선행 사례가 없는 선도 기술 (Exploration Mode)**:
   - 에이전트는 "탐색 모드"를 선언한다.
   - 각 시도의 [가설 → 결과 → 교훈]을 반드시 기록하며, 실패를 감점이 아닌 학습 자산으로 취급한다.

## Blocking Questions

Blocking question은 답이 없으면 구현, release, 데이터 변경, 보안/권한 결정을 안전하게 진행할 수 없는 질문이다. Antigravity/AGY는 blocking question을 발견하면 코드 수정, 확정 문서 작성, destructive command, production-impacting action보다 먼저 사용자에게 직접 질문한다.

Blocking으로 보는 경우:

- 제품 범위나 acceptance criteria가 없어 구현 결과를 판단할 수 없다.
- API contract, Firebase/service contract, DTO/Entity mapping, storage schema가 불명확하다.
- Auth, permission, privacy, token/session, credential, payment, data deletion, migration 정책이 불명확하다.
- UI permission/error/empty/loading state가 사용자 행동을 막거나 보안 판단에 영향을 준다.
- 사용자 확인 없이 진행하면 되돌리기 어렵거나 high-risk 정책을 확정하게 된다.

Blocking question flow:

1. 먼저 기존 코드, docs, 사용자 발화에서 답을 찾는다.
2. 답이 없고 작업을 막는다면 사용자에게 1-4개의 짧은 질문으로 바로 확인한다.
3. 사용자가 답하면 갱신 필요성을 보고하고, 사용자 요청 또는 승인 후 관련 docs와 `docs/handoff/decisions.md`에 필요한 만큼 확정 기록한다.
4. 사용자가 나중에 정하겠다고 하면 선택지를 제공하고, 사용자 요청 또는 승인 후 `docs/handoff/open-questions.md`에 blocking으로 기록한다.
5. 사용자가 임시 진행을 원하면 mock, skeleton, draft plan처럼 reversible한 범위로 낮추고 residual risk를 보고한다.

Non-blocking question은 작업을 막지 않는 불확실성이다. Antigravity/AGY는 확정되지 않은 사실을 추측으로 채우지 않고, 선택지를 제공하거나 reversible한 범위로 좁혀 진행하며 앱별 사실처럼 확정 기록하지 않는다.

## Routing Output

작업이 복잡하거나 high-risk이면 내부적으로 다음 정보를 정리한 뒤 진행한다.

- Intent:
- Scope:
- Risk:
- Selected skill:
- Documents to read:
- Verification:
- Residual risk:
