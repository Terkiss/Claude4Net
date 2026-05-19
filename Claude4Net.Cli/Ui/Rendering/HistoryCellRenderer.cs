using System.Collections.Generic;
using System.IO;
using System;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering;

/// <summary>
/// Renders HistoryCells into DisplayLines.
/// </summary>
public static class HistoryCellRenderer
{
    public static IEnumerable<DisplayLine> Render(HistoryCell cell, int width)
    {
        var renderable = cell.GetRenderable();

        // Use a virtual console to render the Spectre IRenderable into an ANSI string with the given width.
        using var sw = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(sw),
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.No
        });

        // Ensure ANSI is enabled even if redirected (we want the codes for Lumen)
        console.Profile.Capabilities.Ansi = true;
        console.Profile.Width = width;

        console.Write(renderable);

        var output = sw.ToString();

        // Split into lines. We use a simple split as Spectre should have handled wrapping.
        // We trim the final newline that AnsiConsole.Write typically appends.
        var lines = output.TrimEnd('\r', '\n').Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            yield return new DisplayLine(line, DisplayLineKind.Transcript);
        }
    }
}
