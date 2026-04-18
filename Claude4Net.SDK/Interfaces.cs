using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;

namespace Claude4Net.SDK
{
    public interface ITool
    {
        string Name { get; }
        string Description { get; }
        IEnumerable<string>? Aliases => null;
        object? InputSchema { get; }
        bool IsConcurrencySafe => false;
        Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default);
    }

    public interface IToolRegistry
    {
        IReadOnlyList<ITool> GetTools();
        ITool? GetTool(string name);
    }

    public interface IUserApprovalHandler
    {
        Task<bool> RequestApprovalAsync(string tool, string args);
    }

    public enum LLMStreamEventType
    {
        ThinkingDelta,
        TextDelta,
        ToolCallStart,
        Completed
    }

    public enum PermissionMode
    {
        Default,
        Yolo,
        BypassPermissions
    }

    public class LLMStreamEvent
    {
        public LLMStreamEventType Type { get; set; }
        public string Delta { get; set; } = string.Empty;
        public ToolUseRequest? ToolCall { get; set; }
        public LLMResponse? FinalResponse { get; set; }
    }

    public class ToolUseRequest
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public object? Input { get; set; }
    }

    public class ToolUseResult
    {
        public string ToolUseId { get; set; } = string.Empty;
        public object? Content { get; set; }
        public bool IsError { get; set; }
    }

    public class LLMResponse
    {
        public string Text { get; set; } = string.Empty;
        public List<ToolUseRequest> ToolCalls { get; set; } = new();
    }

    public interface ILLMProvider
    {
        string Name { get; }
        IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, System.Threading.CancellationToken ct = default);
        void AddMessage(object message);
        IReadOnlyList<object> GetHistory();
    }
}
