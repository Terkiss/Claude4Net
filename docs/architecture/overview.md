# Architecture Overview

## Purpose

이 문서는 앱의 기본 프로젝트 구조와 계층 경계를 정의한다.

## Read When

- 구조 변경, 기능 구현, 리팩터링 전에 layer boundary를 확인할 때
- `core/`, `data/`, `domain/`, `view/`, `components/` 배치를 정할 때
- 상태 관리/의존성 주입/라우팅/뷰모델/상태 통지자 등의 구조를 판단할 때 (예: Riverpod, Redux provider 위치 파악 등)
- Clean Architecture, MVVM, Result/AppFailure 경계를 확인할 때

## Update Policy

Antigravity/AGY는 사용자 요청이 있거나, 실제 코드 구조와 이 문서가 충돌함을 보고하고 사용자 승인을 받은 경우에만 이 문서를 수정한다. 앱별 구조를 추측으로 확정하지 않고, 확인된 코드와 사용자 결정만 반영한다.

## Related Docs

`docs/architecture/data-model.md`, `docs/architecture/api.md`, `docs/architecture/auth-permissions.md`, `docs/development/conventions.md`, `docs/design/screen-flows.md`

## Canonical Project Tree

기본 소스 구조는 다음을 따른다. 아래는 예시 구조이며, 프로젝트 기술 스택에 따라 경로와 파일 확장자가 달라질 수 있다 (예: Flutter의 `lib/` 구조, React의 `src/` 구조 등).

```text
{source_root}/
  core/
    di/
      data/
      domain/
      view/
    error/
    router/
      app_router.{ext}
      route_guards.{ext}
      route_names.{ext}
      route_paths.{ext}
      routes/
    theme/
      dark_theme.{ext}
      light_theme.{ext}
      spacing/
        height_space.{ext}
        width_space.{ext}
  data/
    {feature}/
      dto/
      data_source/
      mapper/
      repository/
  domain/
    {feature}/
      entity/
      repository/
      use_case/
  view/
    {xx_screen}/
      {xx_screen}.{ext}
      {xx}_viewmodel.{ext}
      components/
        {local_component}/
          {local_component}.{ext}
          {local_component}_notifier.{ext}
  components/
```

`components/` (또는 `widgets/`)는 앱 전역에서 재사용되는 공용 UI 컴포넌트를 둔다. 특정 화면에서만 쓰는 UI 컴포넌트는 해당 `view/{xx_screen}/components/` 아래에 둔다.

## Layer Responsibilities

| Layer | Owns | Must Not Own |
| --- | --- | --- |
| `core/` | 공통 DI, error, router, theme, extension, base utility | feature-specific business logic |
| `data/` | DTO, external data source, mapper, repository implementation | UI state, UI 컴포넌트, screen-specific logic |
| `domain/` | Entity, repository interface, use case, domain rule | UI, DTO serialization, concrete API/storage implementation |
| `view/` | screen, screen-local UI 컴포넌트, viewmodel, screen-local notifier | DTO, data source, repository implementation |
| `components/` | 앱 전역 reusable UI 컴포넌트 | screen-specific state or page-only UI 컴포넌트 |

## Dependency Direction

Dependency는 안쪽으로 향한다.

```text
view -> domain
data -> domain
core -> shared support
```

- `viewmodel`은 domain의 use case 또는 repository interface를 사용한다.
- `viewmodel`은 DTO, data source, repository implementation을 직접 사용하지 않는다.
- `data`의 repository implementation은 domain repository interface를 구현한다.
- `data_source`는 외부 통신이나 local storage에서 raw/DTO 데이터를 받는다.
- repository implementation은 raw/DTO를 앱 내부에서 쓰는 `Entity`로 변환해 반환한다.

## Core Structure

`core/`는 앱 전역 공통 요소를 둔다.

| Folder | Role |
| --- | --- |
| `core/di/` | Provider wiring, dependency registration, shared provider setup |
| `core/error/` | App-level exception, failure, error mapping, error presentation contract |
| `core/router/` | App router, route names, route guards, navigation configuration |
| `core/theme/` | Theme, color, typography, spacing, common visual constants |

`core/di/`는 상태 관리 provider wiring (예: Riverpod, Redux provider wiring 등)을 레이어와 폴더 구조에 맞게 분리한다.

| Folder | Role |
| --- | --- |
| `core/di/data/` | Data source, repository implementation provider wiring |
| `core/di/domain/` | Use case, domain repository interface provider wiring |
| `core/di/view/` | Screen viewmodel provider wiring |

