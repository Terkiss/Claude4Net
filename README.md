# 🤖 Claude4Net (v1.1.0 Stable)

[![Release Gate](https://img.shields.io/badge/Release%20Gate-Passed-green)](./scripts/verify-release.ps1)
[![Tests](https://img.shields.io/badge/Tests-138%2F138%20Passed-brightgreen)](./Claude4Net.Tests/)
[![License](https://img.shields.io/badge/License-MIT-blue)](LICENSE)
[![Framework](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/download)

> **Claude Code의 강력함을 .NET 10 환경으로 완벽하게 포팅하고 확장한 차세대 AI 시스템 에이전트**

`Claude4Net`은 단순한 챗봇이 아닙니다. 사용자의 로컬 환경을 완벽하게 이해하고, 파일 시스템을 조작하며, 터미널 명령을 자율적으로 수행하는 **실행형 AI 시스템 에이전트**입니다. Anthropic Claude, Google Gemini, 그리고 로컬 Ollama 모델을 넘나들며 개발자의 생산성을 극대화합니다.

---

## ✨ 핵심 기능 (Key Features)

### 1. 📂 지능형 세션 및 태스크 관리 (Session & Task Board)
- **영속적 세션**: `.claude4net/sessions/` 경로에 모든 대화 내역, 태스크 보드, 진행 상황을 자동으로 기록하고 `/resume`으로 복구합니다.
- **실시간 태스크 보드**: `/status` 명령어를 통해 현재 에이전트가 수행 중인 작업의 전체적인 맥락과 하위 태스크 상태를 한눈에 파악합니다.

### 2. 🛡️ 엔터프라이즈급 보안 및 권한 제어 (Security & Permissions)
- **Permission Enforcer**: 읽기 전용, 워크스페이스 쓰기, 위험 명령 차단 등 세분화된 권한 정책을 적용합니다.
- **Diff 기반 승인**: 파일 수정 시 실제 변경 내용을 유니파이드 디프(Unified Diff) 형태로 미리보고 승인 여부를 결정합니다.
- **강력한 샌드박싱**: 작업 디렉토리 외부로의 무단 접근을 `PathSafetyEvaluator`가 원천 차단하며, 모든 민감 활동은 `audit_logs`에 기록됩니다.

### 3. 🧠 고성능 데이터 엔진 (TeruTeruPandas & RAG)
- **TeruTeruPandas 탑재**: C# 기반 고성능 SIMD 인메모리 데이터 엔진이 에이전트의 메인 두뇌(Long-term Memory)로 동작합니다.
- **지능형 RAG**: L1/L2 임베딩 캐싱을 통해 비용을 절감하면서도 강력한 의미론적 검색 기능을 제공합니다.

### 4. 🚀 지능형 SmartRouter & Self-Healing
- **비용 인식 라우팅**: 지연 시간, 비용, 에러율을 추적하여 최적의 모델(Claude 3.5 Sonnet, Gemini 2.0 Flash 등)을 선택합니다.
- **자율 디버깅**: 도구 실행 실패 시 에러를 분류하고 스스로 재시도 전략을 수립하여 문제를 해결합니다.

### 5. 🧩 확장 가능한 스킬 생태계 (Skill Registry)
- **동적 스킬 로딩**: 전문화된 워크플로우나 도구 모음을 스킬로 등록하고 관리합니다 (`!skills`).
- **리소스 기반 설계**: 체크리스트, 플레이북, 프로토콜 등 정형화된 지식을 에이전트에게 즉시 주입합니다.

---

## 🏗️ 프로젝트 구조 (Architecture)

- **`Claude4Net.Runtime`**: 에이전트 사고 루프, 세션 관리, 권한 집행 및 오케스트레이션의 핵심.
- **`Claude4Net.Api`**: Multi-Provider 통신 레이어 (Gemini 2.0, Claude 3.5, Ollama 지원).
- **`Claude4Net.SDK`**: 공통 인터페이스, 데이터 모델 및 Diff 서비스.
- **`Claude4Net.Tools`**: 파일 I/O, Bash 실행, LSP 연동 등 시스템 조작 도구.
- **`TeruTeruPandas`**: 에이전트 전용 고성능 벡터 DB 및 데이터 처리 엔진.
- **`Claude4Net.Discord`**: 비동기 협업을 위한 디스코드 인터페이스 및 승인 핸들러.

---

## 🛠️ 주요 명령어 (Commands)

### 시스템 슬래시 명령어
- `/status`: 현재 세션의 요약 정보와 태스크 보드 상태를 표시합니다.
- `/resume <sessionId>`: 이전 세션을 불러와 작업을 이어갑니다.
- `/coordinate`: 다중 에이전트 간의 마일스톤 및 증거 기반 동기화를 수행합니다.

### 관리 및 진단 명령어
- `!doctor`: 시스템 환경, API 키, 데이터베이스 무결성 등을 정밀 진단합니다.
- `!audit`: 최근 발생한 보안 및 권한 관련 이벤트를 확인합니다.
- `!skills`: 등록된 스킬 목록을 확인하고 관리합니다.
- `!prune`: 히스토리를 정리하여 컨텍스트 효율을 높입니다.

---

## 🚀 시작하기 (Getting Started)

### 1. 요구 사항
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) 이상

### 2. 빌드 및 실행
```bash
dotnet build
dotnet run --project Claude4Net.Cli
```

### 3. 프로바이더 설정 예시
```bash
> !login gemini YOUR_GOOGLE_API_KEY      # Gemini 프로바이더 활성화
> !login claude YOUR_ANTHROPIC_API_KEY   # Claude 프로바이더 활성화
> !login geminicli                      # Gemini CLI 연동 모드 (키 불필요)
```

---

## 🤝 기여 및 라이선스

`Claude4Net`은 MIT 라이선스 하에 제공됩니다. 강력한 보안 정책과 확장성 있는 아키텍처를 바탕으로 안전하고 똑똑한 에이전트를 함께 만들어보세요!

---
**v1.1.0 Release - Focused on Reliability & Developer Experience** 🚀
