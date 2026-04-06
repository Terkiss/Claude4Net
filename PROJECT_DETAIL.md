# Claude4Net: Agentic OS & Event-Driven Architecture

## 1. 프로젝트 개요 (Overview)
`Claude4Net`은 단순한 LLM 챗봇을 넘어, 로컬 시스템과 깊게 상호작용하며 도구(Tools)를 실행하고 실시간으로 피드백을 주고받는 **에이전트 기반 OS(Agentic OS)** 프레임워크입니다. 

본 프로젝트는 **클린 아키텍처(Clean Architecture)**와 **이벤트 주도(Event-Driven)** 설계를 기반으로 하며, CLI와 Discord 등 다양한 채널로부터 들어오는 입력을 하나의 통합된 에이전트 루프가 처리하는 구조를 가집니다.

---

## 2. 핵심 아키텍처 (Architecture)

### 2.1 이벤트 주도 메시지 브로커 (Message Broker)
시스템의 중심에는 `System.Threading.Channels` 기반의 `ChannelBroker`가 존재합니다.
- **Producer (생산자)**: `Claude4Net.Cli`의 키보드 입력 루프, `Claude4Net.Discord`의 리스너 서비스.
- **Consumer (소비자)**: `Claude4Net.Runtime`의 `AgentLoop`.
- **장점**: 입력 채널이 늘어나도 에이전트 코어는 변경할 필요가 없으며, 모든 요청은 FIFO(First-In-First-Out) 방식으로 안전하게 처리됩니다.

### 2.2 클린 아키텍처 계층 구조
1.  **SDK (Core)**: 인터페이스, 공용 모델, 전역 상태(`AppState`). 외부 의존성이 가장 적은 순수 로직 계층.
2.  **Api (Infrastructure)**: LLM 프로바이더(Gemini, Claude, Ollama) 구현체.
3.  **Runtime (Application)**: 에이전트의 '생각-행동-관찰' 루프 제어 및 도구 오케스트레이션.
4.  **Tools/Plugins (Interface Adapters)**: 실제 시스템 작업을 수행하는 개별 도구들.
5.  **Cli/Discord (Frameworks & Drivers)**: 사용자와의 실제 접점.

---

## 3. 주요 모듈 상세 설명

### 3.1 Claude4Net.SDK
- **`InputContext` & `IOutputHandler`**: 입력 소스에 상관없이 에이전트가 답변을 돌려줄 수 있도록 추상화된 통로를 제공합니다.
- **`AuthManager`**: 보안을 위해 API 키를 `api_key.json` 또는 환경 변수에서 관리합니다.
- **`AppState`**: YOLO 모드 여부, 활성화된 모델/프로바이더 등 시스템 전역 상태를 유지합니다.

### 3.2 Claude4Net.Runtime
- **`AgentLoop` (The Brain)**: 
    - 메시지 브로커를 감시하다가 요청이 오면 LLM에게 전달합니다.
    - **실시간 생존 신호(Pulse)**: 생각 중에는 `.`, 도구 감지 시에는 `!`를 출력하여 사용자에게 진행 상황을 알립니다.
    - **실시간 스트리밍**: 각 턴(Turn)이 끝날 때까지 기다리지 않고 생성된 텍스트를 즉시 출력 채널로 쏩니다.
- **`ToolOrchestrator`**: 에이전트가 호출한 도구를 찾아 실행하며, YOLO 모드가 아닐 경우 사용자 승인을 요청합니다.

### 3.3 Claude4Net.Api
- **`GeminiProvider`**: 
    - Google Gemini 2.0/1.5 Flash 모델 지원.
    - **SSE 스트리밍**: `:streamGenerateContent` API를 사용하여 토큰 단위 실시간 응답을 구현했습니다.
    - **자동 포맷 변환**: Anthropic 형식의 메시지를 Gemini 규격(`parts`, `functionResponse`)으로 실시간 변환합니다.
