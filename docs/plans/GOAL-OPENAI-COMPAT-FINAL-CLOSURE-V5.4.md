# Terukirdo Plan: GOAL-OPENAI-COMPAT-FINAL-CLOSURE-V5.4

## 1. Execution Scope
The objective is to achieve complete Level-4 external black-box verification for Claude4Net's declared OpenAI-compatible API surface under Terukirdo Protocol v5.4 (Tier 3 Gate).
This plan focuses exclusively on:
1. **P1 (`F-C`)**: Clarify `/v1/responses` as `NOT IMPLEMENTED` (outside currently declared scope) and remove any lingering claims of full OpenAI API surface.
2. **P2 (`F-A`)**: Implement Level-4 Black-box test harness using the official OpenAI .NET SDK (`OpenAI` NuGet package v2.13.0).
3. **P3 (`F-B`)**: Implement Level-4 Black-box test harness using the official OpenAI JavaScript/TypeScript SDK (`openai` npm package v7.4.0) running under Node.js.

## 2. Affected Files
- `Claude4Net.Tests/Claude4Net.Tests.csproj` (added PackageReference `OpenAI` 2.13.0)
- `Claude4Net.Tests/OfficialOpenAiDotNetSdkBlackBoxTests.cs` (new .NET SDK L4 test fixture)
- `Claude4Net.Tests/blackbox_openai_js_sdk_runner.mjs` (new Node.js JavaScript SDK L4 runner)
- `Claude4Net.Tests/OfficialOpenAiJsSdkBlackBoxIntegrationTests.cs` (new Node.js integration runner)
- `MEMORY.md` & `docs/Terukirdo_Trajectory.txt` (documentation & memory sync)

## 3. Required Test Environment & SDK Packages
- .NET 10.0 runtime / SDK
- Official OpenAI .NET SDK (`OpenAI` v2.13.0)
- Official OpenAI Python SDK (`openai` v2.24.0 in Hermes venv)
- Official OpenAI JavaScript SDK (`openai` v7.4.0 under Node v24.16.0)

## 4. Expected Write Operations
- Create `Claude4Net.Tests/OfficialOpenAiDotNetSdkBlackBoxTests.cs`
- Create `Claude4Net.Tests/blackbox_openai_js_sdk_runner.mjs`
- Create `Claude4Net.Tests/OfficialOpenAiJsSdkBlackBoxIntegrationTests.cs`
- Update memory & trajectory files

## 5. Risk Points & Mitigation
- **Risk 1 (Port conflict during parallel test runs)**: Mitigated by `GetAvailablePort()` binding to loopback socket `0` and using `[Collection("AppState")]`.
- **Risk 2 (Surrogate pair splitting or replacement character in JS SDK stream)**: Mitigated by the chunk-level string slicing parser in `IncrementalToolCallParser` and `IncrementalReasoningParser`.
- **Risk 3 (Over-claiming API compatibility)**: Mitigated by explicit statement that `/v1/responses` is `NOT IMPLEMENTED` and claiming `NEAR-FULL` only within declared chat completions, models, and embeddings scope.

## 6. Rollback Point
- Git commit HEAD before this goal.

## 7. Acceptance Criteria
- `F-A` (Official .NET SDK L4): Models, Chat Non-streaming, Chat Streaming with Usage, Tool Calls Streaming, Reasoning, Embeddings, Errors (401/404/400) all PASS.
- `F-B` (Official JS SDK L4): Node.js `openai` client executes all endpoints over real HTTP Kestrel server and all PASS.
- `F-C` (`/v1/responses` classification): Accurately documented as `NOT IMPLEMENTED`.
- Full regression suite (740+ tests): 100% PASS, 0 failures, 0 new warnings, 0 build errors.
- First Reviewer: `CLOSED` for `F-A`, `F-B`, `F-C`.
- Universal Final Controller: `FINALCONTROL PASSED`.
- Final Approach Control: `APPROVED FOR COMMIT ONLY`.
