# Codebase Structure

- **`Claude4Net.SDK`**: Core interfaces and data models (e.g., `ITool`, `ILLMProvider`, `LLMResponse`).
- **`Claude4Net.Runtime`**: The core engine managing the 'think-act-observe' loop (`AgentLoop`), application state (`AppState`), and tool orchestration (`ToolOrchestrator`).
- **`Claude4Net.Api`**: Implementations for communicating with various LLM providers (Claude, Gemini, Ollama).
- **`Claude4Net.Tools`**: Practical tools for the agent to interact with the system (e.g., `BashTool`, `FileReadTool`, `FileWriteTool`, `LsTool`).
- **`Claude4Net.Commands`**: Handles user-initiated commands (e.g., `!login`, `/model`, `!yolo`).
- **`Claude4Net.Cli`**: The interactive CLI application entry point using `Spectre.Console`.
- **`Claude4Net.Tests`**: Unit and integration tests for the projects.
