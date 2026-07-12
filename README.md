# Claude4Net

Claude4Net is a .NET 10 local AI agent runtime with a CLI, multi-provider support, tool execution, event-sourced session history, and an optional dashboard.

## What it provides

- Agent execution with configurable permission modes and dry-run support
- Provider registry and routing for Gemini CLI, OpenAI-compatible APIs, and other providers
- File, shell, LSP, MCP, and workspace tools through a shared orchestration layer
- Session replay, checkpoints, handoffs, routines, and scheduler support
- Optional ASP.NET Core dashboard for live execution and observability
- Self-healing guidance generated from execution and reflection data

## Repository layout

| Project | Responsibility |
| --- | --- |
| `Claude4Net.Cli` | Interactive command-line entry point |
| `Claude4Net.Runtime` | Agent loop, routing, scheduling, persistence, and orchestration |
| `Claude4Net.Api` | LLM provider implementations and provider-facing adapters |
| `Claude4Net.SDK` | Shared contracts, events, models, and interfaces |
| `Claude4Net.Commands` | CLI command registration and handlers |
| `Claude4Net.Tools` | File, process, LSP, MCP, and workspace tools |
| `Claude4Net.Dashboard` | Optional dashboard and live observability surface |
| `Claude4Net.Tests` | Unit and integration tests |

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Any provider-specific CLI or API credentials required by the provider you select

## Build and test

```bash
dotnet restore
dotnet build Claude4Net.slnx
dotnet test Claude4Net.Tests/Claude4Net.Tests.csproj
```

For a quick local verification after restore:

```bash
dotnet build Claude4Net.slnx --no-restore
dotnet test Claude4Net.Tests/Claude4Net.Tests.csproj --no-build
```

## Run the CLI

```bash
dotnet run --project Claude4Net.Cli
```

Start with the dashboard enabled:

```bash
dotnet run --project Claude4Net.Cli -- --dashboard
```

Useful interactive commands include:

- `/help` lists available commands
- `/status` shows session and runtime state
- `/resume <sessionId>` restores a previous session
- `!replay` displays the current event history
- `!skills` lists registered skills
- `/plan` toggles dry-run mode

Provider credentials should be supplied through the provider's supported configuration or login flow. Do not commit API keys or local secret files.

## Architecture

The normal execution path is:

```text
CLI -> Runtime AgentLoop -> Provider -> ToolOrchestrator -> Tools
                         -> Event store / Dashboard / Scheduler
```

Providers implement the shared SDK contracts and are resolved through `ProviderRegistry` and `ProviderFactory`. The runtime keeps provider selection and tool execution behind these boundaries so the CLI and dashboard can share the same behavior.

## Repository hygiene

Generated build output, local Android/SDK artifacts, test results, and secrets are ignored by `.gitignore`. Source-like configuration directories such as `.agents`, `.gemini`, and `android/app` remain visible to Git and should be reviewed before committing.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
