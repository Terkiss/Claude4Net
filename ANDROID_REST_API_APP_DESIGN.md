# Claude4Net Android REST API 앱 기획 설계

## 1. 목적

Claude4Net을 Android 앱에서도 사용할 수 있도록 Android 앱을 원격 작업 관제 클라이언트로 설계한다.

Android 앱은 직접 빌드하거나 Android 로컬 파일 시스템에서 실제 개발 작업을 수행하지 않는다. 대신 REST API 서버에 작업을 요청하고, 서버는 전용 작업 디렉터리에서 Claude4Net을 실행하여 코드 수정, 빌드, 검증, 커밋, 푸시를 수행한다. Android 앱은 15fps 수준의 REST polling으로 작업 상태를 관찰하고, 승인/거절/취소 같은 사용자 명령만 서버로 보낸다.

## 2. 핵심 결론

- Terminal과 Discord는 기존 Claude4Net의 local interactive world로 유지한다.
- REST API와 Android 앱은 별도의 Android REST job world로 분리한다.
- REST API는 별도 프로젝트로 파생하지 않고 기존 `Claude4Net.Dashboard` ASP.NET host에 붙인다.
- CLI 인수는 `--dashboard --api true` 또는 단축형 `--dashboard --api`를 사용한다.
- `--api true`와 `--api`는 모두 API 활성화로 처리하고, `--api false`는 명시적 비활성화로 처리한다.
- REST API 서버와 Android 앱이 같은 네트워크에 있으면 LAN 승인 인증 경로를 사용할 수 있다.
- LAN 승인 인증은 기본 활성화한다.
- LAN 승인 인증은 같은 네트워크 판정 후 서버 터미널에서 `Y`를 눌러야 token을 발급한다.
- Android REST job은 `D:\Claude4Net\AndroidWork\` 아래에서만 작업한다.
- 실제 코드 수정, 빌드, 테스트, release gate, commit, push는 서버에서 수행한다.
- Android 앱은 작업 생성, 상태 관찰, diff 확인, 승인/거절, 완료 결과 확인을 담당한다.
- commit은 서버가 검증 통과 후 자동 수행한다.
- push는 Android 앱에서 명시 승인한 뒤에만 수행한다.
- MVP에서는 Android REST job을 동시에 하나만 실행한다.
- REST polling은 15fps를 목표로 하되, 전체 데이터를 매번 받지 않고 sequence 기반 delta frame만 받는다.

## 3. 운영 디렉터리 구조

운영 서버 배포 루트는 다음과 같이 둔다.

```text
D:\
  Claude4Net\
    Claude4Net.Cli.exe
    Claude4Net.Dashboard.dll
    appsettings.json
    plugins\
    .resources\

    AndroidWork\
      config\
      repos\
      jobs\
      devices\
      temp\
      archive\
```

작업별 디렉터리는 다음 구조를 권장한다.

```text
D:\Claude4Net\AndroidWork\
  jobs\
    job_20260529_0001\
      request.json
      state.json
      workspace\
        repo\
      logs\
        agent.log
        build.log
        test.log
        verify-release.log
      artifacts\
        diff.patch
        changed-files.json
        verification.json
      result.json
```

`D:\Claude4Net\AndroidWork`는 작업 데이터 루트이며, `D:\Claude4Net` 전체가 workspace가 되어서는 안 된다.

## 4. 두 실행 세계 분리

### 4.1 Local Interactive World

대상:

- Terminal
- Discord

특징:

- 기존 `AppState.CurrentCwd`를 기준으로 같은 작업 디렉터리를 본다.
- 기존 CLI/Discord approval handler를 사용한다.
- 기존 `AgentLoop`, `ToolOrchestrator`, `CommandRegistry` 흐름을 최대한 유지한다.
- Terminal과 Discord는 같은 local session/workspace 계열로 취급한다.

```text
Terminal / Discord
  -> Existing broker/input path
  -> AppState.CurrentCwd
  -> AgentLoop
  -> Local tools
```

### 4.2 Android REST Job World

대상:

- Android app
- REST API server
- AndroidWork job worker

특징:

- `D:\Claude4Net\AndroidWork\jobs\{jobId}\workspace\repo`를 workspace로 사용한다.
- REST job은 local interactive `AppState`를 영구적으로 바꾸면 안 된다.
- 실행 시 `AppState`를 snapshot/capture하고 job context로 임시 설정한 뒤, 종료 시 restore한다.
- MVP에서는 Android REST job을 single active worker queue로 순차 실행한다.

```text
Android App
  -> REST API
  -> AndroidJobQueue
  -> AndroidWork job workspace
  -> Claude4Net AgentLoop
  -> Build/Test/Verify
  -> Commit/Push
  -> REST polling result
