# 🤖 Claude4Net (v1.2.0 Stable)

[![Release Gate](https://img.shields.io/badge/Release%20Gate-Passed-green)](./scripts/verify-release.ps1)
[![Tests](https://img.shields.io/badge/Tests-180%2F180%20Passed-brightgreen)](./Claude4Net.Tests/)
[![License](https://img.shields.io/badge/License-MIT-blue)](LICENSE)
[![Framework](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/download)

> **Claude Code의 강력함을 .NET 10 환경으로 완벽하게 포팅하고 확장한 차세대 AI 시스템 에이전트**

`Claude4Net`은 사용자의 로컬 환경을 완벽하게 이해하고, 파일 시스템을 조작하며, 터미널 명령을 자율적으로 수행하는 **실행형 AI 시스템 에이전트**입니다. v1.2.0에서는 이벤트 소싱 기반의 상태 관리, 실시간 웹 관제 대시보드, 그리고 강화된 보안 체계를 통해 더욱 강력한 개발 협업 경험을 제공합니다.

---

## ✨ 핵심 기능 (Key Features)

### 1. 📊 실시간 웹 관제 대시보드 (Web Dashboard)
- **실시간 사고 흐름**: 에이전트의 사고 과정(Thinking Stream)과 도구 호출 과정을 웹 브라우저에서 실시간으로 모니터링합니다. (`--dashboard`)
- **이력 리플레이 (Replay)**: 이벤트 소싱(Event-Sourcing) 아키텍처를 통해 과거 세션의 모든 사고 궤적을 완벽하게 재구성하고 시각화합니다.
- **통합 작업 관리**: 다중 에이전트 협업 상태와 승인 대기열을 한눈에 파악하고 즉각적인 피드백을 제공합니다.

### 2. 🧩 다중 에이전트 조정 (Multi-Agent Coordination)
- **공유 작업 보드 (Shared Task Board)**: 오케스트레이터와 여러 작업 에이전트가 하나의 작업 보드를 공유하며 의존성 있는 복잡한 과업을 수행합니다.
- **역할 기반 할당**: 에이전트의 전문 분야(Research, Coding, Testing 등)와 권한 모드를 고려하여 최적의 작업자에게 과업을 배분합니다.

### 3. 🧠 자가 치유 및 문맥 최적화 (Self-Healing & Optimization)
- **자가 치유 v2 (Self-Healing)**: 실패 패턴(무한 루프, 환각 등)을 자동으로 분류하고, 미래 세션에 교정 지침(Healing Directive)을 주입하여 동일한 실수를 방지합니다.
- **자동 문맥 압축 (Context Compression)**: 모델의 토큰 한도를 지능적으로 관리합니다. 중요 증거를 보존하면서도 오래된 문맥을 요약/압축하여 비용을 절감하고 긴 세션을 유지합니다.

### 4. 🛡️ 강화된 엔터프라이즈 보안 (Security Hardening)
- **심볼릭 링크 방어**: 심볼릭 링크 체인을 통한 워크스페이스 이탈 및 순환 링크 공격을 원천 차단합니다.
- **비밀 마스킹 확대**: 환경 변수, 명령줄 인자, JSON 출력물 내의 민감한 정보를 더욱 철저하게 마스킹합니다.
- **상세 감사 로그**: 보안 거부 발생 시 구체적인 사유를 포함한 감사 로그를 기록하여 투명성을 높였습니다.

---

## 🏗️ 프로젝트 구조 (Architecture)

- **`Claude4Net.Runtime`**: 에이전트 사고 루프, 이벤트 소싱, 자가 치유 및 다중 에이전트 조정의 핵심.
- **`Claude4Net.Dashboard`**: ASP.NET Core 및 Blazor 기반의 실시간 관제 서버 및 클라이언트.
- **`Claude4Net.Api`**: Multi-Provider 통신 레이어 (Gemini 2.0, Claude 3.5, Ollama 지원).
- **`Claude4Net.SDK`**: 공통 인터페이스, 이벤트 모델 및 보안 가드.
- **`Claude4Net.Tools`**: 파일 I/O, Bash 실행, LSP 연동 등 시스템 조작 도구.
- **`TeruTeruPandas`**: 에이전트 전용 고성능 벡터 DB 및 데이터 처리 엔진.

---

## 🛠️ 주요 명령어 (Commands)

### CLI 실행 옵션
- `dotnet run --project Claude4Net.Cli -- --dashboard`: 웹 대시보드와 함께 에이전트를 기동합니다.
- `dotnet run --project Claude4Net.Cli -- --permission-mode ReadWrite`: 워크스페이스 쓰기 권한을 부여하여 실행합니다.

### 시스템 명령어
- `/status`: 현재 세션의 요약 정보와 태스크 보드 상태를 표시합니다.
- `/resume <sessionId>`: 이전 세션의 이벤트 이력을 재생하여 작업을 재개합니다.
- `!replay`: 현재 세션의 전체 이벤트 궤적을 확인합니다.
- `!skills`: 등록된 스킬 및 제안된 스킬 목록을 관리합니다.

---

## 🚀 시작하기 (Getting Started)

### 1. 요구 사항
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) 이상

### 2. 빌드 및 실행
```bash
dotnet build
dotnet run --project Claude4Net.Cli -- --dashboard
```

### 3. 프로바이더 설정
```bash
> /login gemini YOUR_API_KEY   # Gemini 활성화
> /login claude YOUR_API_KEY   # Claude 활성화
```

---

## 🤝 기여 및 라이선스

`Claude4Net`은 MIT 라이선스 하에 제공됩니다. 강력한 보안 정책과 실시간 관제 기능을 갖춘 차세대 에이전트 프레임워크를 함께 만들어보세요!

---
**v1.2.0 Release - Enhanced Observability & Multi-Agent Coordination** 🚀
