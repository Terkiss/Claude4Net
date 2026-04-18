using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    public class GeminiCliProvider : ILLMProvider
    {
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;

        public GeminiCliProvider(IToolRegistry toolRegistry)
        {
            _toolRegistry = toolRegistry;
        }

        public string Name => "gemini-cli";

        public void AddMessage(object message)
        {
            if (message != null)
            {
                _conversationHistory.Add(message);
            }
        }

        public IReadOnlyList<object> GetHistory()
        {
            return _conversationHistory.AsReadOnly();
        }

        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", content = prompt });
            }

            var tools = _toolRegistry?.GetTools();

            var toolDefs = new StringBuilder();
            if (tools != null && tools.Count > 0)
            {
                toolDefs.AppendLine("[AVAILABLE TOOLS]");
                foreach (var t in tools)
                {
                    string schemaDoc = t.InputSchema != null ? System.Text.Json.JsonSerializer.Serialize(t.InputSchema, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) : "{}";
                    toolDefs.AppendLine($"- Name: {t.Name}");
                    toolDefs.AppendLine($"  Description: {t.Description}");
                    toolDefs.AppendLine($"  InputSchema: {schemaDoc}");
                    toolDefs.AppendLine();
                }
                toolDefs.AppendLine(@"[TOOL USE RULES]
You are connected to a C# execution loop. DO NOT execute your internal commands.
If you need to use a tool from the list above, you MUST respond EXACTLY in this XML format:
<tool_call name=""ToolName"">
{ ""argName"": ""argValue"" }
</tool_call>
You can only call one tool per <tool_call> tag. After outputting a tool call, wait for the result.");
            }

            var historyDump = new StringBuilder();
            if (_conversationHistory.Count > 0)
            {
                historyDump.AppendLine("[CONVERSATION HISTORY]");
                historyDump.AppendLine(System.Text.Json.JsonSerializer.Serialize(_conversationHistory, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }

            var systemPromptBuilder = new StringBuilder();
            
            string skillsDir = string.IsNullOrEmpty(AppState.CurrentCwd) ? "" : Path.Combine(AppState.CurrentCwd, "Skills");
            if (!string.IsNullOrEmpty(skillsDir) && Directory.Exists(skillsDir))
            {
                var skillFiles = Directory.GetFiles(skillsDir, "*.md");
                foreach (var file in skillFiles)
                {
                    systemPromptBuilder.AppendLine($"\n[SKILL GUIDELINE: {Path.GetFileName(file)}]");
                    systemPromptBuilder.AppendLine(File.ReadAllText(file));
                    systemPromptBuilder.AppendLine();
                }
            }
            var systemPrompt = systemPromptBuilder.ToString();

            string combinedPrompt = $"{systemPrompt}\n\n[CRITICAL INSTRUCTION]\n반드시 모든 사고(Thinking) 과정과 출력, 대답, 분석 내용을 한국어(Korean)로만 작성하세요.\n\n{toolDefs}\n\n{historyDump}\n\n[CURRENT USER PROMPT]:\n{prompt}";

            string arguments = "/c gemini -y -p \"\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            await process.StandardInput.WriteAsync(combinedPrompt);
            process.StandardInput.Close();

            var interceptedTools = new List<ToolUseRequest>();
            bool isBufferingTool = false;
            StringBuilder toolBuffer = new();
            string currentToolName = "";

            var fullText = new StringBuilder();

            process.ErrorDataReceived += (sender, args) => { /* 백그라운드 에러 스트림 드레인 (Deadlock 방지) */ };
            process.BeginErrorReadLine();

            using var reader = process.StandardOutput;

            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    process.Kill(true);
                    break;
                }

                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                string cleanLine = System.Text.RegularExpressions.Regex.Replace(line, @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", "");

                if (cleanLine.Contains("MCP issues detected"))
                {
                    cleanLine = cleanLine.Replace("MCP issues detected. ", "")
                                         .Replace("Run /mcp list for status.", "")
                                         .Replace("MCP issues detected.", "");
                }

                if (!isBufferingTool)
                {
                    var matchStart = System.Text.RegularExpressions.Regex.Match(cleanLine, @"<tool_call[ \t]+name\s*=\s*""([^""]+)""\s*>");
                    if (matchStart.Success)
                    {
                        isBufferingTool = true;
                        currentToolName = matchStart.Groups[1].Value;
                        string precedingText = cleanLine.Substring(0, matchStart.Index);
                        
                        if (!string.IsNullOrWhiteSpace(precedingText))
                        {
                            fullText.Append(precedingText + "\n");
                            yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = precedingText + "\n" };
                        }

                        string remainder = cleanLine.Substring(matchStart.Index + matchStart.Length);
                        toolBuffer.Clear();
                        
                        var matchEnd = System.Text.RegularExpressions.Regex.Match(remainder, @"</tool_call>");
                        if (matchEnd.Success)
                        {
                            isBufferingTool = false;
                            toolBuffer.AppendLine(remainder.Substring(0, matchEnd.Index));
                            cleanLine = remainder.Substring(matchEnd.Index + matchEnd.Length);
                        }
                        else
                        {
                            toolBuffer.AppendLine(remainder);
                            continue;
                        }
                    }
                }
                else
                {
                    var matchEnd = System.Text.RegularExpressions.Regex.Match(cleanLine, @"</tool_call>");
                    if (matchEnd.Success)
                    {
                        isBufferingTool = false;
                        toolBuffer.AppendLine(cleanLine.Substring(0, matchEnd.Index));
                        cleanLine = cleanLine.Substring(matchEnd.Index + matchEnd.Length);
                    }
                    else
                    {
                        toolBuffer.AppendLine(cleanLine);
                        continue;
                    }
                }

                if (!isBufferingTool && toolBuffer.Length > 0 && !string.IsNullOrEmpty(currentToolName))
                {
                    string jsonArgs = toolBuffer.ToString().Trim();
                    if (string.IsNullOrEmpty(jsonArgs)) jsonArgs = "{}";
                    
                    object? inputObj = null;
                    try 
                    { 
                        inputObj = System.Text.Json.JsonSerializer.Deserialize<object>(jsonArgs); 
                    } 
                    catch (Exception ex)
                    { 
                        Console.WriteLine($"\n\x1b[1;31m⚠️ JSON Parse Error (\x1b[33m{currentToolName}\x1b[1;31m):\x1b[0m {ex.Message}");
                        Console.WriteLine($"\x1b[90mRaw JSON:\x1b[0m {jsonArgs}");
                    }
                    
                    var toolReq = new ToolUseRequest {
                        Id = "call_" + Guid.NewGuid().ToString("N"),
                        Name = currentToolName,
                        Input = inputObj ?? new { }
                    };
                    interceptedTools.Add(toolReq);

                    yield return new LLMStreamEvent {
                        Type = LLMStreamEventType.ToolCallStart,
                        ToolCall = toolReq
                    };

                    toolBuffer.Clear();
                    currentToolName = "";
                    
                    if (string.IsNullOrWhiteSpace(cleanLine))
                        continue;
                }

                if (string.IsNullOrWhiteSpace(cleanLine))
                    continue;

                string chunk = cleanLine + "\n";
                fullText.Append(chunk);

                yield return new LLMStreamEvent
                {
                    Type = LLMStreamEventType.TextDelta,
                    Delta = chunk
                };
            }

            await process.WaitForExitAsync(ct);

            string finalOutput = fullText.ToString().TrimEnd();

            if (!string.IsNullOrEmpty(finalOutput))
            {
                _conversationHistory.Add(new { role = "model", content = finalOutput });
            }

            yield return new LLMStreamEvent
            {
                Type = LLMStreamEventType.Completed,
                FinalResponse = new LLMResponse { Text = finalOutput, ToolCalls = interceptedTools }
            };
        }
    }
}