```

## 5. 전체 작업 흐름

```text
1. Android App -> POST /api/jobs
   사용자가 작업 요청을 생성한다.

2. REST API Server
   jobId를 발급하고 AndroidWork/jobs/{jobId}를 생성한다.

3. Git Workspace Manager
   remote repository를 fetch/clone/worktree로 준비한다.
   target folder allowlist를 검증한다.

4. Android Job Worker
   AppState snapshot을 저장한다.
   job workspace/session/permission mode를 설정한다.
   Claude4Net AgentLoop를 실행한다.

5. Claude4Net
   필요한 코드 수정과 도구 실행을 수행한다.
   위험 작업은 Android approval 상태로 전환한다.

6. Android App
   15fps REST polling으로 frame을 조회한다.
   diff/risk/approval 요청을 보고 approve/reject/cancel 명령을 보낸다.

7. Server Verification
   job workspace 내부의 scripts\verify-release.ps1를 release gate로 실행한다.

8. Git Commit/Push
   검증 통과 후 서버가 자동 commit한다.
   push는 Android 앱에서 push 승인을 받은 뒤에만 수행한다.

9. Android App
   완료 frame 또는 result API로 branch, commit SHA, push 결과, 검증 요약을 받는다.

10. Cleanup
   완료된 job은 기본적으로 무제한 보존한다.
   Android 앱에서 삭제를 요청하면 서버의 job 디렉터리와 metadata도 삭제한다.
