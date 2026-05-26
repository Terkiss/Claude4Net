# Implementation Plan - Milestone K096: Plan/Dry-Run 모드

Milestone K096 introduces a Dry-Run/Plan mode in Claude4Net. This mode enables developers and agents to simulate the impact of executing file system modifications, state modifications, or terminal commands before they are actually executed, yielding an `ImpactReport` and printing a Spectre.Console visual summary.

## Proposed Changes

### Runtime Simulation Engine (`Claude4Net.Runtime/DryRunEngine.cs`)
- Completed. Houses `IsActive`, simulations of `FileWriteTool`, `FileEditTool`, `FileReadTool`, state modifications (RAG, pandas universe), and shell commands (BashTool).
- Collects `SimulatedToolCall`, `SimulatedFileChange`, and `SimulatedStateChange` into an `ImpactReport`.
- Provides Spectre.Console output via `RenderReport()`.

### CLI Option Parsing & Interactive Slash Commands (`Claude4Net.Commands/CommandRegistry.cs`)
- Added `/plan` command to toggle `DryRunEngine.IsActive`.

### Agent Execution Loop Interception (`Claude4Net.Runtime/AgentLoop.cs`)
- Intercepts batch execution in `RunAsync` to call `DryRunEngine.ExecuteSimulatedBatchAsync` instead of the raw `_orchestrator.ExecuteBatchAsync` if `DryRunEngine.IsActive` is true.
- Clears simulator state at the start of a run (`DryRunEngine.Clear()`).
- Renders the Spectre.Console visual report at the end of the run.

### Verification and Test Suite (`Claude4Net.Tests/K096DryRunTests.cs`)
- Validates virtual write redirection (preventing real disk writes).
- Validates read consistency (subsequent reads from virtual changes return the simulated contents rather than disk contents).

## Verification Results
- All unit and integration tests successfully compiled and passed:
  - 2 new tests in `K096DryRunTests.cs` passed.
  - Full test suite passed (646/646 tests total).
