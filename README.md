# 🤖 Claude4Net (v1.2.0 Stable)

[![Release Gate](https://img.shields.io/badge/Release%20Gate-Passed-green)](./scripts/verify-release.ps1)
[![Tests](https://img.shields.io/badge/Tests-613%2F613%20Passed-brightgreen)](./Claude4Net.Tests/)
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

### 5. 📱 안드로이드 원격 제어 및 API (Android Remote Control & API)
- **원격 관제 및 피드백**: Android 모바일 기기를 활용해 이동 중에도 백그라운드 에이전트 작업을 모니터링하고 피드백을 제공합니다.
- **안전한 기기 페어링**: LAN 자동 탐색 및 콘솔 승인을 기반으로 한 10자리 PIN 페어링과 HMAC-SHA256 Bearer 토큰 보안 인증을 처리합니다.
- **격리된 작업 실행**: REST API 요청에 대해 FIFO 작업 큐를 거쳐 격리된 작업 환경(Worktree)에서 에이전트를 가동하고 15fps 델타 폴링 방식으로 화면을 중계합니다.

---

## 🏗️ 프로젝트 구조 (Architecture)

- **`Claude4Net.Runtime`**: 에이전트 사고 루프, 이벤트 소싱, 자가 치유 및 다중 에이전트 조정의 핵심.
- **`Claude4Net.Dashboard`**: ASP.NET Core 및 Blazor 기반의 실시간 관제 서버 및 클라이언트.
- **`Claude4Net.Api`**: Multi-Provider 통신 레이어 (Gemini 2.0, Claude 3.5, Ollama 지원).
- **`Claude4Net.SDK`**: 공통 인터페이스, 이벤트 모델 및 보안 가드.
- **`Claude4Net.Tools`**: 파일 I/O, Bash 실행, LSP 연동 등 시스템 조작 도구.
- **`TeruTeruPandas`**: 에이전트 전용 고성능 벡터 DB 및 데이터 처리 엔진.
- **`Claude4Net.JaVaSDK`**: Android 앱 빌드를 위한 로컬 이식형(Portable) JDK 17 꾸러미. ([Claude4Net.JaVaSDK](./Claude4Net.JaVaSDK))
- **`android/`**: Jetpack Compose와 Retrofit으로 개발된 원격 제어 모바일 클라이언트 앱. ([android/](./android))

---

## 🛠️ 주요 명령어 (Commands)

### CLI 실행 옵션
- `dotnet run --project Claude4Net.Cli -- --dashboard`: 웹 대시보드와 함께 에이전트를 기동합니다.
- `dotnet run --project Claude4Net.Cli -- --permission-mode ReadWrite`: 워크스페이스 쓰기 권한을 부여하여 실행합니다.
- `dotnet run --project Claude4Net.Cli -- --api`: API 서버를 활성화하여 기동합니다.

### 시스템 명령어
- `/status`: 현재 세션의 요약 정보와 태스크 보드 상태를 표시합니다.
- `/resume <sessionId>`: 이전 세션의 이벤트 이력을 재생하여 작업을 재개합니다.
- `!replay`: 현재 세션의 전체 이벤트 궤적을 확인합니다.
- `!skills`: 등록된 스킬 및 제안된 스킬 목록을 관리합니다.

---

## 📱 안드로이드 원격 제어 및 API (Android Remote Control & API)

`Claude4Net`은 모바일 환경에서도 원격으로 작업을 지시하고, 실시간으로 에이전트의 사고 흐름을 관제하며 피드백을 제공할 수 있도록 **REST API 서버**와 **Android 모바일 클라이언트 앱**을 지원합니다. (K098~K106)

### 1. API 서버 구동 매개변수 (API Server Startup Parameters)
기존 [Claude4Net.Cli/](./Claude4Net.Cli) 프로젝트 구동 시 아래 매개변수를 추가하여 API 서버를 함께 기동할 수 있습니다. API 서버는 백그라운드에서 독자적인 에이전트 작업 실행 환경(Worktree 격리)과 작업 큐(Job Queue)를 관리합니다.

- `--api`: API 서버를 활성화합니다. (`--api true` 및 `--api` 모두 활성화로 처리하며, `--api false`로 명시적 비활성화 가능)
- `--api-host <Host>`: API 서버가 바인딩할 호스트 주소입니다. 외부 안드로이드 기기 등에서의 접속을 허용하려면 `0.0.0.0`으로 설정할 수 있습니다. (기본값: `localhost`)
- `--api-port <Port>`: API 서버가 사용할 포트 번호입니다. (기본값: `5277`)

*구동 예시:*
```powershell
dotnet run --project Claude4Net.Cli -- --dashboard --api --api-host 0.0.0.0 --api-port 5277
```

### 2. 기기 페어링 흐름 (Pairing Flow)
안전한 모바일 원격 제어를 위해 HMAC 암호화 토큰 발급 및 콘솔 승인 기반의 페어링 절차를 거칩니다.

1. **LAN 자동 탐색 (LAN Auto-Discovery)**: Android 앱과 API 서버가 동일한 네트워크 대역에 있을 경우, 클라이언트 앱이 서버를 자동으로 탐색하거나 10자리 PIN 페어링 코드를 요청합니다.
2. **콘솔 승인 프롬프트 (Console Prompt Approval)**: Android 기기가 연결을 요청하면, 서버의 호스트 콘솔 화면에 10초간 승인 대기 프롬프트가 표시됩니다. 호스트 사용자가 콘솔에서 `Y`를 입력해 승인해야만 기기가 등록됩니다.
3. **HMAC 보안 페어링 키 (HMAC Pairing Keys)**: 승인된 기기에 대해 HMAC-SHA256 해시 알고리즘 기반의 보안 인증 토큰이 데이터베이스(`android_auth_tokens`, `android_pairing_requests` 테이블)에 안전하게 저장되며, Android 앱은 이후 모든 API 요청 시 이 Bearer 토큰을 사용합니다.

### 3. 안드로이드 모바일 클라이언트 앱 (Android Mobile Client App)
[android/](./android) 프로젝트에 구현된 전용 모바일 앱은 다음과 같은 디자인 및 관제 편의성을 제공합니다.

- **세로형 9:16 채팅 피드 레이아웃 (Vertical 9:16 Aspect Ratio Chat Feed)**: 모바일 한 손 조작에 최적화된 채팅 형태의 타임라인 뷰를 제공합니다.
- **네비게이션 드로어 (Navigation Drawer)**: 왼쪽 드로어 메뉴를 통해 기존 작업 이력을 조회하고, `+ New Chat` 버튼으로 새로운 에이전트 작업을 즉시 시작할 수 있습니다.
- **Terukirdo 프로필 아바타 (Terukirdo Profile Avatar)**: AI 에이전트가 응답하는 말풍선 옆에 시스템 마스코트 캐릭터인 'Terukirdo'의 파란 머리 프로필 아바타(`terukirdo_profile`)가 표시됩니다.
- **인라인 승인 카드 및 컨트롤**: 에이전트가 빌드/테스트 또는 위험한 파일 수정을 수행하기 전, 실시간 델타 폴링(Delta Polling) 결과에 따라 인라인 승인 대기 카드가 노출되며, 사용자는 모바일 화면에서 직접 `Approve` / `Reject` 버튼을 눌러 작업을 제어할 수 있습니다.

### 4. 안드로이드 앱 빌드 방법 (Build Instructions)
로컬에 빌드 환경이 완벽히 갖춰지지 않은 환경을 위해, 리포지토리의 [Claude4Net.JaVaSDK](./Claude4Net.JaVaSDK)에 포함된 로컬 이식형(Portable) JDK 17을 활용해 빌드할 수 있습니다.

1. `Claude4Net.JaVaSDK/jdk.zip` 파일의 압축을 해제합니다.
2. 터미널에서 Java Home 경로를 압축 해제된 JDK 17 디렉터리로 임시 지정하거나, Gradle 빌드 시 JVM 인자로 주입하여 빌드를 진행합니다.

*PowerShell 빌드 예시:*
```powershell
# 1. SDK 압축 해제 (예: jdk 폴더)
Expand-Archive -Path .\Claude4Net.JaVaSDK\jdk.zip -DestinationPath .\Claude4Net.JaVaSDK\jdk-17

# 2. android/ 폴더로 이동하여 빌드 실행 (시스템 전역 JAVA_HOME이 설정되지 않은 경우 Gradle 인자 사용)
cd android
.\gradlew assembleDebug -Dorg.gradle.java.home="..\Claude4Net.JaVaSDK\jdk-17"
```

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