```

## 6. REST API 설계

### 6.0 API 호스팅 방식

REST API는 별도 `Claude4Net.ApiServer` 프로젝트로 분리하지 않는다. 기존 `Claude4Net.Dashboard`가 이미 ASP.NET Core host, SignalR hub, dashboard client hosting을 담당하고 있으므로 Android REST API도 같은 web host에 올린다.

실행 예시:

```powershell
Claude4Net.Cli.exe --dashboard --api true
```

단축형도 같은 의미로 지원한다.

```powershell
Claude4Net.Cli.exe --dashboard --api
```

명시적으로 끄는 형식도 지원한다.

```powershell
Claude4Net.Cli.exe --dashboard --api false
```

외부 Android 기기 접속을 위해 API host는 운영에서 `0.0.0.0` bind를 허용한다.

```powershell
Claude4Net.Cli.exe --dashboard --api --api-host 0.0.0.0 --api-port 5277
```

Dashboard host 역할:

- Blazor Dashboard host
- 기존 SignalR hubs
- Android REST API endpoints
- Android pairing/auth endpoints
- Android job frame polling endpoints

`--api`가 없으면 Android REST API endpoint는 매핑하지 않는다.

### 6.1 Job API

```http
POST /api/jobs
GET  /api/jobs/{jobId}
GET  /api/jobs/{jobId}/result
POST /api/jobs/{jobId}/cancel
DELETE /api/jobs/{jobId}
```

작업 생성 요청 예시:

```json
{
  "deviceId": "pixel-9-pro",
  "repoUrl": "https://example.com/org/repo.git",
  "baseBranch": "experiment",
  "targetPath": "Claude4Net.Runtime",
  "prompt": "이 범위 안에서 nullable warning을 제거해줘.",
  "permissionMode": "Prompt",
  "allowCommit": true,
  "allowPushRequest": true
}
```

### 6.2 Frame API

Android 앱은 job detail 화면에서 최대 15fps로 frame을 조회한다.

```http
GET /api/jobs/{jobId}/frame?afterSeq=123
```

변경이 있으면:

```json
{
  "seq": 124,
  "serverTime": "2026-05-29T12:00:00Z",
  "jobStatus": "RunningTests",
  "agentStatus": "Waiting",
  "phase": "dotnet test",
  "progress": 0.72,
  "latestMessage": "287 tests passed",
  "pendingApproval": null,
  "changedFiles": [
    "Claude4Net.Runtime/ToolOrchestrator.cs"
  ],
  "verification": {
    "build": "Passed",
    "tests": "Running",
    "releaseGate": "Pending"
  }
}
```

변경이 없으면:

```http
204 No Content
```

### 6.3 Command API

```http
POST /api/jobs/{jobId}/commands
```

명령 예시:

```json
{
  "commandId": "cmd_00018",
  "type": "Approve",
  "approvalId": "approval_42"
}
```

지원 명령:

- `Approve`
- `Reject`
- `Cancel`
- `Pause`
- `Resume`
- `RequestDiff`
- `AllowCommit`
- `ApprovePush`

모든 command는 idempotent 해야 한다. 같은 `commandId`가 재전송되면 서버는 같은 결과를 반환해야 한다.

### 6.4 Logs and Diff API

frame에는 큰 로그나 diff 전문을 넣지 않는다.

```http
GET /api/jobs/{jobId}/logs?afterSeq=500&limit=100
GET /api/jobs/{jobId}/diff
GET /api/jobs/{jobId}/artifacts/changed-files
GET /api/jobs/{jobId}/artifacts/verification
```

## 7. 페어링 인증 설계

외부 노출은 허용하되, Android 앱은 기본적으로 10자리 페어링 코드로 인증한다. 계정 시스템은 MVP 범위에서 제외한다.

REST API 서버와 Android 앱이 같은 네트워크에 있으면 10자리 코드 없이 LAN 승인 인증 경로를 사용할 수 있다. 이 경로는 기본 활성화한다.

LAN 승인 인증은 완전 자동 발급이 아니다. 서버가 같은 네트워크 요청으로 판정한 뒤, 서버 터미널에 승인 요청을 표시하고 운영자가 `Y`를 눌러야 3일 유효 access token을 발급한다.

### 7.1 인증 흐름

```text
1. Android App에서 서버 연결을 누른다.
2. Android App -> POST /api/pairing/request
3. 서버 터미널에 10자리 인증 번호가 표시된다.
4. 사용자가 Android App에 인증 번호를 입력한다.
5. Android App -> POST /api/pairing/confirm
6. 서버가 3일 유효 access token을 발급한다.
7. Android App은 이후 REST API 요청에 Bearer token을 포함한다.
```

터미널 출력 예시:

```text
[Android Pairing]
Device requested access: Pixel 9 Pro
Pairing code: 4829137056
Expires in: 30 seconds
```

### 7.2 Pairing API

```http
POST /api/pairing/request
POST /api/pairing/confirm
POST /api/pairing/revoke
GET  /api/auth/session
```

`request` 예시:

```json
{
  "deviceName": "Pixel 9 Pro",
  "appInstanceId": "android-install-uuid"
}
```

`confirm` 예시:

```json
{
  "pairingId": "pair_123",
  "code": "4829137056"
}
```

성공 응답:

```json
{
  "accessToken": "c4n_at_xxx",
  "expiresAt": "2026-06-01T12:00:00Z",
  "deviceId": "pixel-9-pro",
  "scopes": ["jobs:create", "jobs:read", "jobs:approve", "jobs:cancel"]
}
```

이후 요청:

```http
Authorization: Bearer c4n_at_xxx
```

### 7.3 인증 정책

- pairing code는 10자리 숫자다.
- pairing code는 `RandomNumberGenerator`로 생성한다.
- pairing code 유효기간은 30초로 둔다.
- pairing code는 일회용이다.
- 인증 시도는 최대 5회로 제한한다.
- 성공, 만료, 실패 초과 시 pairing request를 폐기한다.
- access token은 random 256-bit 이상으로 생성한다.
- access token 유효기간은 3일이다.
- token은 daily sliding expiration 정책을 사용한다.
- token 원문은 서버에 저장하지 않는다.
- Android 앱은 token을 Android Keystore 또는 encrypted storage에 저장한다.

pairing code 유효기간은 서버 터미널에 10자리 코드가 출력된 순간부터 Android 앱이 그 코드를 제출할 수 있는 시간이다.

```text
12:00:00 서버 터미널에 4829137056 출력
12:00:30까지 Android 앱에서 입력 가능
12:00:31부터 만료
```

### 7.4 LAN 승인 인증 흐름

REST API 서버와 Android 앱이 같은 네트워크에 있으면 LAN 승인 인증 경로를 사용할 수 있다.

```text
1. Android App에서 서버 연결을 누른다.
2. Android App -> POST /api/auth/lan
3. 서버가 요청 IP와 서버 NIC 대역을 비교한다.
4. 같은 로컬 네트워크로 판정되면 서버 터미널에 승인 요청을 표시한다.
5. 서버 운영자가 터미널에서 Y를 누른다.
6. 서버가 3일 유효 access token을 발급한다.
7. Android App은 이후 REST API 요청에 Bearer token을 포함한다.
```

터미널 출력 예시:

```text
[Android LAN Auth]
Device requested access: Pixel 9 Pro
Client IP: 192.168.0.42
Same network: yes
Approve this device? [Y/N] (10 seconds)
```

LAN 승인 인증 API:

```http
POST /api/auth/lan
```

요청 예시:

```json
{
  "deviceName": "Pixel 9 Pro",
  "appInstanceId": "android-install-uuid"
}
```

서버 터미널에서 `Y`를 누르면 성공 응답은 pairing confirm과 동일한 token response를 사용한다. `N`을 누르거나 timeout되면 인증을 거절한다.

LAN 승인 인증 조건:

- 요청 IP가 private/local range여야 한다.
- 요청 IP가 서버 NIC 중 하나와 같은 subnet에 있어야 한다.
- 서버 터미널에서 운영자가 `Y`를 눌러야 한다.
- 서버 터미널 승인 timeout은 10초다.
- 서버가 reverse proxy 뒤에 있으면 trusted proxy 설정이 있어야 한다.
- `X-Forwarded-For`는 trusted proxy가 아니면 신뢰하지 않는다.
- token 유효기간은 pairing token과 동일하게 3일이다.
- token은 `AuthMethod = "LanApproved"` metadata를 가진다.
- LAN 승인 인증 실패 시 Android 앱은 10자리 pairing code 흐름으로 fallback한다.

인증 선택지는 단순하게 유지한다.

- 같은 네트워크 요청: 서버 터미널에서 `Y`를 눌러 승인
- 그 외 요청 또는 LAN 승인 실패: Android 앱에 10자리 pairing code 입력

## 8. TeruTeruPandas 인증 토큰 저장소

Android pairing request와 auth token은 `TeruTeruPandas` 기반 table로 관리한다.

원칙:

- pairing code 원문 저장 금지
- access token 원문 저장 금지
- `CodeHash`, `TokenHash`만 저장
- hash는 가능하면 `HMAC-SHA256` 사용
- HMAC key는 DB가 아니라 server config 또는 environment에 저장
- 만료된 pairing/token은 cleanup job으로 정리

### 8.1 `android_pairing_requests`

| Column | Type | 설명 |
|---|---|---|
| `PairingId` | string | pairing request ID |
| `DeviceName` | string | Android 기기 이름 |
| `AppInstanceId` | string | 앱 설치 인스턴스 ID |
| `CodeHash` | string | 10자리 인증 번호 hash |
| `CreatedAt` | DateTime | 생성 시각 |
| `ExpiresAt` | DateTime | 만료 시각 |
| `AttemptCount` | int | 인증 시도 횟수 |
| `Status` | string | `Pending`, `Confirmed`, `Expired`, `Failed` |

### 8.2 `android_auth_tokens`

| Column | Type | 설명 |
|---|---|---|
| `TokenId` | string | token ID |
| `DeviceName` | string | Android 기기 이름 |
| `AppInstanceId` | string | 앱 설치 인스턴스 ID |
| `TokenHash` | string | access token hash |
| `Scopes` | string | JSON array 문자열 |
| `AuthMethod` | string | `PairingCode` 또는 `LanApproved` |
| `ClientIp` | string | 최초 발급 요청 IP |
| `CreatedAt` | DateTime | 생성 시각 |
| `ExpiresAt` | DateTime | 3일 뒤 만료 시각 |
| `RevokedAt` | DateTime? | revoke 시각 |
| `LastUsedAt` | DateTime? | 마지막 사용 시각 |
| `LastExtendedAt` | DateTime? | 마지막 자동 연장 시각 |
| `RefreshEligibleAt` | DateTime | 다음 자동 연장이 가능한 시각 |

권장 MVP scope:

```json
[
  "jobs:create",
  "jobs:read",
  "jobs:approve",
  "jobs:cancel"
]
```

`jobs:commit`은 MVP에서 Android 앱 scope로 직접 제공하지 않는다. commit은 서버 job policy가 검증 통과 후 자동 수행한다.

`jobs:push`는 Android 앱의 push 승인 scope로만 사용한다. Android 앱은 push를 직접 수행하지 않고 서버에 `ApprovePush` 명령만 보낸다.

### 8.3 Token refresh 정책

Token refresh는 daily sliding expiration 방식을 사용한다.

규칙:

- token 최초 발급 시 `ExpiresAt = CreatedAt + 3 days`
- token 최초 발급 시 `RefreshEligibleAt = CreatedAt + 1 day`
- `RefreshEligibleAt` 이전에 성공한 API 호출은 token을 연장하지 않는다.
- `RefreshEligibleAt` 이후 인증이 필요한 API 호출이 성공하면 token을 자동 연장한다.
- 자동 연장 시 `ExpiresAt = now + 3 days`
- 자동 연장 시 `LastExtendedAt = now`
- 자동 연장 시 `RefreshEligibleAt = now + 1 day`
- 만료되었거나 revoke된 token은 연장하지 않는다.

예시:

```text
5월 29일 10:00 발급
ExpiresAt = 6월 1일 10:00
RefreshEligibleAt = 5월 30일 10:00

