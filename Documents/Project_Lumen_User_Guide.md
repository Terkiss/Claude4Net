# Project Lumen User Guide

Welcome to the new Lumen UI for Claude4Net! Project Lumen adds a real-time, stateful, and interactive terminal experience as an alternative to the traditional scrolling log output.

## Running in Lumen Mode
Lumen mode is currently an opt-in feature. To start the CLI with the new UI, use the `--lumen` flag:
```powershell
dotnet run --project Claude4Net.Cli -- --lumen
```
You will be greeted by a rich header, a continuous conversation history, and a bottom prompt composer.

## Default & Legacy Behavior
By default, the CLI runs in the classic **Legacy Mode** (streaming logs). You can also explicitly force legacy mode using the `--legacy-cli` flag:
```powershell
dotnet run --project Claude4Net.Cli -- --legacy-cli
```
*Note: Piped input (`echo "prompt" | dotnet run`), Discord integrations, and the Dashboard automatically bypass Lumen and use their native or legacy routing.*

## Navigating the Interface

### The Prompt Composer
The bottom of the screen features the Prompt Composer.
- **Typing:** Type your prompt naturally. The buffer handles wrapping.
- **Submission:** Press `Enter` to submit.
- **History:** Use `Up Arrow` and `Down Arrow` to navigate your prompt history.
- **Cancellation (ESC):** If an agent is currently running or thinking, pressing `ESC` will **cancel the active run** and return you to the prompt.

### Approval Dialogs
When the agent requests a sensitive operation (like writing a file), a modal dialog will appear over the prompt:
- **`Y` / `Enter`**: Approve the action.
- **`N`**: Deny the action.
- **`D`**: Toggle the Details/Diff view to inspect exactly what will change.
- **`ESC`**: Cancel the approval and abort the tool call.

## Features & Improvements
- **Tool Summarization:** Extremely long tool inputs or outputs are truncated in the UI (e.g., `Truncated for display...`) to prevent terminal lag, but the full raw text is preserved in the underlying session data.
- **Column Defense:** The footer and layout will automatically adjust if your terminal is resized to narrow widths (e.g., 80 columns).
- **Zero Duplication:** You will no longer see the agent's thought process duplicated as both a stream and a final block.

## Troubleshooting
- **Flickering or Broken Rendering:** If the UI artifacts break, try clearing the screen with the `/clear` command. If the issue persists, switch to `--legacy-cli` and report the terminal emulator you are using.
- **Input overwritten:** If background output arrives while you are typing, your buffer is protected. The UI will re-render your input safely below the new content.

## Known Limitations (v1)
- Rich scrollback (scrolling up through history independently of the terminal buffer) is not yet supported.
- Full-screen alternate buffer is deferred to v2 to ensure maximum compatibility across different Windows terminal emulators.
