# 📖 Claude4Net 사용자 매뉴얼 (v1.0)

Claude4Net-App을 활용하여 로컬 시스템을 제어하고 자율적인 AI 에이전트 환경을 구축하는 방법을 안내합니다.

---

## 🛠️ 설치 및 설정

### 1. 요구 사항
- **.NET 10.0 SDK**: [공식 다운로드](https://dotnet.microsoft.com/download)
- **Git**: 소스 코드 관리 및 에이전트의 저장소 접근용
- **(선택) Ollama**: 로컬 모델 실행 시 필요

### 2. 빌드
```powershell
dotnet build
```

### 3. 실행
```powershell
dotnet run --project Claude4Net.Cli
```

---

## 🔑 계정 및 API 관리

에이전트가 외부 모델을 사용하기 위해서는 API 키 등록이 필요합니다. 등록된 키는 `api_key.json`에 암호화되지 않은 상태로 저장되므로 유의하십시오. (`.gitignore`에 의해 커밋에서는 자동 제외됩니다.)

- **Gemini 등록**: `!login gemini <YOUR_API_KEY>`
- **Claude 등록**: `!login claude <YOUR_API_KEY>`
- **Ollama 등록**: `!login ollama http://localhost:11434`
- **Gemini CLI (무료 모드)**: `!login geminicli` (별도 키 없이 로컬 설치된 gemini-cli 활용)

---

## 🎮 주요 명령어 및 사용법

### 1. 모델 전환 및 조회
- **전체 모델 조회**: `/model`
- **모델 변경**: `/model <model_name>` (예: `/model gemini-1.5-pro`)

### 2. 보안 모드
- **보안 가드레일 활성화 (기본)**: 작업 디렉토리(CWD) 외부 접근 시 사용자 승인 필요.
- **!YOLO 모드**: `!YOLO` 명령어를 통해 모든 보안 승인 절차를 생략하고 에이전트에게 완전한 자율 실행 권한 부여.

### 3. 진단 및 관리
- **시스템 진단**: `!doctor` (런타임, OS, API 키, DB 상태 등 점검)
- **에러 로그 정리**: `!prune` (저장된 실행 궤적 및 에러 로그 중 오래된 항목 삭제)
- **환경 변수 마스킹 확인**: `!env` (민감 정보가 마스킹된 환경 변수 목록 출력)

---

## 🧠 DataUniverse (지식 관리)

Claude4Net은 `TeruTeruPandas` 엔진을 사용하여 인메모리 데이터를 관리합니다.

- **데이터 조회**: `pandas_show_tables`, `pandas_table_info` 도구를 통해 AI가 스스로 데이터를 탐색합니다.
- **SQL 실행**: AI가 `pandas_sql` 도구를 사용하여 복잡한 데이터 분석을 수행할 수 있습니다.
- **메모리 유지**: 시스템 종료 시 또는 10분마다 `DB/memory.db`에 데이터가 자동으로 스냅샷 저장됩니다.

---

## 🛡️ 안전한 운영을 위한 팁

1. **작업 디렉토리 설정**: 시작 시 `/setworkspace <경로>`를 통해 에이전트가 활동할 샌드박스 범위를 명확히 지정하십시오.
2. **Audit 로그 확인**: `audit_logs` 테이블을 조회하여 AI가 수행한 민감한 작업(Bash 실행, 파일 수정 등)의 이력을 검토할 수 있습니다.
3. **Source Guard**: 시스템은 출력되는 로그에서 API 키나 패스워드 패턴을 자동으로 감지하여 마스킹합니다.

---

**Claude4Net v1.0 - Antigravity Autonomous Agent System**