5월 29일 22:00 API 호출 성공
-> 아직 RefreshEligibleAt 이전이므로 연장 없음

5월 30일 11:00 API 호출 성공
-> ExpiresAt = 6월 2일 11:00
-> LastExtendedAt = 5월 30일 11:00
-> RefreshEligibleAt = 5월 31일 11:00
```

이 정책은 15fps polling 환경에서 매 frame마다 DB를 갱신하지 않으면서, 실제 사용 중인 Android 앱의 인증 상태를 자연스럽게 유지하기 위한 것이다.

## 9. Job 상태 모델

```text
Queued
PreparingWorkspace
SyncingGit
RunningAgent
WaitingForApproval
ApplyingChanges
RunningBuild
RunningTests
RunningReleaseGate
ReadyToCommit
Committing
Pushing
Completed
Failed
Cancelled
Deleted
```

상태 전이는 서버가 authoritative하게 관리한다. Android 앱은 상태를 표시하고 command를 보내는 역할만 한다.

완료된 job은 자동 삭제하지 않는다. Android 앱에서 삭제를 누르면 `DELETE /api/jobs/{jobId}`를 호출하고, 서버는 해당 job의 workspace, logs, artifacts, metadata를 삭제한다. 삭제된 job은 일반 목록에서 제외하고, 필요하면 audit 수준의 최소 tombstone만 남긴다.

## 10. Git 작업 정책

서버는 Android job마다 격리된 git 작업 공간을 만든다.

권장 방식:

- `repos\` 아래 bare mirror 또는 shared cache 유지
- `jobs\{jobId}\workspace\repo` 아래 worktree 생성
- job branch는 서버가 생성
- branch 이름 예시: `android/job-20260529-0001`

commit 조건:

- 변경 파일이 허용된 target path 아래에만 있어야 한다.
- forbidden path 변경이 없어야 한다.
- secret scan이 통과해야 한다.
- build/test/release gate가 통과해야 한다.
- Android approval이 필요한 변경은 승인되어야 한다.
- commit message와 result metadata가 생성되어야 한다.
- 위 조건을 만족하면 서버가 자동으로 commit한다.

push 조건:

- commit이 성공해야 한다.
- Android 앱에서 `ApprovePush` 명령을 보내야 한다.
- push 대상 remote/branch가 job metadata와 일치해야 한다.
- push 직전 변경 상태가 commit 이후 변하지 않았음을 확인해야 한다.

실패 시:

- commit 조건 실패 시 commit/push하지 않는다.
- commit 성공 후 push 승인 전 실패 또는 취소가 발생하면 push하지 않는다.
- `result.json`에 실패 단계, 로그 위치, 검증 요약, 남은 diff를 기록한다.
- Android 앱은 실패 frame/result를 받는다.

## 11. AppState 격리 정책

현재 Claude4Net에는 `AppState.CurrentCwd`, `AppState.SessionId`, `AppState.ActiveProvider`, `AppState.ActiveModel`, `AppState.CurrentPermissionMode` 같은 전역 상태가 있다.

MVP에서는 REST job을 실행할 때 다음 방식을 사용한다.

```csharp
var snapshot = AppStateSnapshot.Capture();