- **`OllamaProvider`**: 로컬 LLM 연동을 지원합니다.

### 3.4 Claude4Net.Discord
- **독립 모듈화**: 메인 루프와 완전히 분리되어 백그라운드 서비스로 동작합니다.
- **전용 로깅 시스템**: 콘솔 화면을 오염시키지 않기 위해 모든 로그는 `Log/data/log.txt`에 스레드 안전하게 기록됩니다.
- **이미지 전송 지원**: 에이전트가 생성한 결과물을 감지하여 디스코드 채널에 파일로 업로드합니다.

### 3.5 Claude4Net.MyPlugins
- **`ImageEngineTool`**: Gemini를 활용해 고화질 이미지를 생성하고 로컬에 저장합니다.
- **`DiscordEngineTool`**: 에이전트가 명시적으로 특정 채널에 메시지를 보낼 수 있는 기능을 제공합니다.
- **`PandasDbTool`**: 고성능 데이터 처리 라이브러리인 `TeruTeruPandas`를 사용하여 CSV, SQLite 데이터를 로드하고 SQL 쿼리를 실행하는 기능을 제공합니다.
    - `pandas_load_csv`: CSV 파일을 테이블로 로드.
    - `pandas_load_sqlite`: SQLite 테이블을 로드.
    - `pandas_sql`: 로드된 테이블들에 대해 SQL 실행.
    - `pandas_show_tables`: 현재 로드된 테이블 목록 및 통계 확인.

---

## 4. 설치 및 설정 (Setup)

### 4.1 필수 요구 사항
- .NET 10.0 SDK 이상 (TeruTeruPandas 등 일부 모듈은 net9.0 사용)
- Discord Bot Token (Message Content Intent 활성화 필수)
- Google Gemini API Key 또는 Anthropic API Key

### 4.2 설정 파일 (`api_key.json`)
실행 파일 디렉터리에 아래 형식으로 저장합니다.
```json
{
  "claude": "your_anthropic_key",
  "gemini": "your_gemini_key",
  "discord": "your_discord_bot_token"
}
```
*Tip: 디스코드 키를 `"test"`로 설정하면 디스코드 모듈은 실행되지 않습니다.*

---

## 5. 사용 가이드

### 5.1 CLI 명령 (Internal Commands)
- `!yolo`: 승인 절차 없이 모든 도구를 자동 실행하는 모드를 토글합니다. (ROOT ACCESS)
- `/model <model_name>`: 사용할 LLM 모델과 프로바이더를 실시간으로 교체합니다.
- `!login <provider> <key>`: 런타임에 API 키를 저장하고 로그인합니다.

### 5.2 디스코드 소환
- 봇이 참여 중인 채널에서 `@봇이름 질문내용` 형식으로 멘션하면 에이전트가 응답합니다.
- 이미지를 생성시키면 채팅창에 파일이 직접 업로드됩니다.

---

## 6. 개발자 확장 가이드 (Extensibility)

새로운 도구를 추가하려면 `ITool` 인터페이스를 구현하고 `Claude4Net.MyPlugins` 프로젝트에 추가하면 됩니다. `Program.cs`의 동적 로더가 빌드된 DLL에서 도구를 자동으로 찾아 등록합니다.

```csharp
public class MyNewTool : ITool
{
    public string Name => "my_tool";
    public string Description => "설명";
    public async Task<object> ExecuteAsync(string arguments, object context)
    {
        // 로직 구현
        return new { result = "Success" };
    }
}
```

---

## 7. 향후 로드맵 (Roadmap)
- [ ] 다중 에이전트 협업(Multi-Agent Swarm) 기능 추가
- [ ] 웹 대시보드(Serena UI) 연동 강화
- [ ] MCP(Model Context Protocol) 서버 통합 가속화
- [ ] 벡터 데이터베이스(RAG) 기반 장기 기억 저장소 도입

---
*Last Updated: 2026-04-06*
*Created by Claude4Net Core Team*
