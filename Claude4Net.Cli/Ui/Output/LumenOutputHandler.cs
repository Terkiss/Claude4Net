using System;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.Cli.Ui.Events;

namespace Claude4Net.Cli.Ui.Output;

/// <summary>
/// Implementation of IOutputHandler that bridges to Lumen UI state.
/// This handler specifically handles direct output calls by updating the
/// AssistantResponseCell with provided text as deltas.
/// </summary>
public class LumenOutputHandler(LumenRunObserver observer) : IOutputHandler
{
    /// <summary>
    /// Writes text to the current assistant response cell.
    /// In Lumen, direct writes are treated as text deltas to the current assistant turn.
    /// </summary>
    public Task WriteAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return Task.CompletedTask;

        observer.UpdateState(new AssistantTextUpdatedEvent(text));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Completes the current output sequence with an optional final message.
    /// In Lumen, finalMessage is typically already reported via deltas or handled by AgentLoop.
    /// We avoid adding a notice here to prevent duplication.
    /// </summary>
    public Task CompleteAsync(string finalMessage)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reports a file output to the UI.
    /// </summary>
    public Task SendFileAsync(string filePath, string? text = null)
    {
        if (!string.IsNullOrEmpty(text))
        {
            observer.UpdateState(new NoticeReceivedEvent(text));
        }
        observer.UpdateState(new NoticeReceivedEvent($"File available at: {filePath}", "Success"));
        return Task.CompletedTask;
    }
}