- Page 또는 screen의 `viewmodel` 전용 provider는 `core/di/view/` 아래에 screen 구조에 맞게 둔다.
- Repository, data source, use case provider는 각 레이어에 맞는 `core/di/` 하위 폴더에 둔다.
- 단 하나의 screen 내부 UI 컴포넌트에서만 쓰는 notifier provider는 해당 notifier와 같은 컴포넌트 folder에 둘 수 있다.
- 전역 공용 UI 컴포넌트나 여러 screen에서 쓰는 provider는 `core/di/` 또는 공용 위치로 올린다.
- 기존 프로젝트에 더 구체적인 DI 구조가 있으면 기존 구조를 우선하되, layer boundary는 유지한다.

`core/router/`는 라우팅 패턴 (예: GoRouter, React Router 등) 기반 navigation을 관리한다.

| File | Role |
| --- | --- |
| `app_router.{ext}` | 최종 router composition (예: 라우터 인스턴스) |
| `route_paths.{ext}` | Route path constants |
| `route_names.{ext}` | Route name constants |
| `route_guards.{ext}` | Auth, onboarding, permission redirect rules |
| `routes/` | Feature or screen route groups |
 
Navigation rules:

- `viewmodel`은 UI 프레임워크의 context 객체 (예: BuildContext, React Context 등)를 사용하지 않는다.
- Path 문자열을 screen에서 직접 쓰지 않는다.
- Route name/path는 `core/router` 상수만 사용한다.
- Auth, onboarding 같은 전역 redirect는 개별 screen에 흩뿌리지 않고 guard에서 처리한다.
- Path parameter는 명확한 이름을 쓴다. 예: `:userId`, `:orderId`.
- Query parameter는 route builder에서 파싱하고, viewmodel에는 필요한 값만 넘긴다.

`core/theme/`는 역할별 파일로 나눈다.

- `dark_theme.{ext}`: dark theme definition
- `light_theme.{ext}`: light theme definition
- `spacing/height_space.{ext}`: height spacing helper (예: Flutter의 SizedBox, CSS margin 기반)
- `spacing/width_space.{ext}`: width spacing helper (예: Flutter의 SizedBox, CSS margin 기반)

프로젝트에 다른 theme 파일명이 이미 있으면 기존 naming을 우선하되, dark/light/spacing 책임은 분리한다.

## Error And Result Boundary

실패 처리는 data/domain 경계와 view 경계를 분리한다.

```text
data_source
  -> may throw external exception
repository_impl
  -> catches exception
  -> maps to AppFailure
  -> returns Result<Entity>
use_case
  -> returns Result<T> when fallible
viewmodel
  -> consumes Result<T>
  -> converts to async state or screen-specific state (예: Riverpod AsyncValue<T>, React Suspense 상태 등)
view
  -> handles async state or screen state
```

- `Result<T>`는 repository interface와 use case의 실패 가능한 계약에 사용한다.
- `Success<T>`는 성공 data를 담는다.
- `Failure<T>`는 `AppFailure`를 담는다.
- `AppFailure`는 `core/error/`에 둔다.
- `data_source`는 외부 SDK/API exception을 throw할 수 있다.
- `repository_impl`은 외부 exception을 catch하고 `AppFailure`로 변환한다.
- `viewmodel`은 `Result<T>`를 푸는 마지막 경계다.
- `viewmodel`은 성공을 data state로, 실패를 상태 관리 에러 상태 (예: Riverpod AsyncError, Redux Error State 등) 또는 screen-specific error state로 변환한다.
- `view`는 `Result<T>`, DTO, data source, repository implementation을 직접 다루지 않는다.
- `repository`와 `use_case`는 상태 관리 비동기 타입 (예: Riverpod AsyncValue 등)을 반환하지 않는다.

`Result<T>`는 모든 함수에 강제하지 않는다. Pure mapper, formatter, local calculation처럼 실패가 business contract가 아닌 함수는 단순 값을 반환한다.

## Data Layer

`data/{feature}/`는 외부 데이터와 domain 사이의 adapter 역할을 한다.

```text
data/
  auth/
    dto/
      login_request_dto.{ext}
      login_response_dto.{ext}
      user_dto.{ext}
    data_source/
      auth_remote_data_source.{ext}
      auth_local_data_source.{ext}
    mapper/
      user_mapper.{ext}
    repository/
      auth_repository_impl.{ext}
```

- `dto/`: API, remote service, external input/output 구조를 표현한다.
- `data_source/`: HTTP, Firebase, database, local storage 같은 실제 입출력을 담당한다.
- `mapper/`: DTO와 Entity 변환을 담당한다.
- `repository/`: domain repository interface의 implementation을 둔다.

DTO는 view로 전달하지 않는다. View에서 필요한 값은 repository implementation 또는 use case를 거쳐 `Entity`로 전달한다.

## Feature And Screen Boundary

