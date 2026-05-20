using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Xunit;

namespace Claude4Net.Tests;

public class K040LumenStateTests
{
    [Fact]
    public void InitialState_IsCorrect()
    {
        var state = new LumenState();
        Assert.Null(state.Provider);
        Assert.Empty(state.History);
        Assert.False(state.IsRunning);
    }

    [Fact]
    public void RunStarted_UpdatesMetadata()
    {
        var state = new LumenState();
        var @event = new RunStartedEvent("Anthropic", "claude-3-opus", "session-123");
        
        var newState = LumenReducer.Reduce(state, @event);
        
        Assert.Equal("Anthropic", newState.Provider);
        Assert.Equal("claude-3-opus", newState.Model);
        Assert.Equal("session-123", newState.SessionId);
        Assert.True(newState.IsRunning);
    }

    [Fact]
    public void UserPrompt_AddsCell()
    {
        var state = new LumenState();
        var @event = new UserPromptSubmittedEvent("Hello, world!");
        
        var newState = LumenReducer.Reduce(state, @event);
        
        Assert.Single(newState.History);
        Assert.IsType<UserPromptCell>(newState.History[0]);
        Assert.Equal("User: Hello, world!", newState.History[0].ToPlainText());
    }

    [Fact]
    public void AssistantText_StreamsIntoCell()
    {
        var state = new LumenState();
        state = LumenReducer.Reduce(state, new AssistantTextUpdatedEvent("Hello"));
        state = LumenReducer.Reduce(state, new AssistantTextUpdatedEvent(" world"));
        
        Assert.Single(state.History);
        var cell = Assert.IsType<AssistantResponseCell>(state.History[0]);
        Assert.Equal("Hello world", cell.Content);
        Assert.Equal("Assistant: Hello world", cell.ToPlainText());
    }

    [Fact]
    public void Thinking_StreamsIntoCell()
    {
        var state = new LumenState();
        state = LumenReducer.Reduce(state, new ThinkingStartedEvent("I am"));
        state = LumenReducer.Reduce(state, new ThinkingUpdatedEvent(" thinking"));
        
        Assert.Single(state.History);
        var cell = Assert.IsType<ThinkingCell>(state.History[0]);
        Assert.Equal("I am thinking", cell.Content);
    }

    [Fact]
    public void ToolCallAndResult_AddsCells()
    {
        var state = new LumenState();
        state = LumenReducer.Reduce(state, new ToolCallStartedEvent("call-1", "read_file", "{\"path\":\"test.txt\"}"));
        state = LumenReducer.Reduce(state, new ToolResultReceivedEvent("call-1", "File content here"));
        
        Assert.Equal(2, state.History.Count);
        Assert.IsType<ToolCallCell>(state.History[0]);
        Assert.IsType<ToolResultCell>(state.History[1]);
        
        Assert.Contains("read_file", state.History[0].ToPlainText());
        Assert.Contains("File content here", state.History[1].ToPlainText());
    }

    [Fact]
    public void ErrorAndNotice_AddsCells()
    {
        var state = new LumenState();
        state = LumenReducer.Reduce(state, new NoticeReceivedEvent("Something happened", "Warning"));
        state = LumenReducer.Reduce(state, new ErrorReceivedEvent("Failure", "Disk full"));
        
        Assert.Equal(2, state.History.Count);
        Assert.IsType<NoticeCell>(state.History[0]);
        Assert.IsType<ErrorCell>(state.History[1]);
        
        Assert.Equal("[WARNING]: Something happened", state.History[0].ToPlainText());
        Assert.Equal("ERROR: Failure - Disk full", state.History[1].ToPlainText());
    }

    [Fact]
    public void Approval_CanBeResolved()
    {
        var state = new LumenState();
        state = LumenReducer.Reduce(state, new ApprovalRequestedEvent("req-1", "Delete file", "Delete important.txt?"));
        
        var cell = Assert.IsType<ApprovalCell>(state.History[0]);
        Assert.Contains("PENDING", cell.ToPlainText());
        
        cell.Resolve(true);
        Assert.Contains("APPROVED", cell.ToPlainText());
    }

    [Fact]
    public void ClearTranscript_ClearsHistoryAndScroll()
    {
        var state = new LumenState();
        state = LumenReducer.Reduce(state, new UserPromptSubmittedEvent("Hello"));
        Assert.Single(state.History);

        var newState = LumenReducer.Reduce(state, new ClearTranscriptEvent());
        Assert.Empty(newState.History);
        Assert.Equal(0, newState.Scroll.ScrollOffset);
    }

    [Fact]
    public void ThemeChanged_AppliesTheme()
    {
        var state = new LumenState();
        Assert.Equal("green", LumenTheme.UserColor);

        var newState = LumenReducer.Reduce(state, new ThemeChangedEvent("light"));
        Assert.Equal("darkgreen", LumenTheme.UserColor);

        // Reset back to dark
        LumenTheme.ApplyTheme("dark");
    }

    [Fact]
    public void ModelChanged_UpdatesActiveConfig()
    {
        var state = new LumenState();
        var newState = LumenReducer.Reduce(state, new ModelChangedEvent("anthropic", "claude-3-5-sonnet"));
        Assert.Equal("anthropic", newState.Provider);
        Assert.Equal("claude-3-5-sonnet", newState.Model);
    }

    [Fact]
    public void ProcessToolCallAndResult_CollapsibleStateAndRendering()
    {
        var state = new LumenState();
        state = LumenReducer.Reduce(state, new ToolCallStartedEvent("call-99", "list_dir", "{}"));
        state = LumenReducer.Reduce(state, new ToolResultReceivedEvent("call-99", "file1.txt, file2.txt", false));

        Assert.Equal(2, state.History.Count);
        var callCell = Assert.IsType<ToolCallCell>(state.History[0]);
        var resultCell = Assert.IsType<ToolResultCell>(state.History[1]);

        Assert.True(callCell.IsExpanded);
        Assert.True(resultCell.IsExpanded);

        var callRenderable = callCell.GetRenderable();
        var resultRenderable = resultCell.GetRenderable();

        Assert.NotNull(callRenderable);
        Assert.NotNull(resultRenderable);

        callCell.IsExpanded = false;
        resultCell.IsExpanded = false;

        Assert.False(callCell.IsExpanded);
        Assert.False(resultCell.IsExpanded);
    }
}
