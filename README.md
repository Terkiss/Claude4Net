# 🤖 Claude4Net (v1.0.0 Stable)

[![Release Gate](https://img.shields.io/badge/Release%20Gate-Passed-green)](./scripts/verify-release.ps1)
[![Tests](https://img.shields.io/badge/Tests-77%2F77%20Passed-brightgreen)](./Claude4Net.Tests/)
[![License](https://img.shields.io/badge/License-MIT-blue)](LICENSE)

> **Claude Code의 강력함을 .NET 10 환경으로 완벽하게 포팅한 차세대 AI 시스템 에이전트**

`Claude4Net`은 단순한 챗봇이 아닙니다. 사용자의 로컬 환경을 완벽하게 이해하고, 파일 시스템을 조작하며, 터미널 명령을 자율적으로 수행하는 **실행형 AI 시스템 에이전트**입니다. Anthropic Claude, Google Gemini, 그리고 로컬 Ollama 모델을 넘나들며 개발자의 생산성을 극대화합니다.

---

## ✨ 핵심 기능 (Key Features)

### 1. 🚀 지능형 SmartRouter (Cost-Aware Routing)
- **비용 및 성능 최적화**: 지연 시간(Latency), 토큰당 비용(Cost), 에러율(Error Rate)을 EMA(지수 이동 평균)로 실시간 추적하여 최적의 모델을 선택합니다.
- **동적 서킷 브레이커**: 제공자의 에러율이 임계치를 넘으면 자동으로 차단하고, 안정화 시 점진적으로 복구(Half-Open)합니다.

### 2. 🧠 DataUniverse (In-Memory RAG & Long-Term Memory)
- **TeruTeruPandas 탑재**: C# 기반 고성능 SIMD 인메모리 엔진이 에이전트의 메인 두뇌로 동작합니다.
- **L1/L2 임베딩 캐싱**: 빈번한 의미론적 검색(Semantic Search)의 API 비용을 절감하기 위해 메모리(L1) 및 디스크(L2) 캐싱 시스템을 가동합니다.

### 3. 🛡️ 엔터프라이즈급 보안 및 감사 (Security & Auditing)
- **강력한 샌드박싱**: `PathSafetyEvaluator`를 통해 작업 디렉토리 외부로의 무단 접근을 철저히 차단합니다.
- **보안 감사 로그**: 모든 민감한 도구 실행 내역을 `audit_logs` 테이블에 영구 기록하여 사후 추적성을 보장합니다.
- **Source Guard**: 출력 로그에서 API 키, AWS 비밀키, SSH 키 등을 자동으로 감지하여 마스킹합니다.

### 4. ⚡ 초최적화 실행 파이프라인
- **병렬 도구 실행**: 조회성 도구들을 안전하게 병렬로 실행하여 응답 속도를 극대화했습니다.
- **자율 디버깅(Self-Healing)**: 도구 실행 실패 시 AI가 오류를 분류하고 맞춤형 재시도 전략(Exponential Backoff 등)을 스스로 수립합니다.

---

## 🏗️ 프로젝트 구조 (Architecture)

- **`Claude4Net.SDK`**: 공통 인터페이스 및 데이터 모델.
- **`Claude4Net.Runtime`**: 에이전트의 사고 루프(`AgentLoop`) 및 오케스트레이션.
- **`Claude4Net.Api`**: Multi-Provider 통신 레이어 (Claude, Gemini, Ollama).
- **`Claude4Net.Tools`**: 파일 I/O, Bash 실행, LSP 연동 등 시스템 도구.
- **`Claude4Net.MyPlugins`**: 동적 플러그인 확장 생태계.

---

## 📖 문서 및 가이드 (Documentation)

- **[사용자 매뉴얼 (User Manual)](./Documents/USER_MANUAL.md)**: 설치, API 등록 및 사용법 안내.
- **[핸드오프 가이드 (Handoff)](./Documents/HANDOFF.md)**: 아키텍처 개요 및 배포 가이드.
- **[성능 리포트 (Performance)](./Documents/PERFORMANCE.md)**: 벤치마크 결과 및 최적화 전략.
- **[운영 가이드 (Operations)](./Documents/OPERATIONS.md)**: 빌드/테스트 오류 대응 지침.

---

## 🛠️ 시작하기 (Getting Started)

### 1. 요구 사항
- [.NET 10.0](https://dotnet.microsoft.com/download) SDK 이상

### 2. 빌드 및 실행
```bash
dotnet build
dotnet run --project Claude4Net.Cli
```

### 3. API 등록 예시
```bash
> !login gemini YOUR_GOOGLE_API_KEY
> !login claude YOUR_ANTHROPIC_API_KEY
```

---

## 🤝 기여 및 라이선스

`Claude4Net`은 MIT 라이선스 하에 오픈 소스로 제공됩니다. 인터페이스 중심 설계로 확장이 매우 용이하오니, 여러분만의 강력한 도구를 추가하여 생태계에 기여해 보세요!

---
**v1.0.0 Stable Release - Powered by Antigravity Design Philosophy** 🚀
