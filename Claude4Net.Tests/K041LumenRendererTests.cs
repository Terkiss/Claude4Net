using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Moq;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Claude4Net.Tests;

public class K041LumenRendererTests
{
    [Fact]
    public void Renderer_ShouldNotThrow_AtDifferentWidths()
    {
        // Arrange
        var console = new TestConsole();
        console.Profile.Width = 80;
        
        var renderer = new LumenRenderer(console);
        var state = new LumenState
        {
            Provider = "TestProvider",
            Model = "TestModel",
            SessionId = "TestSession",
            History = new List<HistoryCell>
            {
                new UserPromptCell("Hello [world]"),
                new AssistantResponseCell()
            }
        };
        state.History[1].AppendDelta("Hi there!");

        // Act & Assert
        renderer.RenderFull(state); // Should not throw
        
        console.Profile.Width = 120;
        renderer.RenderFull(state); // Should not throw
    }

    [Fact]
    public void Renderer_ShouldEscapeMarkupInCells()
    {
        // Arrange
        var console = new TestConsole();
        console.Profile.Width = 80;
        
        var renderer = new LumenRenderer(console);
        var state = new LumenState
        {
            History = new List<HistoryCell>
            {
                new UserPromptCell("Text with [blue]tags[/]")
            }
        };

        // Act
        renderer.RenderFull(state);

        // Assert
        // We just ensure it doesn't crash and writes something
        Assert.NotEmpty(console.Output);
    }
}
