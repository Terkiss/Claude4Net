# 🤖 Claude4Net

> **Claude Code의 강력함을 .NET 10 환경으로 완벽하게 포팅한 차세대 AI 시스템 에이전트**

`Claude4Net`은 단순한 챗봇이 아닙니다. 사용자의 로컬 환경을 완벽하게 이해하고, 파일 시스템을 조작하며, 터미널 명령을 자율적으로 수행하는 **실행형 AI 시스템 에이전트**입니다. Anthropic Claude, Google Gemini, 그리고 로컬 Ollama 모델을 넘나들며 개발자의 생산성을 극대화합니다.

---

## ✨ 핵심 기능 (Key Features)

### 1. 🚀 쿼드러플 프로바이더 지원 (Multi-Provider)
- **Anthropic Claude 3.5 Sonnet**: 정교한 코딩과 추론.
- **Google Gemini 3.0/3.1**: 초고성능 사고(Thinking) 및 대규모 컨텍스트 지원.
- **Gemini CLI (gemini-cli)**: 로컬 쉘에서 실행되는 Gemini 도구를 백그라운드로 납치하여 완전 무료로 로컬 추론 및 시스템 제어를 수행하는 하이브리드 ReAct 엔진.
- **Local Ollama**: `llama3`, `qwen3` 등 로컬 모델을 활용한 보안 기반 자율 실행.

### 2. ⚡ Antigravity 시스템 프로토콜
- **고도화된 로컬 페르소나**: `Gemini 3.0 Antigravity Protocol` 및 `Ollama Local System Agent Protocol` 등 모델별 전용 시스템 프롬프트를 적용하여, AI가 스스로를 대화형 챗봇이 아닌 최고 권한의 로컬 엔지니어로 완벽히 인지합니다.
- **도구 우선 실행(Tool-First) 및 Zero-Hallucination**: 파일이나 디렉토리를 절대 추측하지 않으며, "어떤 일을 할 수 있습니다"라고 말하기 전에 즉시 `BashTool` 또는 `LsTool` 등을 백그라운드에서 직접 실행하여 실제 결과(Observation)만을 가지고 보고합니다.
- **자율 디버깅(Self-Healing)**: 도구 실행 실패 시 AI가 로그를 스스로 분석하고 컨텍스트를 활용해 재시도합니다.

### 3. 🛡️ 보안 및 !YOLO 모드
- **권한 승인 체계**: 민감한 작업(Write, Bash 등) 실행 전 사용자의 명시적 승인을 요청합니다.
- **보안 샌드박싱**: 작업 디렉토리(CWD) 외부로 벗어나는 위험한 경로 접근을 사전에 차단하여 샌드박싱을 강화합니다.
- **🔥 !YOLO (Root Access)**: 모든 보안 가드레일을 일시 해제하고 AI에게 높은 자율 실행 권한을 부여합니다. (단, 시스템 파괴적 작업 감지 시 안전장치가 개입합니다).

### 4. 📊 지능형 사고 가시화 UI
- **Thinking Process**: AI의 사고 과정을 실시간으로 중계하여 에이전트의 논리 흐름을 투명하게 공개합니다.
- **실시간 스트리밍**: 타닥타닥 타이핑되는 생동감 있는 응답 환경을 제공합니다.

### 5. 🧩 완전한 동적 플러그인 생태계 (Dynamic Plugins)
- **자율 파라미터 스키마**: 도구 스스로 필요한 입력 형태(`InputSchema`)를 정의하여 유연성을 극대화했습니다.
- **핫 로드 (Hot-Load)**: `plugins/` 폴더에 `.dll` 파일만 넣으면 소스코드 수정 없이도 AI가 즉각 새로운 도구(`ImageEngineTool`, `DiscordEngineTool` 등)로 인식합니다.

### 6. 🌐 이벤트 기반 아키텍처 및 Discord 통합 (Event-Driven)
- 백그라운드 이벤트 리스너(DiscordListenerService)를 통해 에이전트 로직을 외부 시스템과 실시간 연결합니다.

### 7. 🤔 텍스트 기반 ReAct 파싱 아키텍처 (Text-to-Tool Bridging)
- Function Calling API가 지원되지 않는 CLI 전용 모델이나 환경에서도, 시스템 프롬프트 주입 및 실시간 스트림 데이터 인터셉트를 통해 **완벽한 C# 네이티브 도구 연동**을 구현해냅니다.
- `GeminiCliProvider`는 자체 컨텍스트 덤프와 XML 태그 파서를 통해 터미널 환경을 뛰어넘는 안정적인 자동 툴 체이닝(Auto Tool Chaining) 샌드박스를 제공합니다.

