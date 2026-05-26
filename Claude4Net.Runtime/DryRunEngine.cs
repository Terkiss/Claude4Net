using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.Tools;
using Spectre.Console;

namespace Claude4Net.Runtime
{
    public class DryRunEngine
    {
        private static bool _isActive;
        private static readonly object _lock = new();
        private static readonly Dictionary<string, string> _virtualFiles = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<SimulatedToolCall> _simulatedCalls = new();
        private static readonly List<SimulatedFileChange> _simulatedFileChanges = new();
        private static readonly List<SimulatedStateChange> _simulatedStateChanges = new();

        static DryRunEngine()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase))
            {
                _isActive = true;
            }
        }

        public static bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    return _isActive;
                }
            }
            set
            {
                lock (_lock)
                {
                    _isActive = value;
                }
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _virtualFiles.Clear();
                _simulatedCalls.Clear();
                _simulatedFileChanges.Clear();
                _simulatedStateChanges.Clear();
            }
        }

        public static ImpactReport GetReport()
        {
            lock (_lock)
            {
                return new ImpactReport
                {
                    ToolCalls = _simulatedCalls.ToList(),
                    FileChanges = _simulatedFileChanges.ToList(),
                    StateChanges = _simulatedStateChanges.ToList()
                };
            }
        }

        public static void RenderReport()
        {
            var report = GetReport();

            var table = new Table().Border(TableBorder.Rounded);
            table.Title("[bold yellow]Dry-Run Impact Report (Plan Mode)[/]");
            table.AddColumn("[bold]Category[/]");
            table.AddColumn("[bold]Target / Detail[/]");
            table.AddColumn("[bold]Description / Impact[/]");

            foreach (var call in report.ToolCalls)
            {
                table.AddRow("[cyan]Tool Call[/]", Markup.Escape(call.Name), Markup.Escape(call.Arguments.Length > 60 ? call.Arguments.Substring(0, 57) + "..." : call.Arguments));
            }

            foreach (var change in report.FileChanges)
            {
                string statusColor = change.ChangeType switch
                {
                    "Create" => "green",
                    "Update" => "yellow",
                    "Delete" => "red",
                    _ => "white"
                };
                table.AddRow($"[{statusColor}]File {change.ChangeType}[/]", Markup.Escape(change.FilePath), Markup.Escape(change.ImpactAnalysis));
            }

            foreach (var state in report.StateChanges)
            {
                table.AddRow("[magenta]State Change[/]", Markup.Escape(state.Target), Markup.Escape($"{state.Action}: {state.Details}"));
            }

            AnsiConsole.Write(table);

            if (report.FileChanges.Any(c => !string.IsNullOrEmpty(c.DiffContent)))
            {
                var diffsTable = new Table().Border(TableBorder.Minimal);
                diffsTable.AddColumn("[bold]File Diff Details[/]");
                foreach (var change in report.FileChanges)
                {
                    if (!string.IsNullOrEmpty(change.DiffContent))
                    {
                        diffsTable.AddRow($"[bold underline]{Markup.Escape(change.FilePath)} ({change.ChangeType})[/]\n{Markup.Escape(change.DiffContent)}");
                    }
                }
                AnsiConsole.Write(new Panel(diffsTable) { Header = new PanelHeader("Virtual Changes Preview"), Border = BoxBorder.Rounded });
            }
        }

        public static async Task<ToolUseResult> ExecuteSimulatedToolAsync(ToolUseRequest request, ToolOrchestrator orchestrator, CancellationToken ct)
        {
            var tool = orchestrator.GetTool(request.Name);
            if (tool == null)
            {
                return new ToolUseResult { ToolUseId = request.Id, Content = $"Error: Tool '{request.Name}' not found.", IsError = true };
            }

            string jsonInput = JsonSerializer.Serialize(request.Input);
            lock (_lock)
            {
                _simulatedCalls.Add(new SimulatedToolCall
                {
                    Name = request.Name,
                    Arguments = jsonInput,
                    Timestamp = DateTime.UtcNow
                });
            }

            string toolNameLower = tool.Name.ToLowerInvariant();

            // 1. FileWriteTool simulation
            if (toolNameLower.Contains("filewritetool") || toolNameLower == "write")
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var input = JsonSerializer.Deserialize<FileWriteInput>(jsonInput, options);
                    if (input != null)
                    {
                        bool exists = File.Exists(input.file_path) || _virtualFiles.ContainsKey(input.file_path);
                        string changeType = exists ? "Update" : "Create";

                        string? oldContent = null;
                        if (_virtualFiles.TryGetValue(input.file_path, out var cached))
                        {
                            oldContent = cached;
                        }
                        else if (File.Exists(input.file_path))
                        {
                            oldContent = await File.ReadAllTextAsync(input.file_path, ct);
                        }

                        // Generate preview / diff
                        string diff = "";
                        if (tool is IPreviewableTool previewTool)
                        {
                            var preview = await previewTool.GetPreviewAsync(jsonInput);
                            diff = preview?.DiffContent ?? "";
                        }

                        lock (_lock)
                        {
                            _virtualFiles[input.file_path] = input.content;
                            _simulatedFileChanges.Add(new SimulatedFileChange
                            {
                                FilePath = input.file_path,
                                ChangeType = changeType,
                                DiffContent = diff,
                                ImpactAnalysis = input.file_path.EndsWith(".cs") ? "Modifies C# source code." : "Modifies project asset."
                            });
                        }

                        return new ToolUseResult { ToolUseId = request.Id, Content = new { filePath = input.file_path, status = "Success" }, IsError = false };
                    }
                }
                catch (Exception ex)
                {
                    return new ToolUseResult { ToolUseId = request.Id, Content = $"Dry-run Simulation Error: {ex.Message}", IsError = true };
                }
            }

            // 2. FileEditTool simulation
            if (toolNameLower.Contains("fileedittool") || toolNameLower == "edit")
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var input = JsonSerializer.Deserialize<FileEditInput>(jsonInput, options);
                    if (input != null)
                    {
                        string? oldContent = null;
                        if (_virtualFiles.TryGetValue(input.file_path, out var cached))
                        {
                            oldContent = cached;
                        }
                        else if (File.Exists(input.file_path))
                        {
                            oldContent = await File.ReadAllTextAsync(input.file_path, ct);
                        }
                        else
                        {
                            return new ToolUseResult { ToolUseId = request.Id, Content = $"Simulation Error: File not found: {input.file_path}", IsError = true };
                        }

                        if (!oldContent.Contains(input.old_string))
                        {
                            return new ToolUseResult { ToolUseId = request.Id, Content = "Simulation Error: String not found for replacement.", IsError = true };
                        }

                        string updatedContent = oldContent.Replace(input.old_string, input.new_string);

                        // Generate preview / diff
                        string diff = "";
                        if (tool is IPreviewableTool previewTool)
                        {
                            var preview = await previewTool.GetPreviewAsync(jsonInput);
                            diff = preview?.DiffContent ?? "";
                        }

                        lock (_lock)
                        {
                            _virtualFiles[input.file_path] = updatedContent;
                            _simulatedFileChanges.Add(new SimulatedFileChange
                            {
                                FilePath = input.file_path,
                                ChangeType = "Update",
                                DiffContent = diff,
                                ImpactAnalysis = "Edits file content."
                            });
                        }

                        return new ToolUseResult { ToolUseId = request.Id, Content = new { filePath = input.file_path, status = "Success" }, IsError = false };
                    }
                }
                catch (Exception ex)
                {
                    return new ToolUseResult { ToolUseId = request.Id, Content = $"Dry-run Simulation Error: {ex.Message}", IsError = true };
                }
            }

            // 3. FileReadTool simulation (Read from virtual files if they exist, else actual disk)
            if (toolNameLower.Contains("filereadtool") || toolNameLower == "read")
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var input = JsonSerializer.Deserialize<FileReadInput>(jsonInput, options);
                    if (input != null)
                    {
                        string content;
                        lock (_lock)
                        {
                            _virtualFiles.TryGetValue(input.file_path, out content);
                        }

                        if (content != null)
                        {
                            var allLines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            int startLine = input.offset ?? 1;
                            int lineCount = input.limit ?? (allLines.Length - startLine + 1);
                            var selectedLines = allLines.Skip(Math.Max(0, startLine - 1)).Take(Math.Max(0, lineCount)).ToList();

                            return new ToolUseResult
                            {
                                ToolUseId = request.Id,
                                Content = new
                                {
                                    filePath = input.file_path,
                                    content = string.Join("\n", selectedLines),
                                    totalLines = allLines.Length
                                },
                                IsError = false
                            };
                        }
                    }
                }
                catch { }
            }

            // 4. Memory/State modification tools simulation
            if (toolNameLower.StartsWith("pandas_agent_memory_upsert") ||
                toolNameLower.StartsWith("pandas_insert_row") ||
                toolNameLower.StartsWith("pandas_update_cell") ||
                toolNameLower.StartsWith("pandas_delete_rows") ||
                toolNameLower.StartsWith("pandas_snapshot") ||
                toolNameLower.StartsWith("pandas_restore") ||
                toolNameLower.StartsWith("pandas_save_csv") ||
                toolNameLower.StartsWith("pandas_save_json") ||
                toolNameLower.StartsWith("pandas_save_sqlite"))
            {
                lock (_lock)
                {
                    _simulatedStateChanges.Add(new SimulatedStateChange
                    {
                        Target = "DataUniverse/Pandas Table",
                        Action = tool.Name,
                        Details = jsonInput.Length > 100 ? jsonInput.Substring(0, 97) + "..." : jsonInput
                    });
                }

                return new ToolUseResult { ToolUseId = request.Id, Content = new { status = "Success", message = $"[Dry-Run] State modification {tool.Name} simulated successfully." }, IsError = false };
            }

            // 5. Shell execution command (BashTool)
            if (toolNameLower.Contains("bashtool") || toolNameLower == "bash" || toolNameLower == "sh" || toolNameLower == "shell")
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var input = JsonSerializer.Deserialize<BashInput>(jsonInput, options);
                    if (input != null)
                    {
                        string cmd = input.command.ToLowerInvariant();
                        bool isModifying = cmd.Contains("rm ") || cmd.Contains("del ") || cmd.Contains("mkdir ") || cmd.Contains("git ") || cmd.Contains(">>") || cmd.Contains(">");

                        if (isModifying)
                        {
                            lock (_lock)
                            {
                                _simulatedStateChanges.Add(new SimulatedStateChange
                                {
                                    Target = "System shell",
                                    Action = "Shell Command Blocked",
                                    Details = input.command
                                });
                            }
                            return new ToolUseResult
                            {
                                ToolUseId = request.Id,
                                Content = new { command = input.command, output = "", error = "[Dry-Run] Modifying command blocked.", exitCode = 0 },
                                IsError = false
                            };
                        }
                    }
                }
                catch { }
            }

            // Fallback: execute normally
            return await orchestrator.ExecuteToolAsync(request, new { }, null, ct);
        }

        public static async Task<List<ToolUseResult>> ExecuteSimulatedBatchAsync(IEnumerable<ToolUseRequest> requests, ToolOrchestrator orchestrator, IUserApprovalHandler? overrideHandler, CancellationToken ct)
        {
            var results = new List<ToolUseResult>();
            foreach (var req in requests)
            {
                if (ct.IsCancellationRequested) break;
                results.Add(await ExecuteSimulatedToolAsync(req, orchestrator, ct));
            }
            return results;
        }
    }

    public class ImpactReport
    {
        public List<SimulatedToolCall> ToolCalls { get; set; } = new();
        public List<SimulatedFileChange> FileChanges { get; set; } = new();
        public List<SimulatedStateChange> StateChanges { get; set; } = new();
    }

    public class SimulatedToolCall
    {
        public string Name { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class SimulatedFileChange
    {
        public string FilePath { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty; // Create, Update, Delete
        public string DiffContent { get; set; } = string.Empty;
        public string ImpactAnalysis { get; set; } = string.Empty;
    }

    public class SimulatedStateChange
    {
        public string Target { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
}
