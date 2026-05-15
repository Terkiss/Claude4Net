using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Output;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Claude4Net.SDK;
using Moq;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Xunit;

namespace Claude4Net.Tests;

public class K042OutputBridgeTests
{
    [Fact]
    public async Task Observer_ShouldUpdateState_OnRunStarted()
    {
        // Arrange
        var console = new TestConsole();
        var renderer = new LumenRenderer(console);
        var state = new LumenState();
        var observer = new LumenRunObserver(renderer, state);

        var runEvent = new SDK.RunStartedEvent("Session-123", "Provider-A", "Model-B", "Hello");

        // Act
        await observer.OnEventAsync(runEvent);

        // Assert
        Assert.Equal("Provider-A", observer.State.Provider);
        Assert.Equal("Model-B", observer.State.Model);
        Assert.Equal("Session-123", observer.State.SessionId);
        Assert.True(observer.State.IsRunning);
    }

    [Fact]
    public async Task Observer_ShouldCreateAssistantCell_OnTextDelta()
    {
        // Arrange
        var console = new TestConsole();
        var renderer = new LumenRenderer(console);
        var state = new LumenState();
        var observer = new LumenRunObserver(renderer, state);

        // Act
        await observer.OnEventAsync(new SDK.TextDeltaEvent("Hello"));

        // Assert
        Assert.Single(observer.State.History);
        Assert.IsType<AssistantResponseCell>(observer.State.History[0]);
    }

    [Fact]
    public async Task Observer_ShouldMapMultipleEvents()
    {
        // Arrange
        var console = new TestConsole();
        var renderer = new LumenRenderer(console);
        var observer = new LumenRunObserver(renderer, new LumenState());

        // Act
        await observer.OnEventAsync(new SDK.ThinkingStartedEvent(1));
        await observer.OnEventAsync(new SDK.ThinkingDeltaEvent("Thinking..."));
        await observer.OnEventAsync(new SDK.ToolCallQueuedEvent("id1", "tool1", "{}"));
        await observer.OnEventAsync(new SDK.ToolResultReceivedEvent("id1", "result", false));
        await observer.OnEventAsync(new SDK.RunCompletedEvent("Session-123", TimeSpan.FromSeconds(1)));

        // Assert
        Assert.Equal(3, observer.State.History.Count);
        Assert.IsType<ThinkingCell>(observer.State.History[0]);
        Assert.IsType<ToolCallCell>(observer.State.History[1]);
        Assert.IsType<ToolResultCell>(observer.State.History[2]);
        Assert.False(observer.State.IsRunning);
    }

    [Fact]
    public async Task OutputHandler_ShouldUpdateAssistantText()
    {
        // Arrange
        var console = new TestConsole();
        var renderer = new LumenRenderer(console);
        var observer = new LumenRunObserver(renderer, new LumenState());
        var handler = new LumenOutputHandler(observer);

        // Act
        await handler.WriteAsync("Direct output");

        // Assert
        Assert.Single(observer.State.History);
        Assert.IsType<AssistantResponseCell>(observer.State.History[0]);
    }

    [Fact]
    public async Task OutputHandler_ShouldHandleCompleteAndFiles()
    {
        // Arrange
        var console = new TestConsole();
        var renderer = new LumenRenderer(console);
        var observer = new LumenRunObserver(renderer, new LumenState());
        var handler = new LumenOutputHandler(observer);

        // Act
        await handler.CompleteAsync("Done");
        await handler.SendFileAsync("path/to/file", "Log file");

        // Assert
        // CompleteAsync adds 1 NoticeCell
        // SendFileAsync adds 2 NoticeCells (one for text, one for path)
        Assert.Equal(3, observer.State.History.Count);
        Assert.All(observer.State.History, cell => Assert.IsType<NoticeCell>(cell));
    }

    [Fact]
    public async Task Observer_ShouldAccumulateTextDelta_InAssistantCell()
    {
        // Arrange
        var console = new TestConsole();
        var renderer = new LumenRenderer(console);
        var observer = new LumenRunObserver(renderer, new LumenState());

        // Act
        await observer.OnEventAsync(new SDK.TextDeltaEvent("Hello "));
        await observer.OnEventAsync(new SDK.TextDeltaEvent("World!"));

        // Assert
        Assert.Single(observer.State.History);
        var cell = Assert.IsType<AssistantResponseCell>(observer.State.History[0]);
        Assert.Equal("Hello World!", cell.Content);
    }

    [Fact]
    public async Task Observer_ShouldPreserveToolIds_ForLinking()
    {
        // Arrange
        var console = new TestConsole();
        var renderer = new LumenRenderer(console);
        var observer = new LumenRunObserver(renderer, new LumenState());
        string callId = "tool-123";

        // Act
        await observer.OnEventAsync(new SDK.ToolCallQueuedEvent(callId, "get_weather", "{\"city\":\"Seoul\"}"));
        await observer.OnEventAsync(new SDK.ToolResultReceivedEvent(callId, "Sunny", false));

        // Assert
        Assert.Equal(2, observer.State.History.Count);
        var callCell = Assert.IsType<ToolCallCell>(observer.State.History[0]);
        var resultCell = Assert.IsType<ToolResultCell>(observer.State.History[1]);

        Assert.Equal(callId, callCell.CallId);
        Assert.Equal(callId, resultCell.CallId);
    }

    [Fact]
    public async Task Observer_ShouldSetIsRunningToFalse_OnRunCompleted()
    {
        // Arrange
        var console = new TestConsole();
        var renderer = new LumenRenderer(console);
        var observer = new LumenRunObserver(renderer, new LumenState { IsRunning = true });

        // Act
        await observer.OnEventAsync(new SDK.RunCompletedEvent("Session-1", TimeSpan.Zero));

        // Assert
        Assert.False(observer.State.IsRunning);
    }

    [Fact]
    public async Task Observer_ShouldNotThrow_WhenRendererThrowsException()
    {
        // Arrange
        var mockConsole = new Mock<IAnsiConsole>();
        mockConsole.Setup(x => x.Write(It.IsAny<IRenderable>())).Throws(new Exception("Render failure"));

        var renderer = new LumenRenderer(mockConsole.Object);
        var observer = new LumenRunObserver(renderer, new LumenState());

        // Act & Assert
        var exception = await Record.ExceptionAsync(async () =>
            await observer.OnEventAsync(new SDK.TextDeltaEvent("Trigger render")));

        Assert.Null(exception); // Should be caught by fail-safe
    }

    [Fact]
    public async Task Observer_ShouldHandleNullEventsGracefully()
    {
        // Arrange
        var console = new TestConsole();
        var renderer = new LumenRenderer(console);
        var observer = new LumenRunObserver(renderer, new LumenState());

        // Act & Assert
        await observer.OnEventAsync(null!); // Should not throw
    }
}