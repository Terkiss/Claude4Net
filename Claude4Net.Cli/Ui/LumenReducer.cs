using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Claude4Net.Cli.Ui.Approval;

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

            UserPromptSubmittedEvent e => string.IsNullOrWhiteSpace(e.Text) ? state : AddCell(state, new UserPromptCell(e.Text)),

            ThinkingStartedEvent e => AddCell(state, new ThinkingCell(e.InitialThought)),

            ThinkingUpdatedEvent e => UpdateLastCell<ThinkingCell>(state, c => c.AppendDelta(e.ThoughtDelta)),

            AssistantTextUpdatedEvent e => EnsureAssistantCell(state).Apply(s =>
                UpdateLastCell<AssistantResponseCell>(s, c => c.AppendDelta(e.TextDelta))),

            ToolCallStartedEvent e => AddCell(state, new ToolCallCell(e.CallId, e.ToolName, e.Arguments)),

            ToolResultReceivedEvent e => AddCell(state, new ToolResultCell(e.CallId, e.Result, e.IsError)),

            NoticeReceivedEvent e => AddCell(state, new NoticeCell(e.Message, e.Level)),

            MarkupReceivedEvent e => AddCell(state, new MarkupCell(e.Markup)),

            RenderableReceivedEvent e => AddCell(state, new RenderableCell(e.Renderable)),

            ErrorReceivedEvent e => AddCell(state, new ErrorCell(e.Message, e.Details)),

            ApprovalRequestedEvent e => AddCell(state, new ApprovalCell(e.RequestId, e.Title, e.Description)),

            RunCompletedEvent => state with
            {
                IsRunning = false,
                History = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(state.History, c => {
                    if (c is ThinkingCell tc) tc.IsActive = false;
                    return c;
                }))
            },

            ApprovalDialogOpenedEvent e => state with
            {
                ApprovalDialog = new ApprovalDialogState
                {
                    RequestId = e.RequestId,
                    Title = e.Title,
                    Description = e.Description,
                    RiskLevel = e.RiskLevel,
                    PreviewSummary = e.PreviewSummary,
                    IsVisible = true,
                    LastAction = ApprovalDialogAction.None // Reset on new open
                }
            },

            ApprovalDialogClosedEvent => state with
            {
                ApprovalDialog = state.ApprovalDialog with { IsVisible = false }
            },

            ApprovalDialogActionSelectedEvent e => state with
            {
                ApprovalDialog = state.ApprovalDialog with { LastAction = e.Action }
            },

            ApprovalDialogDetailToggledEvent => state with
            {
                ApprovalDialog = state.ApprovalDialog with { IsDetailMode = !state.ApprovalDialog.IsDetailMode }
            },

            ScrollUpRequestedEvent e => state with
            {
                Scroll = state.Scroll with
                {
                    AutoScroll = false,
                    ScrollOffset = state.Scroll.ScrollOffset + e.Lines
                }
            },

            ScrollDownRequestedEvent e => MoveScroll(state, -e.Lines),

            ScrollToHomeRequestedEvent => state with
            {
                Scroll = state.Scroll with
                {
                    AutoScroll = false,
                    ScrollOffset = int.MaxValue / 2 // Will be clamped by builder
                }
            },

            ScrollToEndRequestedEvent => state with { Scroll = state.Scroll with { AutoScroll = true, ScrollOffset = 0 } },

            ClearTranscriptEvent => state with
            {
                History = new System.Collections.Generic.List<HistoryCell>(),
                Scroll = state.Scroll with { ScrollOffset = 0, AutoScroll = true }
            },

            ThemeChangedEvent e => ApplyTheme(state, e.ThemeName),

            ModelChangedEvent e => state with
            {
                Provider = e.Provider,
                Model = e.Model
            },

            _ => state
        };
    }

    private static LumenState MoveScroll(LumenState state, int delta)
    {
        int newOffset = Math.Max(0, state.Scroll.ScrollOffset + delta);
        bool pinned = newOffset == 0;
        return state with
        {
            Scroll = state.Scroll with { AutoScroll = pinned, ScrollOffset = newOffset }
        };
    }

    private static LumenState AddCell(LumenState state, HistoryCell cell)
    {
        var newHistory = new List<HistoryCell>(state.History) { cell };

        // If pinned to bottom, keep offset at 0.
        var newScroll = state.Scroll.AutoScroll
            ? state.Scroll with { ScrollOffset = 0 }
            : state.Scroll;

        return state with { History = newHistory, Scroll = newScroll };
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



    private static LumenState ApplyTheme(LumenState state, string themeName)
    {
        LumenTheme.ApplyTheme(themeName);
        return state;
    }

    // Helper for fluent-like ensure/update
    private static T Apply<T>(this T obj, Func<T, T> func) => func(obj);
}
