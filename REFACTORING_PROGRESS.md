# Claude4Net API Server 리팩터링 진행 상황

## 시작: 2026-08-20
## 브랜치: experiment

---

## Phase 1: 적대적 감사 보고서 보안 패치 (완료)

### 배경
적대적 검증 에이전트가 지적한 아키텍처, 보안, 동시성 결함에 대한 2단계 개선 로드맵 실행.

### 완료된 패치 (5건)

#### 1. ADV-02: SSE Usage Chunk 예외 처리
- **파일**: `Claude4Net.Runtime/ApiServer/Claude4NetApiServer.cs`
- **문제**: SSE 스트리밍 중 usage chunk 전송 시 클라이언트 연결 해제 발생 시 예외 처리 누락
- **해결**: `await context.Response.WriteAsync(...)` 호출을 try/catch 블록 내부로 이동
- **라인**: ~580-595

#### 2. SEC-04: JSON 제어 문자 이스케이프
- **파일**: `Claude4Net.Runtime/ApiServer/Streaming/IncrementalToolCallParser.cs`
- **문제**: JSON 직렬화 시 < 0x20 ASCII 제어 문자 이스케이프 누락 (JSON 스펙 위반)
- **해결**: `EscapeJsonString()` 메서드에 제어 문자 이스케이프 로직 추가
  ```csharp
  if (c < 0x20)
  {
      sb.Append("\\u");
      sb.Append(((int)c).ToString("x4"));
  }
  ```
- **라인**: ~350-380

#### 3. SEC-05: 버퍼 크기 제한
- **파일**: `Claude4Net.Runtime/ApiServer/Streaming/IncrementalToolCallParser.cs`
- **문제**: Tool argument 버퍼에 크기 제한 없음 → 메모리 과소비 가능
- **해결**: 최대 버퍼 크기 1MB 제한 추가
  ```csharp
  private const int MaxArgumentBufferSize = 1_000_000;
  if (_toolArgBuffers[index].Length > MaxArgumentBufferSize)
  {
      throw new InvalidOperationException("Tool argument exceeds maximum size (1MB)");
  }
  ```
- **라인**: ~440-470

#### 4. SEC-01: 타이밍 공격 방어
- **파일**: `Claude4Net.Runtime/ApiServer/Claude4NetApiServer.cs`
- **문제**: API Key 검증 시 `string.Equals()` 사용 → 타이밍 공격 취약
- **해결**: `CryptographicOperations.FixedTimeEquals()` 사용
  ```csharp
  using System.Security.Cryptography;
  
  var tokenBytes = Encoding.UTF8.GetBytes(token);
  var apiKeyBytes = Encoding.UTF8.GetBytes(ApiKey);
  if (tokenBytes.Length != apiKeyBytes.Length || 
      !CryptographicOperations.FixedTimeEquals(tokenBytes, apiKeyBytes))
  ```
- **라인**: ~110-120

#### 5. SEC-03: Health 엔드포인트 정보 노출 제한
- **파일**: `Claude4Net.Runtime/ApiServer/Claude4NetApiServer.cs`
- **문제**: `/api/v1/health`가 Port, ActiveProvider 등 민감 정보 반환
- **해결**: 단순화된 응답으로 변경
  ```csharp
  app.MapGet("/api/v1/health", () => Results.Ok(new { status = "healthy" }));
  ```
- **라인**: ~740

### 테스트 수정

#### DI 등록 순서 수정
- **파일**: `Claude4Net.Tests/OpenAiApiServerTests.cs`
- **문제**: `TestMockProviderFactory`가 `CliServiceRegistration`보다 늦게 등록되어 실제 팩토리들이 먼저 선택됨
- **해결**: 등록 순서 변경
  ```csharp
  var services = new ServiceCollection();
  // MockProviderFactory를 먼저 등록
  services.AddSingleton<IProviderFactory, TestMockProviderFactory>();
  CliServiceRegistration.ConfigureServices(services);
  // IEmbeddingProvider는 나중에 등록하여 덮어쓰기
  services.AddSingleton<IEmbeddingProvider, TestMockEmbeddingProvider>();
  services.AddSingleton<Claude4NetApiServer>();
  ```
- **라인**: ~58-63

#### Health 테스트 적응
- **파일**: `Claude4Net.Tests/OpenAiApiServerTests.cs`
- **문제**: SEC-03 수정으로 `Port` 필드 제거됨
- **해결**: `Port` 검증 제거
  ```csharp
  // SEC-03: health 엔드포인트는 민감정보 노출 방지를 위해 Port를 반환하지 않음
  ```
- **라인**: ~196

