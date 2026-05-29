# Implementation Plan

## 0. Source Documents
- [ANDROID_REST_API_APP_DESIGN.md](file:///D:/Project/CKP/Test/openclaude/Claude4Net-App/Documents/ANDROID_REST_API_APP_DESIGN.md)
- [ANDROID_UI_DESIGN.md](file:///D:/Project/CKP/Test/openclaude/Claude4Net-App/Documents/ANDROID_UI_DESIGN.md)
- [Mockup Folder](file:///D:/Project/CKP/Test/openclaude/Claude4Net-App/Documents/uiMokup)

## 1. Planning Summary
- **Goal**: Build a remote control and monitoring client-server architecture for Claude4Net. The Android App will feature a chat-centric UI (resembling Google Gemini or KakaoTalk) where the user interacts with the avatar of Terukirdo (the blue-haired maid mascot), monitors task statuses through rich inline cards inside chat bubbles, and executes operations dynamically via interactive buttons.
- **Scope**:
  - **CLI Expansion**: Add `--api [true|false]`, `--api-host`, and `--api-port` arguments to CLI options.
  - **Authentication**: 10-digit random Pairing Code, LAN Auto-approve via subnet detection and terminal prompt input `Y/N`.
  - **Storage**: Database tables `android_pairing_requests` and `android_auth_tokens` using TeruTeruPandas, store token hashes (HMAC-SHA256) instead of raw text.
  - **Job Isolation**: Isolated workspace under `D:\Claude4Net\AndroidWork`, AppState backup/restore context, automated workspace preparation via worktree, sequential single-worker queue.
  - **Verification & Git**: Automatic compilation, testing, and `verify-release.ps1` execution. Automatic commit on success, and push only on Android `ApprovePush` command.
  - **Android Client (Chat-centric UI)**:
    - **Layout Framework**: 9:16 vertical layout using Compose `ModalNavigationDrawer`, `Scaffold`, and `LazyColumn`.
    - **Navigation Drawer**: Opened via a hamburger menu button (≡) on the top-left, featuring a "+ New chat" button and a scrollable history of recent jobs.
    - **Conversation Feed**: Speech bubbles displaying User messages (right-aligned) and Terukirdo messages (left-aligned, accompanied by the blue-haired mascot avatar `terukirdo_profile.png`).
    - **Inline Cards**: Render task phases, builds, tests, and release-gates status within Terukirdo's speech bubbles dynamically.
    - **Interactive Modals/Cards**: Render "Approve" (green) and "Reject" (red) buttons directly inside Terukirdo's bubbles for pending approvals.
    - **Input Control**: Bottom chat text box with a send button to direct the agent.
- **Non-goals**: Multi-tenant concurrent job runs on the server (sequential execution only), WebSocket/SignalR based job log streaming, remote shell access.
- **Current Assumption**: The C# solution is compiled using .NET 10. The Android application target SDK is API 34+ with Kotlin and Material 3.

## 2. Requirements Extracted From Source
### Functional Requirements
- CLI must launch REST API endpoints inside `Claude4Net.Dashboard` when `--api` is set.
- Pairing codes must be 10-digit random numbers, valid for 30 seconds, maximum 5 attempts.
- Tokens must expire in 3 days with a daily sliding expiration extension.
- Job queue must execute tasks sequentially, capturing and restoring the `AppState` global context.
- Target paths inside workspaces must be checked against allowlists to prevent path traversal.
- Server must run `scripts\verify-release.ps1` inside the job's workspace.
- Server must commit automatically on validation success, but push requires Android approval command.
- Android app must poll job frames with `afterSeq` parameter, and display live conversation cards in real-time.
- Android app must show inline actions for approvals and state badges (`[Build]`, `[Tests]`, `[Release Gate]`).

### Non-Functional Requirements
- REST polling target frame rate is 15fps (approx. 66ms delay).
- No delta frames (204 No Content) must be sent if status hasn't changed.
- API endpoints must require Authorization headers with Bearer tokens except pairing.

### Security / Safety Requirements
- No raw pairing codes or tokens in logs or databases; only SHA-256 / HMAC-SHA256 hashes.
- Path escape from `AndroidWork` must be forbidden; block symbolic links or junctions.
- LAN auto-connect must strictly require IP subnet matching and a 10-second timeout terminal prompt.

### UX / UI Requirements
- Fluid Jetpack Compose layout using Material 3 styling. Monospaced font for terminal lines.
- Red/Yellow/Green badges for build/test/release-gate status.
- Secure token storage using Android Keystore/EncryptedSharedPreferences.
- Integrated blue-haired avatar `terukirdo_profile.png` placed next to assistant chat bubbles.
- Slid-out navigation drawer displaying "+ New Chat" and history.

### Data / State Requirements
- Tables: `android_pairing_requests` and `android_auth_tokens` managed through TeruTeruPandas.
- Job status lifecycle: `Queued`, `PreparingWorkspace`, `SyncingGit`, `RunningAgent`, `WaitingForApproval`, `ApplyingChanges`, `RunningBuild`, `RunningTests`, `RunningReleaseGate`, `ReadyToCommit`, `Committing`, `Pushing`, `Completed`, `Failed`, `Cancelled`, `Deleted`.

### Documentation / Release Requirements
- Document REST API contracts and pairing terminal commands.
- Units and integration tests for pairing, delta polling, and job queue.

## 3. Constraints
- **Allowed**: Directory manipulation within `D:\Claude4Net\AndroidWork`, ASP.NET routing inside `Claude4Net.Dashboard`.
- **Forbidden**: Changing files outside of `AndroidWork` from a job worker. Modifying `.agents/` prompts during runtime.
- **Compatibility**: Retain full CLI/Discord interactive world capability while launching the API world.

## 4. Milestone Roadmap
| Milestone | Name | Status | Goal | Evidence Required |
| --- | --- | --- | --- | --- |
| K098 | API Startup & Infrastructure | Proposed | Parse `--api` arguments, configure hosting, isolate AppState snapshot | Unit tests for arguments parsing & AppState snapshot |
| K099 | TeruTeruPandas Auth Database | Proposed | Create pairing/token database schemas, implement HMAC-SHA256 hashing | Database initialization and query test results |
| K100 | Pairing & LAN Auth Endpoints | Proposed | Connect pairing routes, implement LAN Auto-Connect terminal prompt | Pairing validation tests & terminal prompt test |
| K101 | Job Queue & Isolated Execution | Proposed | Build the Single-Worker Job queue, spawn workspaces, wrap AppState | Worktree setup checks & job runner build verification |
| K102 | Live Frame Delta API & Commands | Proposed | Implement Delta Polling (seq tracking) and command processor | Polling response tests (200/204) & Command idempotency test |
| K103 | Android App Bootstrap & Auth UI | Proposed | Set up Compose project, Retrofit, EncryptedSharedPreferences, Auth UI | Auth flow token retrieval log |
| K104 | Android Chat Feed & Side Drawer | Proposed | Build the main 9:16 layout, navigation drawer with '+ New chat', and scrollable list showing history and input bar | Compile layout and verify drawer animation |
| K105 | Interactive Bubbles & Avatar Integration | Proposed | Map polling frames to chat bubble updates, show Terukirdo's blue-haired avatar, embed inline verification cards and Approve/Reject buttons | Rendered UI screenshot in 9:16 aspect ratio |
| K106 | End-to-End Release Validation | Proposed | Connect app and server E2E, check release gates | Complete pass of `verify-release.ps1` |

## 5. Execution Cards

### K098 API Startup & Infrastructure
Status: Proposed
Dependency: None

Goal:
- Support CLI options for dashboard API hosting and verify `AppStateSnapshot` utility.

Allowed files:
- `Claude4Net.Cli/Program.cs`
- `Claude4Net.Cli/CliOptions.cs`
- `Claude4Net.Dashboard/Startup.cs`
- `Claude4Net.Runtime/AppState.cs`
- `Claude4Net.Runtime/AppStateSnapshot.cs`

Forbidden files:
- `.agents/*`

Required work:
- Register arguments: `--api [true/false]`, `--api-host`, `--api-port` in `CliOptions.cs`.
- Create `AppStateSnapshot` to capture and restore: `CurrentCwd`, `SessionId`, `ActiveProvider`, `ActiveModel`, and `CurrentPermissionMode`.
- Conditionally bind WebHost to `0.0.0.0` or specified host when `--api` is set.

Required tests:
- Parse argument combinations and assert dashboard options.
- Test `AppStateSnapshot.Capture()` and `Restore()` behavior under mutational changes.

Verification commands:
```powershell
dotnet build -p:UseAppHost=false
dotnet test --filter "Category=K098"
```

---

### K099 TeruTeruPandas Auth Database
Status: Proposed
Dependency: K098

Goal:
- Set up database tables for pairing requests and authentication tokens under the pandas engine.

Allowed files:
- `Claude4Net.Runtime/Storage/PandasUniverseManager.cs`
- `Claude4Net.Runtime/Storage/AuthDatabase.cs`
- `Claude4Net.Runtime/Security/Cryptography.cs`

Required work:
- Define `android_pairing_requests` schema with fields: `PairingId`, `DeviceName`, `AppInstanceId`, `CodeHash`, `CreatedAt`, `ExpiresAt`, `AttemptCount`, `Status`.
- Define `android_auth_tokens` schema with fields: `TokenId`, `DeviceName`, `AppInstanceId`, `TokenHash`, `Scopes`, `AuthMethod`, `ClientIp`, `CreatedAt`, `ExpiresAt`, `LastUsedAt`, `LastExtendedAt`, `RefreshEligibleAt`.
- Implement `HMAC-SHA256` hashing for codes/tokens using a server secret.

Required tests:
- Database schema migration verification.
- CRUD test suites for pairing requests and token lifecycle.

Verification commands:
```powershell
dotnet test --filter "Category=K099"
```

---

### K100 Pairing & LAN Auth Endpoints
Status: Proposed
Dependency: K099

Goal:
- Add endpoints for authentication and implement LAN auto-connect terminal dialog prompt.

Allowed files:
- `Claude4Net.Dashboard/Controllers/AuthController.cs`
- `Claude4Net.Dashboard/Controllers/PairingController.cs`
- `Claude4Net.Runtime/Security/PairingManager.cs`

Required work:
- API endpoint `POST /api/pairing/request`: Generate 10-digit PIN, print on host console.
- API endpoint `POST /api/pairing/confirm`: Hash PIN, verify, issue JWT-like access token.
- API endpoint `POST /api/auth/lan`: Verify client IP is private/same subnet. Display console prompt `[Y/N]` for 10 seconds. Issue token if `Y` is chosen.

Required tests:
- Pairing code timeout verification.
- Mock network comparison matching and console approval mocking tests.

Verification commands:
```powershell
dotnet test --filter "Category=K100"
```

---

### K101 Job Queue & Isolated Execution
Status: Proposed
Dependency: K098

Goal:
- Implement sequential job processing and sandboxed file executing environments.

Allowed files:
- `Claude4Net.Runtime/Jobs/JobQueue.cs`
- `Claude4Net.Runtime/Jobs/JobWorker.cs`
- `Claude4Net.Runtime/Jobs/GitWorkspaceManager.cs`

Required work:
- Implement `JobQueue` (FIFO Queue, Single thread).
- `GitWorkspaceManager` should generate a git worktree of the repository inside `D:\Claude4Net\AndroidWork\jobs\{jobId}\workspace\repo`.
- Run jobs by capturing `AppState`, replacing workspace Cwd, calling `AgentLoop`, then restoring original `AppState`.
- Auto-run compilation, tests, and `verify-release.ps1` inside job workspace on code modification.

Required tests:
- Sequential run ordering tests.
- Path traversal block verification tests.

Verification commands:
```powershell
dotnet test --filter "Category=K101"
```

---

### K102 Live Frame Delta API & Commands
Status: Proposed
Dependency: K101

Goal:
- Add endpoints for tracking job execution metrics and managing commands.

Allowed files:
- `Claude4Net.Dashboard/Controllers/JobController.cs`
- `Claude4Net.Runtime/Jobs/JobStateTracker.cs`

Required work:
- Live frame delta API `GET /api/jobs/{jobId}/frame?afterSeq=123`.
- Track progress, phase, latestMessage, pendingApproval, changedFiles, verification state.
- Return `204 No Content` if sequence matches (no changes).
- Commands endpoint `POST /api/jobs/{jobId}/commands` to approve tools, cancel job, or approve git push.

Required tests:
- Delta framing sequence validation.
- Command idempotency with same `commandId`.

Verification commands:
```powershell
dotnet test --filter "Category=K102"
```

---

### K103 Android App Bootstrap & Auth UI
Status: Proposed
Dependency: K100

Goal:
- Bootstrap Jetpack Compose app, implement secure storage, and create Auth & Pairing screen.

Allowed files:
- `android/` directory (New Gradle + Compose project)

Required work:
- Create Jetpack Compose Kotlin application.
- Set up Retrofit client with `AuthInterceptor` retrieving token from `EncryptedSharedPreferences`.
- Implement Pairing PIN input page and LAN auto-connect loading spinner.

Required tests:
- Retrofit authorization header injection unit tests.
- UI Compose preview and behavior testing.

Verification commands:
- Gradle build: `./gradlew assembleDebug` (run within android directory)

---

### K104 Android Chat Feed & Side Drawer
Status: Proposed
Dependency: K103, K102

Goal:
- Build the 9:16 layout scaffolding with a hamburger-triggered sliding drawer.

Allowed files:
- `android/` directory

Required work:
- Implement `ModalNavigationDrawer` containing a header, a `+ New Chat` button, and list items showing previous job runs.
- Construct the base `Scaffold` with a top bar including a hamburger menu icon (≡).
- Render `LazyColumn` for message list history and include the bottom messaging input row.

Required tests:
- Drawer open state flow UI tests.
- Layout height limits validation.

Verification commands:
- Gradle check: `./gradlew test`

---

### K105 Interactive Bubbles & Avatar Integration
Status: Proposed
Dependency: K104

Goal:
- Bind live frames to chat messaging flow, showing Terukirdo's blue-haired avatar and inline control cards.

Allowed files:
- `android/` directory
- Image resources: `android/app/src/main/res/drawable/terukirdo_profile.png`

Required work:
- Place the blue-haired maid avatar `terukirdo_profile.png` as leading elements next to Terukirdo's speech bubbles.
- Structure speech bubbles: User bubble (right-aligned), Assistant bubble (left-aligned).
- Implement interactive sub-cards inside assistant bubbles: status badges (`[Build]`, `[Tests]`, `[Release Gate]`) and action buttons (Approve/Reject) triggered by `pendingApproval` signals.

Required tests:
- Dynamic list update performance tests under 15fps polling.
- Button click callback validation tests.

Verification commands:
- Gradle check: `./gradlew test`

---

### K106 End-to-End Release Validation
Status: Proposed
Dependency: K105

Goal:
- Verify complete E2E flow and execute official release script validation.

Allowed files:
- `D:\Project\CKP\Test\openclaude\Claude4Net-App\Documents\Implementation_Plan.md`
- `D:\Project\CKP\Test\openclaude\Claude4Net-App\IMPLEMENTATION_PROGRESS.md`

Required work:
- Perform final integration test running Android requests against C# Dashboard.
- Clean working directory, run `.\scripts\verify-release.ps1`.
- Complete all documentation updates.

Verification commands:
```powershell
git status --short --branch
.\scripts\verify-release.ps1
```

---

## 6. Review / Final-Control Checklist
- [ ] No credential or secret leak in databases or logging.
- [ ] AppState is perfectly cleaned up and restored after job completion or failure.
- [ ] All workspaces are confined inside the parent `AndroidWork` folder.
- [ ] The android build is fully automated via gradle wrapper.
- [ ] UI features Terukirdo's custom blue-haired maid profile avatar and a slide-out drawer matching mockups.

## 7. Open Questions
- What is the remote repository mapping strategy? Should mirror repository clones be auto-created on host start?
- In case of a git conflict during auto-commit, should we automatically reject the job?

## 8. Next Suggested Action
- Request user review of the `Documents/Implementation_Plan.md`. Once approved, start working on **K098**.
