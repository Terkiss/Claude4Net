using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class ToolOrchestrator : IToolRegistry
    {
        private readonly List<ITool> _tools;
        private readonly IUserApprovalHandler? _approvalHandler;

        public ToolOrchestrator(IEnumerable<ITool> tools, IUserApprovalHandler? approvalHandler = null)
        {
            _tools = tools.ToList();
            _approvalHandler = approvalHandler;
        }

        public void AddTool(ITool tool)
        {
            if (!_tools.Any(t => t.Name == tool.Name)) _tools.Add(tool);
        }

        public IReadOnlyList<ITool> GetTools() => _tools.ToList();

        public ITool? FindTool(string name)
        {
            return _tools.FirstOrDefault(t => 
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                (t.Aliases != null && t.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))));
        }

        public async Task<ToolUseResult> ExecuteToolAsync(ToolUseRequest request, object context)
        {
            var tool = FindTool(request.Name);
            if (tool == null) return new ToolUseResult { ToolUseId = request.Id, Content = $"Error: Tool '{request.Name}' not found.", IsError = true };

            try
            {
                string jsonInput = JsonSerializer.Serialize(request.Input);

                bool isFullAuto = AppState.CurrentPermissionMode == PermissionMode.Yolo || 
                                 AppState.CurrentPermissionMode == PermissionMode.BypassPermissions;
                
                if (_approvalHandler != null && IsSensitiveTool(tool.Name) && !isFullAuto)
                {
                    bool approved = await _approvalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                    if (!approved) return new ToolUseResult { ToolUseId = request.Id, Content = "User denied permission.", IsError = true };
                }

                var result = await tool.ExecuteAsync(jsonInput, context);
                return new ToolUseResult { ToolUseId = request.Id, Content = result, IsError = false };
            }
            catch (Exception ex)
            {
                return new ToolUseResult { ToolUseId = request.Id, Content = $"Execution Error: {ex.Message}", IsError = true };
            }
        }

        private bool IsSensitiveTool(string name)
        {
            var sensitivePrefixes = new[] { "bash", "write", "edit", "delete", "shell", "sh" };
            return sensitivePrefixes.Any(p => name.ToLower().Contains(p));
        }

        public async Task<List<ToolUseResult>> ExecuteBatchAsync(IEnumerable<ToolUseRequest> requests, object context)
        {
            var tasks = requests.Select(req => ExecuteToolAsync(req, context));
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }
    }
}
