# Terukirdo Memory Ledger

## Current Status
- `OpenAI-Compatible API Rework (Wave 1~15)` 및 Ralph Loop 전체 품질 게이트 통과 완료.
- **Hermes Agent & OpenAI Client Integration / Antigravity CLI API Streaming Fix** 완료:
  1. `stream-json` 표준 입력(stdin) 파이프라인 전환: 80KB 이상의 대용량 프롬프트 전송 시 Windows `CreateProcess` 32KB 명령줄 길이 제한으로 인한 `Win32Exception` (502 Bad Gateway) 완벽 해결.
  2. 공백 없는 표준 슬러그(Kebab-case) 모델 ID로 전면 개편 (`gemini-3.7-flash-high`, `gemini-3.6-flash-high`, `claude-sonnet-4-6-thinking` 등) 및 `NormalizeAgyModel` 자동 매핑 적용 (Hermes `model switch failed model names cannot contain spaces` 해결).
  3. `ProviderEndpointPolicy` 엔드포인트 검증 및 API Descriptor 조회 시 격리성 보장.
  4. API 에러 응답 정보 은닉화(Sanitization)로 클라이언트로의 내부 예외 누수 차단 및 서버 콘솔 진단 로깅.
- **Google Gemini Official API & Antigravity Model Catalog Modernization** 완료:
  1. Google Gemini Native API 3.x 전체 라인업(`gemini-3.7-flash`, `gemini-3.6-flash`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.1-pro` 등) 등록 및 DefaultModels 최신화.
  2. Google Antigravity CLI 실제 가용 모델 정밀화 (`Gemini 3.7 Flash`, `Gemini 3.6 Flash`, `Gemini 3.5 Flash`, `Gemini 3.1 Pro`, `Claude Sonnet/Opus 4.6 Thinking`, `GPT-OSS 120B`).
  3. Spectre.Console 대괄호 파서 이스케이프 (`[[Antigravity]]` 등) 완료.
- 솔루션 전체 빌드 0 errors, 전체 테스트 100% 통과 (0 failures, 0 skipped).

## Active Task
- Hermes Agent와의 실제 연결 테스트 및 대화/툴 호출 검증

## Known Risks
- 없음

## Open Questions
- 없음

## Next Steps
- 헤르메스(Hermes) 에이전트에서 Claude4Net API 서버(포트 7836)로 대화 재시도 및 정상 동작 확인

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