### 테스트 결과
- **총 테스트**: 749개
- **통과**: 749개 (100%)
- **실패**: 0개
- **실행 시간**: ~1분 33초

---

## Phase 2: God-Class 분해 (진행 중)

### 목표
`Claude4NetApiServer.cs` (1,113줄)를 책임별로 분리하여 유지보수성 향상

### 분석된 책임 영역

| 영역 | 줄 수 | 내용 |
|------|-------|------|
| 서버 라이프사이클 | 50~153 | Start/Stop, CORS, 인증 미들웨어 |
| 인증 미들웨어 | 85~135 | API Key 검증 |
| OpenAI 엔드포인트 | 155~737 | models, completions, embeddings, chat |
| 커스텀 엔드포인트 | 739~826 | health, status, usage, agent/run, tools, skills |
| 헬퍼 메서드 | 828~1068 | ProviderResolver, PromptBuilder, Utils |

### 분리 계획 (안전한 순서)

1. **유틸리티 클래스** (무의존, 정적)
   - ✅ `EmbeddingUtils.cs` - Base64 변환
   - ✅ `StopSequenceHelper.cs` - Stop sequence 적용
   - ✅ `PromptBuilder.cs` - 프롬프트 빌더

2. **인증 미들웨어** (의존성 낮음)
   - 🔄 `ApiKeyAuthMiddleware.cs` - API Key 검증

3. **엔드포인트 그룹화** (의존성 높음)
   - ⏸️ `OpenAiEndpoints.cs` - OpenAI 호환 엔드포인트
   - ⏸️ `CustomEndpoints.cs` - 커스텀 엔드포인트

### 현재 완료된 작업

#### 1. EmbeddingUtils.cs 생성 ✅
- **파일**: `Claude4Net.Runtime/ApiServer/EmbeddingUtils.cs` (신규)
- **내용**: `FloatsToBase64()`, `Base64ToFloats()` 정적 메서드
- **라인 수**: 42줄

#### 2. PromptBuilder.cs 생성 ✅
- **파일**: `Claude4Net.Runtime/ApiServer/PromptBuilder.cs` (신규)
- **내용**: `BuildFromMessages()` 정적 메서드
  - JSON response format 지원
  - Tools instructions 지원
  - Multi-message conversation 지원
- **라인 수**: 95줄

#### 3. StopSequenceHelper.cs 생성 ✅
- **파일**: `Claude4Net.Runtime/ApiServer/StopSequenceHelper.cs` (신규)
- **내용**: `Apply()` 정적 메서드
  - String, JsonElement, IEnumerable 지원
  - 가장 먼저 나타나는 stop sequence 기준으로 자르기
- **라인 수**: 62줄

#### 4. Claude4NetApiServer.cs 수정 (부분 완료) 🔄
- `FloatsToBase64()` 호출 → `EmbeddingUtils.FloatsToBase64()` 변경 완료
- `BuildPromptFromMessages()` 호출 → `PromptBuilder.BuildFromMessages()` 변경 완료
- `ApplyStopSequences()` → `StopSequenceHelper.Apply()` 위임 완료
- **원본 메서드 제거**: 미완료 (XML 파싱 문제로 중단)
  - `BuildPromptFromMessages()` 메서드 본문 아직 존재 (라인 937-979)
  - `FloatsToBase64()` 메서드 본문 이미 제거됨
  - `Base64ToFloats()` 메서드 본문 이미 제거됨

### 현재 상태

**Claude4NetApiServer.cs**: 1,054줄 (원본 1,113줄 → 59줄 감소)

**제거된 코드**:
- ✅ `FloatsToBase64()` 메서드 (15줄)
- ✅ `Base64ToFloats()` 메서드 (10줄)
- ⏸️ `BuildPromptFromMessages()` 메서드 (42줄) - 아직 존재

**변경된 호출**:
- ✅ `EmbeddingUtils.FloatsToBase64()` (라인 336)
- ✅ `PromptBuilder.BuildFromMessages()` (라인 377)
- ✅ `StopSequenceHelper.Apply()` (라인 983)

### 중단 이유
`BuildPromptFromMessages()` 메서드 본문에 XML 태그(`<invoke>`)가 포함되어 있어 `patch` 도구가 XML로 오인하여 수정 실패. `execute_code` (Python)를 사용한 전체 파일 복원 필요.

---

## 미완료 작업 (다음 세션에서 계속)

### Phase 2 계속

1. **BuildPromptFromMessages() 메서드 제거**
   - `execute_code` (Python)로 Claude4NetApiServer.cs 전체 읽기
   - 라인 937-979 삭제
   - 파일 다시 쓰기

