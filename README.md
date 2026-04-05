# 🤖 Claude4Net

> **Claude Code의 강력함을 .NET 8/10 환경으로 완벽하게 포팅한 차세대 AI 시스템 에이전트**

`Claude4Net`은 단순한 챗봇이 아닙니다. 사용자의 로컬 환경을 이해하고, 파일 시스템을 조작하며, 터미널 명령을 자율적으로 수행하는 **실행형 AI 에이전트**입니다. Anthropic Claude 3.5 Sonnet과 최신 Google Gemini 모델군을 지원하며, 개발자를 위한 극강의 생산성을 지향합니다.

---

## ✨ 주요 기능 (Key Features)

- **🚀 멀티 프로바이더 & 모델 스위칭**:
  - Anthropic Claude 3.5 Sonnet 지원.
  - 최신 Google Gemini 모델군 완벽 지원 (`gemini-3-flash-preview`, `gemini-3.1-pro-preview` 등).
- **⚡ 실시간 스트리밍 UI**: `IAsyncEnumerable` 기반의 런타임으로 AI의 사고 과정과 응답을 타닥타닥 타이핑되는 생동감 있는 화면으로 제공합니다.
- **🛠️ 자율 도구 실행 (Agentic Tools)**:
  - `BashTool`: 로컬 파워쉘/배시 명령어 실행.
  - `FileTools`: 파일 읽기, 쓰기, 지능형 수정을 자율적으로 수행.
- **🛡️ 보안 및 승인 체계**: 민감한 작업 실행 전 사용자 승인을 요청합니다.
- **🔥 !YOLO 모드 (Root Access)**: 모든 가드레일을 해제하고 AI에게 완전한 자율 실행 권한을 부여합니다.
- **🧩 플러그인 기반 아키텍처**: 인터페이스 기반 설계로 누구나 자신만의 도구를 C#으로 개발하여 확장할 수 있습니다.

---

## 🏗️ 아키텍처 (Modular Architecture)

`Claude4Net`은 관심사의 철저한 분리를 위해 6개의 전문 프로젝트로 구성되어 있습니다.

1.  **`Claude4Net.SDK`**: 모든 도구와 플러그인이 준수해야 할 헌법(인터페이스)과 공통 모델이 정의되어 있습니다.
2.  **`Claude4Net.Api`**: LLM(Anthropic, Gemini)과의 저수준 통신 및 메시지 포맷팅을 담당합니다.
3.  **`Claude4Net.Tools`**: AI 에이전트의 '손발'이 되는 실제 시스템 조작 도구 집합입니다.
4.  **`Claude4Net.Runtime`**: '생각-행동-관찰' 루프를 관리하는 에이전트의 핵심 엔진입니다. 상태 관리(`AppState`)와 보안 저장(`AuthManager`)을 총괄합니다.
5.  **`Claude4Net.Commands`**: 사용자 슬래시 명령어(`!login`, `/model` 등)와 시스템 지시문이 정의되어 있습니다.
6.  **`Claude4Net.Cli`**: `Spectre.Console` 기반의 화려하고 인터랙티브한 사용자 인터페이스를 제공하는 진입점입니다.

---

## 🛠️ 시작하기 (Getting Started)

### 1. 요구 사항
- [.NET 8.0](https://dotnet.microsoft.com/download) 이상 (권장: .NET 10.0)

### 2. 설치 및 빌드
```bash
git clone https://github.com/your-repo/Claude4Net.git
cd Claude4Net/Claude4Net-App
dotnet build
```

### 3. API 키 등록
프로그램을 실행한 후, `!login` 명령어를 통해 API 키를 등록하세요. 키는 실행 파일 경로의 `api_key.json`에 안전하게 저장됩니다.
```bash
# Gemini 등록 예시
> !login gemini YOUR_API_KEY_HERE

# Claude 등록 예시
> !login claude YOUR_API_KEY_HERE
```

---

## 🎮 주요 명령어 (Commands)

| 명령어 | 설명 |
| :--- | :--- |
| `!login <provider> <key>` | API 키를 등록하고 `api_key.json`에 저장합니다. |
| `/model <model_name>` | 현재 세션에서 사용할 LLM 모델을 변경합니다. |
| `!yolo` | **[위험]** 모든 보안 승인 절차를 생략하고 완전 자율 모드를 토글합니다. |
| `/help` | 사용 가능한 모든 명령어 목록을 보여줍니다. |

### 💡 사용 가능한 Gemini 모델 리스트
- `gemini-3-flash-preview` (기본값)
- `gemini-3.1-pro-preview`
- `gemini-3.1-flash-lite-preview`
- `gemini-2.5-pro`
- `gemini-2.5-flash`
- `gemini-2.0-flash-lite`

---

## 🤝 기여하기 (Contributing)

`Claude4Net`은 오픈소스 프로젝트입니다. 새로운 도구 제안, 버그 수정, 성능 개선 등 모든 형태의 기여를 환영합니다! `Claude4Net.SDK`를 사용하여 여러분만의 커스텀 도구를 만들어 보세요.

---

## ⚖️ 라이선스 (License)

이 프로젝트는 MIT License를 따릅니다.

---
**Powered by Antigravity Design Philosophy** 🚀
