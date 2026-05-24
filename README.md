# Claude4Net

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![Release Gate](https://img.shields.io/badge/release%20gate-613%2F613%20passing-brightgreen)](./scripts/verify-release.ps1)
[![Branch](https://img.shields.io/badge/branch-claude4net__Rpa-blue)](#)

Claude4Net is a .NET 10 local agent runtime for coding, workspace automation, tool execution, verification, and multi-agent orchestration.

It combines a CLI agent loop, Lumen terminal UI, provider routing, local tools, memory/RAG, checkpointing, routines, skills, a Blazor dashboard, Discord integration, and an Antigravity/Ralph agent workflow.

Current verified baseline:

- Latest tracked release-gate evidence: 613/613 unit and integration tests passing
- Latest warning cleanup commit on the base experiment line: `185db17`
- Current RPA/orchestration branch: `claude4net_Rpa`
- Active implementation SSOT: `Documents/Implementation_Plan.md`

## What It Does

Claude4Net is designed to act like an execution-capable local development agent.

Core capabilities:

- Reads, writes, edits, and lists workspace files through guarded tools
- Runs shell commands through permission-aware execution paths
- Routes requests across Claude, Gemini, Gemini CLI, Ollama, and compatible providers
- Maintains agent memory and local data tables through TeruTeruPandas
- Records sessions, trajectories, checkpoints, and verification evidence
- Supports workspace/session isolation and checkpoint restore flows
- Provides slash commands for spec gates, coordinates, routines, skills, providers, verification, and diagnostics
- Offers a modern interactive terminal UI through Lumen
- Exposes typed dashboard read/control surfaces through Blazor and SignalR
- Supports Discord-based async orchestration and approval paths
- Uses Ralph Loop agent prompts for worker, reviewer, final-control, and planning workflows

## Project Layout

```text
Claude4Net-App/
  Claude4Net.Api/              LLM provider clients, MCP/LSP transports
  Claude4Net.Cli/              Console entrypoint, options, Lumen TUI
  Claude4Net.Commands/         Slash/bang command registry
  Claude4Net.Dashboard/        ASP.NET Core host and SignalR hubs
  Claude4Net.Dashboard.Client/ Blazor WebAssembly dashboard UI
  Claude4Net.Discord/          Discord listener and approval integration
  Claude4Net.MyPlugins/        Built-in local plugins and Pandas tools
  Claude4Net.Runtime/          Agent loop, routing, checkpoints, routines, skills
  Claude4Net.SDK/              Shared models, events, AppState, contracts
  Claude4Net.Tests/            Unit and integration tests
  Claude4Net.Tools/            File, shell, LSP, and system tools
  TeruTeruPandas/              DataFrame and local data universe engine
  .gemini/agents/              Antigravity/Ralph agent prompt definitions
  Documents/                   SSOT, implementation history, system prompts
  scripts/                     Release gate and validation scripts
```

## Major Features

### Agent Runtime

- `AgentLoop` coordinates provider calls, tool calls, streaming, tool results, events, and completion.
- `ToolOrchestrator` centralizes tool execution.
- `PermissionEnforcer` and path-safety checks prevent unsafe filesystem and shell operations.
- `CheckpointStore` protects file and memory state around risky changes.

### Provider Routing

- Provider descriptors are loaded through `ProviderRegistry`.
- Smart routing supports local and remote providers.
- Provider settings precedence is handled through built-in, system, user, workspace, environment, and CLI layers.
- Provider factory preparation is in place for safer provider construction.

### Lumen CLI UI

Lumen is the interactive terminal UI path.

Run with:

```powershell
dotnet run --project Claude4Net.Cli -- --lumen
```

Useful modes:

```powershell
dotnet run --project Claude4Net.Cli -- --legacy-cli
dotnet run --project Claude4Net.Cli -- --smoke-exit
dotnet run --project Claude4Net.Cli -- doctor --output-format json
```

Lumen includes:

- Fixed transcript/input/footer rendering
- CJK-aware text width handling
- Slash command palette
- Scroll navigation
- Approval dialog frame rendering
- Bottom-pane anchoring fixes

### Dashboard

Run with:

```powershell
dotnet run --project Claude4Net.Cli -- --dashboard
```

Dashboard capabilities:

- Provider state read models
- Coordinate/spec state read models
- Skill and routine read models
- Checkpoint and verification views
- Typed safe control actions
- Arbitrary remote command execution remains denied

### Specs, Coordinates, Routines, And Skills

Implemented surfaces include:

- `/spec` command surface for structured acceptance criteria
- `/coordinate` enforcement and evidence tracking
- `/routine` management and permission-aware execution
- Routine scheduler hardening
- Skill proposal lifecycle
- Skill apply engine
- Skill trajectory mining
- Global/local skill store separation

K087 separated:

- Global skill store from the executable-side skill path
- Workspace-local skill store from `.claude4net/skills`
- Skill apply and self-evolving skill paths for safe local/global behavior

### Antigravity / Ralph Agent Workflow

The `.gemini/agents/` directory defines Antigravity CLI agents for larger work:

- `ralph-orchestrator.md`: chooses one milestone, writes execution cards, coordinates the loop
- `gemini-cli-worker.md`: implements only the assigned card
- `gemini-pro-first-reviewer.md`: reviews actual git diff and test evidence
- `universal-final-controller.md`: verifies release readiness
- `Final_Approach_Control.md`: decides commit readiness only
- `terukirdo_plan.md`: reads attached Markdown documents and produces SSOT candidate implementation plans
- `tech-expert.md`: provides architectural advice without direct implementation

Important workflow rule:

- Ralph Loop may reach commit readiness.
- Push is outside Ralph Loop.
- Push happens only in a separate user and third-final-controller conversation.

## Getting Started

Requirements:

- .NET 10 SDK
- PowerShell
- Provider credentials for remote providers, if needed
- Ollama installed locally if using local Ollama models

Build:

```powershell
dotnet build -p:UseAppHost=false
```

Run the CLI:

```powershell
dotnet run --project Claude4Net.Cli
```

Run with a workspace:

```powershell
dotnet run --project Claude4Net.Cli -- --setworkspace "D:\path\to\workspace"
```

Run Lumen:

```powershell
dotnet run --project Claude4Net.Cli -- --lumen --setworkspace "D:\path\to\workspace"
```

Run dashboard:

```powershell
dotnet run --project Claude4Net.Cli -- --dashboard
```

## Verification

Standard checks:

```powershell
git status --short --branch
git diff --check
git diff --cached --check
dotnet build -p:UseAppHost=false
dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false
```

Official release gate:

```powershell
.\scripts\verify-release.ps1
```

The release gate currently verifies:

- Standard build
- Strict nullable build
- Full unit/integration suite
- Focused state/spec/provider/routine/dashboard smoke groups
- CLI `--smoke-exit`

## Operational SSOT

Active project state is tracked in:

- `Documents/Implementation_Plan.md`
- `IMPLEMENTATION_PROGRESS.md`

Rules:

- `Documents/Implementation_Plan.md` keeps the current queue state, reusable templates, and latest completed entry.
- `IMPLEMENTATION_PROGRESS.md` keeps historical completion evidence.
- Old reports are not authoritative.
- Repository state and raw command output override narrative reports.

## Safety Rules

- Do not modify `.agents/`.
- Do not stage unrelated files.
- Do not use `git add .` or `git add -A` in automated flows.
- Do not mark work complete without build/test/release evidence.
- Do not expose arbitrary dashboard command execution.
- Do not push from Ralph Loop.
- Push requires explicit user approval through the third-final-controller path.

## License

No repository license file is currently tracked in this branch. Add a `LICENSE` file before publishing or distributing outside the current controlled workspace.
