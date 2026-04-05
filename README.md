# 🤖 Claude4Net

> **Claude Code의 강력함을 .NET 10 환경으로 완벽하게 포팅한 차세대 AI 시스템 에이전트**

`Claude4Net`은 단순한 챗봇이 아닙니다. 사용자의 로컬 환경을 완벽하게 이해하고, 파일 시스템을 조작하며, 터미널 명령을 자율적으로 수행하는 **실행형 AI 시스템 에이전트**입니다. Anthropic Claude, Google Gemini, 그리고 로컬 Ollama 모델을 넘나들며 개발자의 생산성을 극대화합니다.

---

## ✨ 핵심 기능 (Key Features)

### 1. 🚀 트리플 프로바이더 지원 (Multi-Provider)
- **Anthropic Claude 3.5 Sonnet**: 정교한 코딩과 추론.
- **Google Gemini 3.0/3.1**: 초고성능 사고(Thinking) 및 대규모 컨텍스트 지원.
- **Local Ollama**: `llama3`, `qwen3` 등 로컬 모델을 활용한 보안 기반 자율 실행.

### 2. ⚡ Antigravity 시스템 프로토콜
- **강력한 로컬 페르소나**: AI가 자신이 로컬 시스템에 상주함을 완벽히 인지합니다.
- **도구 우선 실행(Tool-First)**: "할 수 있다"는 말 대신 즉시 도구를 호출하여 결과를 증명합니다.
- **자율 디버깅(Self-Healing)**: 도구 실행 실패 시 AI가 스스로 오류를 분석하고 재시도합니다.

### 3. 🛡️ 보안 및 !YOLO 모드
- **권한 승인 체계**: 민감한 작업(Write, Bash 등) 실행 전 사용자의 명시적 승인을 요청합니다.
- **🔥 !YOLO (Root Access)**: 모든 보안 가드레일을 해제하고 AI에게 완전한 자율 실행 권한을 부여합니다. 복잡한 프로젝트 분석 시 강력한 위력을 발휘합니다.

### 4. 📊 지능형 사고 가시화 UI
- **Thinking Process**: AI의 사고 과정을 실시간으로 중계하여 에이전트의 논리 흐름을 투명하게 공개합니다.
- **실시간 스트리밍**: 타닥타닥 타이핑되는 생동감 있는 응답 환경을 제공합니다.

---

## 🏗️ 모듈형 아키텍처 (Modular Architecture)

`Claude4Net`은 클린 아키텍처 철학에 따라 6개의 전문 프로젝트로 정밀하게 분리되어 있습니다.

- **`Claude4Net.SDK`**: 핵심 인터페이스와 공통 데이터 모델 정의. 모든 도구와 프로바이더의 기초.
- **`Claude4Net.Runtime`**: '생각-행동-관찰' 루프를 관리하는 에이전트의 핵심 엔진 (`AgentLoop`, `AppState`).
- **`Claude4Net.Api`**: LLM(Claude, Gemini, Ollama)과의 고수준 통신 레이어.
- **`Claude4Net.Tools`**: 시스템의 손발이 되는 도구 집합 (`BashTool`, `FileRead/Write`, `LsTool`).
- **`Claude4Net.Commands`**: 사용자 명령 처리기 (`!login`, `/model`, `!yolo`).
- **`Claude4Net.Cli`**: `Spectre.Console` 기반의 인터랙티브 진입점.

---

## 🎮 주요 명령어 (User Commands)

| 명령어 | 설명 |
| :--- | :--- |
| `!login <provider> <key>` | API 키 또는 Ollama URI를 등록하고 `api_key.json`에 영구 저장합니다. |
| `/model` | 현재 사용 가능한 모든 프로바이더의 모델 리스트를 보여줍니다. |
| `/model <name>` | 사용할 모델을 변경합니다. (접두사에 따라 프로바이더 자동 스위칭) |
| `!yolo` | **[위험]** 모든 보안 승인 절차를 생략하고 완전 자율 모드를 활성화합니다. |
| `/help` | 명령어 도움말을 확인합니다. |

---

## 🛠️ 시작하기 (Getting Started)

### 1. 요구 사항
- [.NET 10.0](https://dotnet.microsoft.com/download) SDK 이상 (또는 .NET 8.0)

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

`Claude4Net`은 인터페이스 중심 설계로 확장이 매우 쉽습니다. `SDK`의 `ITool`을 구현하여 여러분만의 강력한 도구를 추가해 보세요!

---
**Powered by Antigravity Design Philosophy** 🚀
