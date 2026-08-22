using Claude4Net.SDK;

namespace Claude4Net.Runtime;

public sealed class EmptyToolRegistry : IToolRegistry
{
    public static EmptyToolRegistry Instance { get; } = new();

    private EmptyToolRegistry()
    {
    }

    public IReadOnlyList<ITool> GetTools() => Array.Empty<ITool>();

    public ITool? GetTool(string name) => null;
}
