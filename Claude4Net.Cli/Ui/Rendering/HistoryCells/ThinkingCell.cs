using System;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ThinkingCell(string? initialThought = null) : HistoryCell
{
    private readonly StringBuilder _buffer = new(initialThought ?? string.Empty);

    public string Content => _buffer.ToString();

    public bool IsExpanded { get; set; } = false; // Collapsed by default
    public bool IsActive { get; set; } = true;

    public override void AppendDelta(string delta)
    {
        _buffer.Append(delta);
    }

    public override string ToPlainText() => $"Thinking: {Content}";

    public override IRenderable GetRenderable()
    {
        var content = string.IsNullOrWhiteSpace(Content) ? "..." : Content;
        var lineCount = content.Split('\n').Length;

        // Parse theme colors safely
        var thinkingColor = LumenTheme.ThinkingColor;
        var borderColor = Color.Grey35;
        try
        {
            borderColor = Style.Parse(LumenTheme.BorderColor).Foreground;
        }
        catch { }

        if (!IsExpanded)
        {
            if (IsActive)
            {
                // Dynamic spinner frame based on current time
                string[] frames = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
                int frameIdx = Math.Abs((int)((DateTime.UtcNow.Ticks / 1000000) % frames.Length));
                return new Markup($"[{thinkingColor}]+ {frames[frameIdx]} Thinking...[/]");
            }
            else
            {
                return new Markup($"[{thinkingColor}]+ Thought ({lineCount} lines) - Press 'T' to expand[/]");
            }
        }

        // Expanded view
        string headerText = IsActive ? " - THOUGHT (Thinking...) " : $" - THOUGHT ({lineCount} lines) ";
        return new Panel(new Markup($"[{thinkingColor}]{Markup.Escape(content)}[/]"))
        {
            Header = new PanelHeader(headerText),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: borderColor),
            Padding = new Padding(1, 0, 1, 0)
        };
    }
}
