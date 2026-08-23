# Terukirdo Memory Ledger

## Current Status
- **Alibaba Coding Plan (Qwen 3.8 Max, Wan 2.7, HappyHorse 1.1, DeepSeek-V4-Pro, GLM-5.2) 공식 탑재 완료**:
  - `token-plan.ap-southeast-1.maas.aliyuncs.com` 공식 엔드포인트 연동, 멀티모달 & 스트리밍 응답 파이프라인 탑재.
  - `/login qwen <key>` 및 `/login alibaba <key>` 자동 지원.
- **TeruTeruPandas 실시간 텔레메트리 파이프라인 & Blazor 관제 대시보드 고도화 완료**:
  - 90일 잔디 히트맵, 24시간 시간대별 토큰 소모 분석, 분산 추적(Distributed Traces) 워터폴, 브라우저 실시간 결재 큐, 테르키르도 리본 연동.
  - 캘린더 상호작용(재클릭 먹통) 결함 해결 (로컬 즉시 반응, `@key` 가상 DOM 안정화, SignalR 2초 타임아웃 및 자동 폴백 엔진).
- **슬래시 명령어 전체 정비 및 한국어 매뉴얼 번역 완료**:
  - 사용되지 않는 더미/스텁 명령 제거 및 24개 핵심 명령어의 한국어 설명과 `/help` 가이드 제공.
- **3개 국어 README (한국어, 영어, 일본어) 전면 최신화 완료**:
  - 'Why Claude4Net?' 6대 킬러 가치 제안 섹션, 아키텍처 다이어그램 및 최신 뱃지 동기화.
- **전체 단위/통합 테스트 슈트**: 1,012 / 1,012 전수 통과 (100% All-Pass, 0 실패, 0 스킵).
- **Git 상태**: `origin/experiment` 원격 푸시 완료.

## Active Task
- 사용자 후속 요청 대기

## Known Risks
- 없음

## Open Questions
- 없음

## Next Steps
- 사용자 피드백 반영 및 추가 기능 요청 지원

- **Actionable Insight**: Deep reasoning and ultra-work agent modes (e.g. OpenCode ULTRAWORK) with massive multi-turn prompts (150KB+) require extended request timeouts (defaulting to 10~30 minutes) to prevent 504 Gateway Timeout aborts during deep thinking steps.
- **Actionable Insight**: When Claude4Net runs as an OpenAI-compatible API server, providers like `GeminiCliProvider` must operate in **Pure Passthrough Mode (API Mode)** without prepending Claude4Net's internal workspace confinement rules (`[ACTIVE WORKSPACE DIRECTORY]`) or skills, so external agents (Hermes, Cursor, Roo Code) retain their own workspace and system prompt integrity.
- **Actionable Insight**: Windows OS에서 `Process.Start` 시 명령줄 인자 버퍼 길이는 최대 32,767자(32KB)로 제한되므로, 수십~수백 KB 이상의 대용량 프롬프트는 반드시 `StandardInput` 스트림을 통해 파이프로 전송해야 한다.
- **Actionable Insight**: OpenAI 호환 API 서버의 모델 식별자(`id`)는 URL 및 외부 클라이언트 호환성을 위해 공백이나 괄호가 없는 표준 케밥케이스 슬러그(예: `gemini-3.7-flash-high`)를 사용하고, 내부 CLI 구동 시 실제 표시 이름으로 정규화 매핑해야 한다.
- **Actionable Insight**: In Spectre.Console markup strings, literal square brackets like `[Antigravity]` are interpreted as style tags. Always escape them as `[[Antigravity]]` to prevent runtime `Could not find color or style` exceptions.
- **Actionable Insight**: When creating or editing workspace project files using `write_to_file`, do not supply `ArtifactMetadata` because it enforces the artifact directory path restriction.
- **Actionable Insight**: 명령어 로직이 비대해지면 `CommandRegistry`에서 직접 구현하지 말고, `Claude4Net.Runtime/Handlers/`에 도메인별 정적 핸들러 클래스를 만들어 위임한다.
  - 근거: `AgentLoop`와 `CommandRegistry` 양쪽에서 동일한 명령어 로직을 중복 없이 호출하기 위함.
- **Actionable Insight**: `AgentLoop`의 책임은 실행 제어에 집중하고, RAG(검색)와 Telemetry(통계/기록)는 각각 `RAGService`, `TelemetryService`로 분리하여 위임한다.
- **Actionable Insight**: Claude4Net에 새 프로바이더 추가는 3단계 패턴을 따른다. ① `Claude4Net.Api/`에 `ILLMProvider`를 직접 구현하는 독립 클래스 작성 ② `Claude4Net.Runtime/ProviderFactory.cs`에 Factory 클래스 + `ProviderRegistry.cs`의 `RegisterBuiltInDefaults()`에 descriptor 등록 ③ `Claude4Net.Cli/Bootstrap/CliServiceRegistration.cs`에 DI 등록
  - 근거: `OllamaProvider`, `GeminiProvider`, `ClaudeService` 모두 이 패턴을 따름
- **Actionable Insight**: 프로바이더 클래스는 범용 클래스(OpenAiCompatProvider)를 상속하는 껍데기 방식이 아닌, `ILLMProvider`를 직접 구현하는 독립 클래스로 작성해야 함
  - 근거: 주인님 확정 결정 — "클래스 1개 = 전용 프로바이더 1개 매칭이 깔끔하다"
- **Actionable Insight**: API 키는 환경 변수가 아닌 `api_key.json`(AuthManager) + `!login <provider> <key>` 명령으로 관리. 환경 변수는 폴백용만
  - 근거: 주인님 확정 결정 — 기존 프로바이더(Claude, Gemini)가 모두 이 방식을 사용
- **Actionable Insight**: When batch editing or formatting files in Python, do not open files for writing (`open(f, 'w')`) before reading their contents in the same expression, as this immediately truncates the files to 0 bytes. Always read files fully into memory first, then write the cleaned contents.
- **결정론적 Turn-End Memory Sync**: 확률적인 프롬프트 의존성(MD 파일 지시)을 제거하기 위해, `stop_quality_gate.py`에 강제 검증 로직을 추가했습니다.
- **Git status split parsing**: Parsing git status porcelain lines by splitting on whitespace (`line.split(None, 1)`) is much more robust than hardcoded slicing (`line[3:]`).

## 사용자 확정 결정
- 클래스 1개 = 전용 프로바이더 1개 매칭. ILLMProvider 직접 구현하는 독립 클래스 선호. 상속(껍데기) 방식 반대
- API 키는 환경변수 사용 금지. api_key.json(!login) 방식으로 통일