- `data/{feature}`와 `domain/{feature}`는 business/domain capability 기준으로 나눈다.
- `view/{xx_screen}`은 UI screen 기준으로 나눈다.
- Feature와 screen은 1:1일 필요가 없다.
- 하나의 screen은 여러 domain feature를 사용할 수 있고, 하나의 feature는 여러 screen에서 사용될 수 있다.
- Screen 이름에 맞추기 위해 불필요한 data/domain feature를 만들지 않는다.

## Domain Layer

`domain/{feature}/`는 앱 내부 의미와 business rule을 둔다.

```text
domain/
  auth/
    entity/
      user_entity.{ext}
    repository/
      auth_repository.{ext}
    use_case/
      login_use_case.{ext}
      logout_use_case.{ext}
```

- `entity/`: 앱 내부에서 안정적으로 사용하는 domain object를 둔다.
- `repository/`: data layer가 구현할 interface를 둔다.
- `use_case/`: 의미 있는 앱 행동이나 business flow를 둔다.

`use_case`는 필수가 아니다. 단순 repository pass-through라면 `viewmodel`이 domain repository interface를 직접 사용할 수 있다. 다음 중 하나가 있으면 use case를 둔다.

- 여러 repository나 data source 조합
- validation, permission, filtering, sorting, error mapping
- 여러 화면에서 재사용되는 앱 행동
- 테스트해야 할 domain rule
- login, logout, delete account처럼 의미 있는 사용자 행동

## View Layer

`view/`는 screen 단위로 구성한다.

```text
view/
  home_screen/
    home_screen.{ext}
    home_viewmodel.{ext}
    components/
      banner_carousel/
        banner_carousel.{ext}
        banner_carousel_notifier.{ext}
```

- `{xx_screen}.{ext}`: 화면 전체 UI 컴포넌트를 둔다.
- `{xx}_viewmodel.{ext}`: 화면 전체 상태, action, navigation trigger, child notifier orchestration을 담당한다.
- `components/`: 해당 screen에서만 쓰는 하위 UI 컴포넌트를 둔다.
- `{local_component}_notifier.{ext}`: 특정 하위 UI 컴포넌트가 독립 상태를 가질 때 해당 컴포넌트 폴더 안에 둔다.

`viewmodel` 표기는 `viewmodel`을 그대로 사용한다. 예: `home_viewmodel.{ext}`, `HomeViewmodel`.
`viewmodel`은 UI 프레임워크의 context 객체 (예: BuildContext, React Context 등)를 보유하거나 context 기반 navigation을 직접 실행하지 않는다.

## State Management

상태 관리는 프로젝트의 상태 관리 패턴 (예: Riverpod, Redux toolkit, MobX 등)을 기본으로 한다.

- 코드 생성 기반 provider/notifier 생성을 사용한다 (예: @riverpod, Redux slice 등).
- 생성 파일은 직접 수정하지 않는다.
- 화면 전체 orchestration은 screen-level `viewmodel`이 담당한다.
- UI 컴포넌트 내부에 닫힌 local state는 해당 컴포넌트 folder의 `notifier`가 담당할 수 있다.
- Screen viewmodel provider는 `core/di/view/`에서 wiring한다.
- 단일 screen-local UI 컴포넌트 notifier provider는 notifier와 같은 컴포넌트 folder에 둘 수 있다.
- screen-level `viewmodel`은 필요한 child notifier를 조합하되, 모든 local state를 무조건 소유하지 않는다.

## Naming

상세 naming은 `docs/development/conventions.md`를 따른다.

- Domain object: `Entity`
- External input/output object: `Dto`
- Repository interface: `{feature}_repository.{ext}`
- Repository implementation: `{feature}_repository_impl.{ext}`
- Screen folder: `{xx}_screen/`
- Screen UI 컴포넌트 file: `{xx}_screen.{ext}`
- Screen viewmodel file: `{xx}_viewmodel.{ext}`
- Local UI 컴포넌트 notifier file: `{local_component}_notifier.{ext}`

## Architecture Rules

- 기존 프로젝트 코드가 이미 존재하면 코드 구조를 먼저 확인한다.
- 이 문서와 코드가 충돌하면 충돌 내용을 보고하고 갱신 필요성을 제안한다.
- 새 계층이나 abstraction은 실제 책임 차이와 변경 격리를 만들 때만 추가한다.
- DTO와 Entity의 필드/책임이 같다면 layer purity만으로 중복 class를 만들지 않는다.
- Auth, privacy, data deletion, migration, secret, release config는 high-risk로 다룬다.
- 여러 module에 영향을 주거나 되돌리기 비싼 결정은 `docs/architecture/adr/`에 남긴다.

