using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;

namespace Claude4Net.Cli.Ui;

public static class LumenReducer
{
    public static LumenState Reduce(LumenState state, LumenEvent @event)
    {
        return @event switch
        {
            RunStartedEvent e => state with
            {
                Provider = e.Provider,
                Model = e.Model,
                SessionId = e.SessionId,
                IsRunning = true
            },
            
            UserPromptSubmittedEvent e => AddCell(state, new UserPromptCell(e.Text)),
            
            ThinkingStartedEvent e => AddCell(state, new ThinkingCell(e.InitialThought)),
            
            ThinkingUpdatedEvent e => UpdateLastCell<ThinkingCell>(state, c => c.AppendDelta(e.ThoughtDelta)),
            
            AssistantTextUpdatedEvent e => EnsureAssistantCell(state).Apply(s => 
                UpdateLastCell<AssistantResponseCell>(s, c => c.AppendDelta(e.TextDelta))),
            
            ToolCallStartedEvent e => AddCell(state, new ToolCallCell(e.CallId, e.ToolName, e.Arguments)),
            
            ToolResultReceivedEvent e => AddCell(state, new ToolResultCell(e.CallId, e.Result, e.IsError)),
            
            NoticeReceivedEvent e => AddCell(state, new NoticeCell(e.Message, e.Level)),
            
            ErrorReceivedEvent e => AddCell(state, new ErrorCell(e.Message, e.Details)),
            
            ApprovalRequestedEvent e => AddCell(state, new ApprovalCell(e.RequestId, e.Title, e.Description)),
            
            RunCompletedEvent => state with { IsRunning = false },
            
            _ => state
        };
    }

    private static LumenState AddCell(LumenState state, HistoryCell cell)
    {
        var newHistory = new List<HistoryCell>(state.History) { cell };
        return state with { History = newHistory };
    }

    private static LumenState UpdateLastCell<T>(LumenState state, Action<T> action) where T : HistoryCell
    {
        if (state.LastCell is T target)
        {
            action(target);
            return state;
        }
        return state;
    }

    private static LumenState EnsureAssistantCell(LumenState state)
    {
        if (state.LastCell is AssistantResponseCell) return state;
        return AddCell(state, new AssistantResponseCell());
    }

    // Helper for fluent-like ensure/update
    private static T Apply<T>(this T obj, Func<T, T> func) => func(obj);
}
