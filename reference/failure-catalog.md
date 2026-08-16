# 실패 사례집 — Failure Catalog

> 실전에서 발생한 실패와 해결책을 기록하는 문서.  
> 같은 실수를 반복하지 않기 위한 참조용.

---

## F-001: 인코딩 변환으로 스크립트 파괴

**날짜:** 2026-07-20  
**프로젝트:** Claude4Net agy-server  
**심각도:** P1 — 스크립트 실행 불가  

### 상황
서브 에이전트(code-worker)에게 PowerShell 스크립트를 UTF-8 BOM으로 변환하도록 지시.

### 결과
한글 주석의 줄바꿈이 삭제되어 주석과 코드가 같은 줄로 합쳐짐:
```
# ANSI 코드 제거용 패턴$ansiPattern = "`e\[[0-9;]*m"   ← 전체가 주석
# 서버로부터 프롬프트 대기    while ($true) {              ← while이 주석 안
```

### 근본 원인
서브 에이전트가 파일을 읽고 다시 쓸 때 줄바꿈 문자를 잃어버림. 한글 문자 주변에서 특히 발생.

### 해결
1. 영문 주석만 사용하여 스크립트 재작성
2. 인코딩 변환 후 줄 수 비교 검증 추가

### 방지책
- Rule: 스크립트에 한글 주석 금지
- Rule: 서브 에이전트 수정 후 view_file 전체 확인

---

## F-002: 인프라 실패를 HTTP 200 성공으로 반환

**날짜:** 2026-07-20  
**프로젝트:** Claude4Net agy-server  
**심각도:** P2 — 클라이언트가 실패를 감지 못함  

### 상황
`SendPromptToBridgeAsync`에서 브릿지 미연결/타임아웃 시 에러 문자열을 반환.

### 결과
```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": "[Agy Proxy] Bridge not connected. Make sure PowerShell bridge is running."
    }
  }]
}
```
HTTP 200으로 전송. 클라이언트는 정상 응답으로 처리.

### 근본 원인
에러를 문자열로 반환하는 패턴. 호출자가 문자열 내용으로 성공/실패를 판단해야 함.

### 해결
전용 예외 클래스 도입:
- `BridgeNotConnectedException` → 503
- `BridgeTimeoutException` → 504
- `BridgeSendException` → 503
- `BridgeDisconnectedException` → 503
- `BridgeProcessException` → 502

### 방지책
인프라 실패는 반드시 예외로 throw. 문자열 반환 금지.

---

## F-003: FIFO 큐로 비동기 응답 매칭

**날짜:** 2026-07-20  
**프로젝트:** Claude4Net agy-server  
**심각도:** P1 — 응답이 잘못된 요청에 배달  

### 상황
`Queue<TaskCompletionSource<string>>`로 HTTP 요청과 IPC 응답을 매칭.

### 결과
동시 요청 시:
1. 요청 A 입력 → TCS-A 큐 삽입
2. 요청 B 입력 → TCS-B 큐 삽입
3. 응답 B 먼저 도착 → TCS-A에 배달 (FIFO이므로)
4. 요청 A 타임아웃 → TCS-A 큐에 남아 다음 응답을 훔침

### 근본 원인
FIFO 큐는 순서를 가정. 비동기 환경에서 응답 순서는 보장되지 않음.

### 해결
`ConcurrentDictionary<string, TCS>` + Full GUID Request ID:
```
PROMPT:<requestId>:<base64>  →  RESULT:<requestId>:<base64>
```

### 방지책
비동기 응답 매칭에는 항상 ID 기반 상관관계 사용.

---

## F-004: 서브 에이전트 출력물 불완전 검증

**날짜:** 2026-07-20  
**프로젝트:** Claude4Net agy-server  
**심각도:** P2 — 깨진 코드가 커밋됨  

### 상황
서브 에이전트가 수정 완료를 보고. 테르키르도가 grep으로 핵심 패턴만 확인.

### 결과
grep은 패턴 존재만 확인:
```
✅ grep "$ansiPattern" → 있음
✅ grep "while ($true)" → 있음
```
그러나 실제로는 같은 줄에 합쳐져 주석 처리됨 → 실행 불가

### 근본 원인
grep은 줄 내 패턴 매칭만 수행. 줄 무결성(줄바꿈, 들여쓰기, 코드 구조)은 검증 불가.

### 해결
view_file로 전체 내용 확인 + 변환 전후 줄 수 비교

### 방지책
서브 에이전트 수정 파일은 반드시 view_file로 전체 확인.
