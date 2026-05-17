using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Events;

public record RenderableReceivedEvent(IRenderable Renderable) : LumenEvent;
