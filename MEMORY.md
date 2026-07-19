# Terukirdo Memory Ledger

## Current Status
- Claude4Net-App 프로젝트: 대규모 리팩토링 완료 (Core 및 Command 분리).
- `CommandRegistry.cs` 경량화 완료 (98라인). 핸들러 로직은 `Claude4Net.Runtime/Handlers/`로 모듈화됨.
- `AgentLoop.cs` 복잡도 개선 완료. RAG 및 Telemetry 로직은 `Claude4Net.Runtime/Services/`로 위임됨.
- 자율 연속 실행(!goal) 및 GLM 프로바이더 기능이 통합된 상태에서 전체 빌드 0 에러 유지.
- 이 프로젝트는 다중 에이전트 환경 (Hermes, Antigravity CLI, Codex 등). 메모리는 Hermes 내부 + docs/ 양쪽 필수 기록

## Active Task
- 리팩토링 후 시스템 안정성 확인 및 커밋 대기

## Known Risks
- 명령어 핸들러 이동으로 인한 외부 플러그인 호환성 (네임스페이스 변경: `Claude4Net.Runtime.Handlers`)

## Open Questions
- 없음

## Next Steps
- 주인님의 승인을 받아 리팩토링 작업 분량을 Commit.

## Key Technical Learnings
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
