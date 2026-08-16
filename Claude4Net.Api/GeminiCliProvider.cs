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
    /// <summary>
    /// ?��? 'gemini' CLI ?�구�??�로?�스�??�행?�여 Google Gemini 모델??기능???�?�하???�로바이?�입?�다.
    /// CLI 출력??캡처?�고 XML ?�식???�구 ?�출???�싱?�여 처리?�니??
    /// </summary>
    public class GeminiCliProvider : ILLMProvider
    {
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// GeminiCliProvider?????�스?�스�?초기?�합?�다.
        /// </summary>
        /// <param name="toolRegistry">?�구 ?�보 ?��??�트�?/param>
        public GeminiCliProvider(IToolRegistry toolRegistry)
        {
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// ?�로바이???�름?�니??
        /// </summary>
        public virtual string Name => "gemini-cli";

        /// <summary>
        /// ?당 ?공?용 ?큰 카운?? 가?옵?다.
        /// </summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>
        /// Gets the maximum context window size dynamically resolved from the active model.
        /// </summary>
        public int ContextLimit => GeminiProvider.ResolveGeminiContextLimit(AppState.ActiveModel);

        /// <summary>
        /// ????스?리??메시지?추??니??
        /// </summary>
        /// <param name="message">메시지 객체</param>
        public void AddMessage(object message)
        {
            if (message != null)
            {
                _conversationHistory.Add(message);
            }
        }

        /// <summary>
        /// ?�재 ?�???�스?�리�?반환?�니??
        /// </summary>
        /// <returns>메시지 목록</returns>
        public IReadOnlyList<object> GetHistory()
        {
            return _conversationHistory.AsReadOnly();
        }

        /// <summary>
        /// ?�???�스?�리�??�로??목록?�로 ?�체합?�다.
        /// </summary>
        /// <param name="history">?�체할 메시지 목록</param>
        public void SetHistory(IEnumerable<object> history)
        {
            _conversationHistory.Clear();
            if (history != null) _conversationHistory.AddRange(history);
        }

        /// <summary>
        /// gemini CLI�??�출?�여 ?�롬?�트�??�송?�고 출력???�트리밍?�니??
        /// XML ?�태???�구 ?�출???�시간으�?감�??�여 ?�벤?�로 발생?�킵?�다.
        /// </summary>
        /// <param name="prompt">?�용???�력 ?�롬?�트</param>
        /// <param name="model">모델�?/param>
        /// <param name="ct">?�업 취소 ?�큰</param>
        /// <returns>?�트리밍 ?�벤???�거??/returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", content = prompt });
            }

            var tools = _toolRegistry?.GetTools();

            // ?�구 ?�의 �??�용 규칙???�롬?�트???�함
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

            // ?�율 진화 ?�킬 ?�보 로드
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

            // 최종 ?�롬?�트 조합
            string combinedPrompt = $"{systemPrompt}\n\n[CRITICAL INSTRUCTION]\n반드??모든 ?�고(Thinking) 과정�?출력, ?�?? 분석 ?�용???�국??Korean)로만 ?�성?�세??\n\n{toolDefs}\n\n{historyDump}\n\n[CURRENT USER PROMPT]:\n{prompt}";

            string modelArg = !string.IsNullOrEmpty(model) ? $"--model \"{model}\" " : "";
            string arguments = $"/c agy {modelArg}-p \" \"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrEmpty(AppState.CurrentCwd) ? AppDomain.CurrentDomain.BaseDirectory : AppState.CurrentCwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            // CLI???�롬?�트 ?�력
            await process.StandardInput.WriteAsync(combinedPrompt);
            process.StandardInput.Close();

            var interceptedTools = new List<ToolUseRequest>();
            bool isBufferingTool = false;
            StringBuilder toolBuffer = new();
            string currentToolName = "";

            var fullText = new StringBuilder();

            process.ErrorDataReceived += (sender, args) => { /* ?�러 ?�레??*/ };
            process.BeginErrorReadLine();

            using var reader = process.StandardOutput;

            // CLI 출력 ?�시�??�싱 �??�벤???�송
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    process.Kill(true);
                    break;
                }

                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                // ANSI ?�스케?�프 코드 ?�거
                string cleanLine = System.Text.RegularExpressions.Regex.Replace(line, @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@~])", "");

                // 불필?�한 ?�림 문구 ?�제
                if (cleanLine.Contains("MCP issues detected"))
                {
                    cleanLine = cleanLine.Replace("MCP issues detected. ", "")
                                         .Replace("Run /mcp list for status.", "")
                                         .Replace("MCP issues detected.", "");
                }

                // <tool_call> ?�그 감�? �?버퍼�?로직
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

                // ?�구 ?�출 ?�성 ???�벤??발생
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
                        Console.WriteLine($"\n\x1b[1;31m?�️ JSON Parse Error (\x1b[33m{currentToolName}\x1b[1;31m):\x1b[0m {ex.Message}");
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