2. **ApiKeyAuthMiddleware.cs 분리**
   - Claude4NetApiServer.cs 라인 85-135의 인증 미들웨어 로직 추출
   - 별도 클래스로 구현

3. **엔드포인트 그룹화** (선택 사항)
   - `OpenAiEndpoints.cs`: models, completions, embeddings, chat
   - `CustomEndpoints.cs`: health, status, usage, agent/run, tools, skills

4. **테스트 재실행**
   - 모든 변경 후 749개 테스트 재실행
   - 회귀 확인

### Phase 3 (미래)

1. **명령어 서브시스템 통합**
   - `AgentLoop`의 switch문 제거
   - Split-Brain 해결 (명령어 라우팅 일원화)

2. **정적 AppState Contextual 주입**
   - 멀티테넌트 동시 요청 안전성 확보
   - AppState를 인스턴스 필드로 전환

---

## 파일 변경 요약

### 신규 생성 (3개)
1. `Claude4Net.Runtime/ApiServer/EmbeddingUtils.cs` (42줄)
2. `Claude4Net.Runtime/ApiServer/PromptBuilder.cs` (95줄)
3. `Claude4Net.Runtime/ApiServer/StopSequenceHelper.cs` (62줄)

### 수정 (2개)
1. `Claude4Net.Runtime/ApiServer/Claude4NetApiServer.cs`
   - Phase 1: 5개 보안 패치
   - Phase 2: 3개 유틸리티 호출 변경, 2개 메서드 제거
   - 현재: 1,054줄 (원본 1,113줄)

2. `Claude4Net.Tests/OpenAiApiServerTests.cs`
   - DI 등록 순서 수정
   - Health 테스트 Port 검증 제거
   - 디버그 출력 코드 제거

---

## 테스트 결과

### Phase 1 완료 후
```
총 테스트 수: 749
     통과: 749
```

### Phase 2 현재 상태
- 아직 테스트 재실행 안 함
- `BuildPromptFromMessages()` 메서드가 아직 존재하여 컴파일 가능
- 다음 세션에서 메서드 제거 후 테스트 필요

---

## 기술적 결정 사항

### Phase 1: 보안 패치 우선순위
1. **즉시 패치**: ADV-02, SEC-01, SEC-03, SEC-04, SEC-05
2. **구조적 리팩터링**: Phase 2로 연기

**근거**: 보안 취약점은 즉각적인 위험, 구조적 개선은 점진적 접근

### Phase 2: 분리 순서
1. **유틸리티 먼저**: 무의존, 정적 메서드 → 리스크 낮음
2. **미들웨어 다음**: 의존성 낮음 → 중간 리스크
3. **엔드포인트 마지막**: 의존성 높음 → 높은 리스크

**근거**: 점진적 변경으로 회귀 위험 최소화

### DI 등록 순서
- `TestMockProviderFactory`를 **먼저** 등록
- `CliServiceRegistration.ConfigureServices()`를 **나중에** 호출
- `IEmbeddingProvider`를 **마지막**에 등록하여 덮어쓰기 보장

**근거**: `ProviderRegistry.CreateProvider()`가 `FirstOrDefault()` 사용 → 먼저 등록된 팩토리가 우선 선택됨

---

## 알려진 문제

### write_file 도구의 XML 파싱 버그
- **증상**: C# 코드에 `<invoke>` 같은 XML 태그가 있으면 파일이 잘림
- **해결**: `execute_code` (Python) 사용
- **발생 위치**: `IncrementalToolCallParser.cs` 복원 시

### patch 도구의 XML 파싱 버그
- **증상**: old_string에 XML 태그가 있으면 매칭 실패
- **해결**: `execute_code` (Python)로 전체 파일 수정 또는 더 작은 단위로 patch
- **발생 위치**: `BuildPromptFromMessages()` 제거 시

---

## 다음 세션 체크리스트

- [ ] `BuildPromptFromMessages()` 메서드 제거 (execute_code 사용)
- [ ] 전체 테스트 재실행 (749개)
- [ ] `ApiKeyAuthMiddleware.cs` 분리
- [ ] 테스트 재실행
- [ ] (선택) 엔드포인트 그룹화
- [ ] 최종 테스트 재실행
- [ ] 커밋 및 푸시

---

## 참고 문서

- 적대적 감사 보고서: `.agents/audit_adversarial_2026-08-20.md`
- 프로젝트 구조: `docs/Project_Structure.md`
- AGENTS.md: 최상위 프로젝트 지침
- Terukirdo Protocol v5.4: 에이전트 행동 규약

---

**마지막 업데이트**: 2026-08-20 (세션 종료 전)