try
{
    AppState.CurrentCwd = job.WorkspacePath;
    AppState.SessionId = job.SessionId;
    AppState.CurrentPermissionMode = job.PermissionMode;
    await agentLoop.RunAsync(job.Input);
}
finally
{
    snapshot.Restore();
}
```

REST job은 순차 실행한다. 이 방식은 전역 상태 충돌을 줄이는 단기 방어책이다.

장기적으로는 `AgentRunContext`를 도입한다.

```text
AgentRunContext
  JobId
  Channel
  WorkspaceRoot
  SessionId
  PermissionMode
  Provider
  ApprovalHandler
  OutputHandler
  ToolExecutor
  GitBranch
```

## 12. Approval 구조

Android REST job의 승인 흐름:

```text
ToolOrchestrator
  -> PermissionEnforcer
  -> RequireApproval
  -> AndroidApprovalRequest
  -> frame.pendingApproval
  -> Android App approve/reject
  -> command API
  -> ToolOrchestrator resumes
```

승인 요청에는 다음 정보가 포함되어야 한다.

- `approvalId`
- `jobId`
- `toolName`
- `riskLevel`
- `summary`
- `affectedPaths`
- `diffAvailable`
- `createdAt`
- `expiresAt`

approval handler가 없거나 timeout되면 기본 deny 처리한다.

## 13. Android 앱 UI 방향

Android 앱은 Kotlin + Jetpack Compose + Material 3를 권장한다.

주요 화면:

- Job list
- Job detail live frame
- Chat/request creation
- Approval queue
- Diff viewer
- Logs viewer
- Verification result
- Git result
- Settings

15fps frame polling은 foreground job detail 화면에서만 수행한다.

권장 polling 정책:

- foreground detail 화면: 최대 15fps
- list 화면: 1-2fps
- background: 중지 또는 매우 낮은 빈도
- approval required/completed/failed: notification 사용

## 14. 보안 정책

필수 규칙:

- `AndroidWork` 밖 path escape 금지
- `D:\Claude4Net\plugins`, `.resources`, 실행 파일, config는 job이 수정할 수 없음
- Android REST API는 외부 bind를 허용할 수 있지만, write/control endpoint는 Bearer token 인증을 요구함
- 같은 네트워크 LAN 승인 인증은 `/api/auth/lan` 경로에서만 수행함
- LAN 승인 인증은 private/local subnet 판정과 서버 터미널 `Y` 승인이 모두 성공한 경우에만 token을 발급함
- trusted proxy가 아닌 `X-Forwarded-For`는 신뢰하지 않음
- pairing code와 access token 원문은 로그와 DB에 저장하지 않음
- symlink, junction, reparse point escape 차단
- Android 입력값을 파일 경로로 직접 사용하지 않음
- jobId/deviceId/repo alias는 서버에서 정규화
- logs와 frame에는 secret/token/password를 마스킹
- commit/push 전 변경 파일 allowlist 검사
- `bash` 또는 shell execution은 서버에서만 수행
- Android 앱은 직접 shell capability를 갖지 않음
- release gate는 반드시 job workspace 내부의 `scripts\verify-release.ps1`를 실행함
- host 배포 루트의 scripts를 job 검증에 재사용하지 않음

## 15. MVP 범위

MVP에 포함:

- `D:\Claude4Net\AndroidWork` root 설정
- `--dashboard --api` startup option
- Dashboard host 내 Android REST API endpoint 매핑
- 10자리 pairing code 인증
- 같은 네트워크 LAN 승인 인증
- 3일 유효 token 발급
- daily sliding expiration 기반 token 자동 연장
- TeruTeruPandas 기반 pairing/token table
- REST job 생성 API
- job frame polling API
- command API: approve/reject/cancel
- logs/diff 조회 API
- Android job queue single worker
- git workspace 준비
- Claude4Net 실행
- job workspace 내부 `scripts\verify-release.ps1` 실행
- 검증 통과 후 자동 commit
- Android push 승인 후 push
- Android 앱 기본 화면: job list, detail, approval, diff, result

MVP에서 제외:

- REST job 병렬 실행
- Android 로컬 파일 workspace 직접 수정
- Android shell execution
- full multi-tenant AppState 제거
- WebSocket/SignalR streaming
- 복잡한 branch conflict 자동 해결
- 계정 기반 로그인 시스템

## 16. 향후 확장

- `AgentRunContext` 기반으로 전역 `AppState` 의존 축소
- REST job 병렬 실행
- repo/branch/project lock manager
- WebSocket 또는 SignalR optional transport
- Android push notification 고도화
- device별 권한 정책
- server-side job replay
- job archive and cleanup dashboard
- device별 token revoke UI

## 17. 남은 설계 질문

1. 자동 commit 이후 push 승인 timeout 정책을 정해야 한다.
2. job branch naming 규칙과 remote push target을 정해야 한다.
3. release gate 실패 시 Android 앱에 전체 로그를 어느 범위까지 보여줄지 정해야 한다.
4. 삭제된 job에 대해 최소 tombstone metadata를 남길지 완전 삭제할지 정해야 한다.
5. token revoke를 CLI command, dashboard UI, Android app 중 어디서 제공할지 정해야 한다.
