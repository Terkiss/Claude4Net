# 🚀 Claude4Net 핸드오프 및 배포 가이드

본 문서는 Claude4Net v1.0 시스템의 아키텍처 구조와 운영 환경 배포를 위한 가이드를 제공합니다.

---

## 🏗️ 아키텍처 개요

Claude4Net은 **ReAct (Reasoning + Acting)** 아키텍처를 기반으로 설계된 모듈형 에이전트 시스템입니다.

### 1. 계층 구조
- **App/Cli**: 사용자 상호작용 및 콘솔 렌더링.
- **Runtime**: 에이전트의 '사고 루프(AgentLoop)' 및 도구 실행 오케스트레이션.
- **SDK**: 프로토콜 인터페이스 및 공통 데이터 모델.
- **Api**: 외부 LLM 프로바이더와의 추상화된 통신.
- **Tools/Plugins**: 시스템 제어 및 지식 처리를 위한 자율 도구 집합.

### 2. 핵심 컴포넌트
- **SmartRouter**: 지연 시간, 비용, 에러율을 실시간 분석하여 최적의 LLM 모델을 선택.
- **DataUniverse**: `TeruTeruPandas`를 활용한 고성능 인메모리 RAG 및 상태 저장소.
- **PathSafetyEvaluator**: 실시간 I/O 경로 분석을 통한 강력한 샌드박싱 제어.
- **SelfHealingService**: 도구 실행 오류를 지능적으로 분류하고 재시도 전략을 수립.

---

## 📦 배포 가이드

### 1. 배포 아티팩트 준비
프로젝트 루트에서 다음 명령을 실행하여 배포용 바이너리를 생성합니다.
```powershell
dotnet publish Claude4Net.Cli -c Release -o ./publish
```

### 2. 환경 설정
- `api_key.json`: 실행 환경의 루트에 위치하거나 `!login` 명령을 통해 생성.
- `plugins/`: 추가적인 동적 플러그인(`.dll`)을 배치할 폴더 생성.
- `db/`: DataUniverse 스냅샷이 저장될 디렉토리 권한 확인.

### 3. 보안 권고사항
- **프로덕션 환경**: !YOLO 모드 사용을 지양하고, 반드시 `audit_logs`를 모니터링하십시오.
- **네트워크**: 외부 API 통신을 위한 아웃바운드 443 포트 허용이 필요합니다.

---

## 🛠️ 유지보수 및 확장

### 1. 새로운 도구 추가
`SDK.ITool` 인터페이스를 구현하는 새로운 클래스를 `Claude4Net.MyPlugins` 프로젝트에 추가하거나 별도의 DLL로 빌드하여 `plugins/` 폴더에 드롭하는 것만으로 확장이 가능합니다.

### 2. 모델 업데이트
새로운 LLM 프로바이더가 출시될 경우 `Claude4Net.Api` 프로젝트에 해당 인터페이스를 구현하고 `SmartRouter`에 등록하십시오.

---

**Claude4Net 프로젝트가 성공적인 v1.0 릴리스를 완료했습니다.**
**준비 완료. 핸드오프 승인 대기 중.**