### 8. 🧠 DataUniverse (Hippocampus / In-Memory Long-Term Memory)
- **TeruTeruPandas 탑재**: C# 기반 고성능 SIMD 인메모리 프레임워크(`TeruTeruPandas`)가 에이전트의 메인 두뇌(DB)로 동작합니다.
- **무정형(Schema-less) 동적 테이블 설계**: 고정된 하드코딩 구조 없이 AI가 스스로 `pandas_load_csv`, `pandas_load_json` 도구를 통해 외부 지식을 흡수하고 테이블 구조를 실시간 창조합니다.
- **PandasUniverseManager의 자동 백업 (Auto-Backup)**: 에이전트가 테이블(데이터)을 건드릴 때마다, 트랜잭션 큐 기반의 싱글톤 매니저가 즉시 `DB/memory.db`로 영구 자동 저장(스냅샷 덮어쓰기)을 수행하여 데이터 손실을 완벽히 차단합니다.
- **도구 기반(ReAct) 구조 파악 및 통제**: 답답한 텍스트 SQL 쿼리에만 의존하지 않고, AI가 `pandas_table_info`로 테이블 구조(DataType, Null 여부)를 투시하고, AI 전용 C# 네이티브 툴 플러그인(`PandasDbTool`)을 통해 데이터를 자율적으로 통제(CRUD)할 수 있도록 진화하였습니다.

### 9. 🏎️ 고성능 초최적화 아키텍처 (High-Performance Engine)
- **도구 실행 병렬화**: `IsConcurrencySafe`를 통해 조회성 도구들을 병렬로 동시 실행하여 I/O 오버헤드를 대폭 줄였습니다.
- **인텐트 기반 라우팅**: 정형화된 사용자의 요청은 LLM을 거치지 않고 `QueryRouter`가 식별 후 즉시 처리하여 응답 속도를 극대화했습니다.
- **동적 프롬프트 & 컨텍스트 압축**: 도구 실행 결과가 일정 길이를 넘으면 자동으로 압축 및 요약하여 LLM 토큰 비용을 낮추고 컨텍스트 윈도우 한계를 극복합니다.
- **네트워크 안정성 강화**: `IHttpClientFactory` 인젝션을 통하여 소켓 고갈(Socket Exhaustion)을 방지하고 TCP 연결을 안정적으로 재사용합니다.
- **정밀한 토큰 트래커**: Agent 루프에서 발생하는 Input/Output 토큰 사용량을 철저히 분리 추적하고 요약합니다.
- **프롬프트 캐시 파괴 감지**: API 캐시 비용 절감을 위해 스키마 해시 변경 내역을 `TeruTeruPandas`로 실시간 수집 및 분석하는 텔레메트리 기반을 마련했습니다.

---

## 🏗️ 모듈형 아키텍처 (Modular Architecture)

`Claude4Net`은 클린 아키텍처 철학에 따라 기능별 전문 프로젝트로 정밀하게 분리되어 있습니다.

- **`Claude4Net.SDK`**: 핵심 인터페이스와 공통 데이터 모델 정의. 모든 도구와 프로바이더의 기초.
- **`Claude4Net.Runtime`**: '생각-행동-관찰' 루프를 관리하는 에이전트의 핵심 엔진 (`AgentLoop`, `AppState`).
- **`Claude4Net.Api`**: LLM(Claude, Gemini, Ollama)과의 고수준 통신 레이어.
- **`Claude4Net.Tools`**: 시스템의 손발이 되는 범용 도구 집합 (`BashTool`, `FileRead/Write`, `LsTool`, `LspTool` 등).
- **`Claude4Net.Commands`**: 사용자 명령 처리기 (`!login`, `/model`, `!yolo`).
- **`Claude4Net.Cli`**: `Spectre.Console` 기반의 인터랙티브 진입점.
- **`Claude4Net.Discord`** *(New!)*: 이벤트 기반 디스코드 봇 통합 서비스 및 백그라운드 리스너.
- **`Claude4Net.MyPlugins`** *(New!)*: 런타임에 동적으로 주입되는 플러그인 확장 생태계 (`ImageEngineTool`, `DiscordEngineTool`).

---

## 🎮 주요 명령어 (User Commands)

| 명령어 | 설명 |
| :--- | :--- |
| `!login <provider> [key]` | API 키를 등록하고 `.gitignore` 처리된 `api_key.json`에 영구 저장합니다. `!login geminicli` 입력 시 키 없이 백그라운드 CLI 연동 모드가 켜집니다. |
| `/model` | 현재 사용 가능한 모든 프로바이더의 모델 리스트를 보여줍니다. |
| `/model <name>` | 사용할 모델을 변경합니다. (접두사에 따라 프로바이더 자동 스위칭) |
| `!yolo` | **[위험]** 모든 보안 승인 절차를 생략하고 완전 자율 모드를 활성화합니다. |
| `/help` | 명령어 도움말을 확인합니다. |

---

## 🛠️ 시작하기 (Getting Started)

### 1. 요구 사항
- [.NET 10.0](https://dotnet.microsoft.com/download) SDK 이상 (또는 .NET 8.0/9.0 지원)

### 2. 빌드 및 실행
```bash
cd Claude4Net-App
dotnet build
dotnet run --project Claude4Net.Cli
```

### 3. API 등록 예시
```bash
# Gemini 등록
> !login gemini YOUR_GOOGLE_API_KEY

# Ollama 등록 (로컬)
> !login ollama http://localhost:11434
```

---

## 🤝 기여하기 (Contributing)

`Claude4Net`은 인터페이스 중심 설계로 확장이 매우 쉽습니다. `SDK`의 `ITool`을 구현하거나 새로운 `Plugin` DLL을 동적으로 로드해 여러분만의 강력한 도구를 추가해 보세요!

---
**Powered by Antigravity Design Philosophy** 🚀
