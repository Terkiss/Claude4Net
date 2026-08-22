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
        /// gemini CLI??출?여 ?롬?트??송?고 출력???트리밍?니??
        /// XML ?태???구 ?출???시간으?감??여 ?벤?로 발생?킵?다.
        /// </summary>
        /// <param name="prompt">?용???력 ?롬?트</param>
        /// <param name="model">모델?/param>
        /// <param name="ct">?업 취소 ?큰</param>
        /// <returns>?트리밍 ?벤???거??/returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", content = prompt });
            }

            var tools = _toolRegistry?.GetTools();
            bool isApiMode = (tools == null || tools.Count == 0);

            string combinedPrompt;
            string wsPath = !string.IsNullOrEmpty(AppState.CurrentCwd)
                ? AppState.CurrentCwd
                : (!string.IsNullOrEmpty(AppState.OriginalCwd) ? AppState.OriginalCwd : Environment.CurrentDirectory);

            if (isApiMode)
            {
                // In API Mode: Pure LLM passthrough for external clients (Hermes, Cursor, Roo Code, etc.)
                // The external client provides its own system prompt, workspace path, tools, and conversation history.
                combinedPrompt = prompt;
            }
            else
            {
                // In CLI Mode: Claude4Net internal agent loop
                var toolDefs = new StringBuilder();
                toolDefs.AppendLine("[AVAILABLE TOOLS]");
                foreach (var t in tools!)
                {
                    string schemaDoc = t.InputSchema != null ? System.Text.Json.JsonSerializer.Serialize(t.InputSchema, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) : "{}";
                    toolDefs.AppendLine($"- Name: {t.Name}");
                    toolDefs.AppendLine($"  Description: {t.Description}");
                    toolDefs.AppendLine($"  InputSchema: {schemaDoc}");
                    toolDefs.AppendLine();
                }
                toolDefs.AppendLine(@"[TOOL USE RULES]
You are connected to an external execution loop. DO NOT execute your internal tools or system commands directly.
If you need to use a tool, you MUST respond EXACTLY in this XML format:
<tool_call name=""ToolName"">
{ ""argName"": ""argValue"" }
</tool_call>
You can only call one tool per <tool_call> tag. After outputting a tool call, wait for the result.");

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

                string workspaceRules = $@"
[ACTIVE WORKSPACE DIRECTORY]
Path: {wsPath}

[CRITICAL WORKSPACE CONFINEMENT RULES]
1. All project files, source code, folders, and resources MUST be created and edited strictly INSIDE the active workspace directory: {wsPath}
2. NEVER create project directories or files in user home (C:\Users\...) or outside {wsPath}.
3. Always use relative paths from the workspace root or absolute paths inside {wsPath}.";

                combinedPrompt = $"{systemPrompt}\n\n[CRITICAL INSTRUCTION]\n반드시 모든 사고(Thinking) 과정과 출력, 코드 분석 내용은 한국어(Korean)로만 작성하세요.\n{workspaceRules}\n\n{toolDefs}\n\n{historyDump}\n\n[CURRENT USER PROMPT]:\n{prompt}";
            }

            string agyPath = ResolveAgyExecutable();

            var processStartInfo = new ProcessStartInfo
            {
                FileName = agyPath,
                WorkingDirectory = wsPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8
            };

            processStartInfo.ArgumentList.Add("--input-format");
            processStartInfo.ArgumentList.Add("stream-json");
            processStartInfo.ArgumentList.Add("--output-format");
            processStartInfo.ArgumentList.Add("stream-json");
            processStartInfo.ArgumentList.Add("--dangerously-skip-permissions");
            processStartInfo.ArgumentList.Add("--disable-slash-commands");

            string resolvedModel = NormalizeAgyModel(model);
            if (!string.IsNullOrEmpty(resolvedModel))
            {
                processStartInfo.ArgumentList.Add("--model");
                processStartInfo.ArgumentList.Add(resolvedModel);
            }
            if (!isApiMode && !string.IsNullOrEmpty(AppState.CurrentCwd))
            {
                processStartInfo.ArgumentList.Add("--add-dir");
                processStartInfo.ArgumentList.Add(AppState.CurrentCwd);
            }

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            // Write input prompt over stdin (supports arbitrary size without 32KB Windows CLI limit)
            var inputMessage = new
            {
                @event = "user",
                message = new
                {
                    role = "user",
                    content = combinedPrompt
                }
            };
            string inputJson = System.Text.Json.JsonSerializer.Serialize(inputMessage) + "\n";
            await process.StandardInput.WriteAsync(inputJson);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            var interceptedTools = new List<ToolUseRequest>();
            bool isBufferingTool = false;
            StringBuilder toolBuffer = new();
            string currentToolName = "";

            var fullText = new StringBuilder();
            var stdErrBuffer = new StringBuilder();

            process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    stdErrBuffer.AppendLine(args.Data);
                }
            };
            process.BeginErrorReadLine();

            using var reader = process.StandardOutput;

            // CLI 출력 ?시??싱 ??벤???송
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    process.Kill(true);
                    break;
                }

                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                string cleanLine = line.Trim();
                if (string.IsNullOrEmpty(cleanLine)) continue;

                string textToProcess = cleanLine;

                // Try parsing stream-json event
                if (cleanLine.StartsWith("{") && cleanLine.EndsWith("}"))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(cleanLine);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("event", out var eventProp))
                        {
                            string eventType = eventProp.GetString() ?? "";
                            if (eventType == "step_update" && root.TryGetProperty("step_update", out var stepUpdate))
                            {
                                if (stepUpdate.TryGetProperty("text_delta", out var textDeltaProp))
                                {
                                    textToProcess = textDeltaProp.GetString() ?? "";
                                }
                                else
                                {
                                    continue;
                                }
                            }
                            else if (eventType == "result" && root.TryGetProperty("result", out var resultProp))
                            {
                                if (resultProp.TryGetProperty("status", out var statusProp) &&
                                    statusProp.GetString() == "ERROR" &&
                                    resultProp.TryGetProperty("error", out var errProp))
                                {
                                    stdErrBuffer.AppendLine(errProp.GetString());
                                }
                                continue;
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback to raw line processing
                    }
                }

                // ANSI ?스케?프 코드 ?거
                textToProcess = System.Text.RegularExpressions.Regex.Replace(textToProcess, @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@~])", "");

                // 불필?한 ?림 문구 ?제
                if (textToProcess.Contains("MCP issues detected"))
                {
                    textToProcess = textToProcess.Replace("MCP issues detected. ", "")
                                                 .Replace("Run /mcp list for status.", "")
                                                 .Replace("MCP issues detected.", "");
                }

                // <tool_call> ?그 감? ?버퍼?로직
                if (!isBufferingTool)
                {
                    var matchStart = System.Text.RegularExpressions.Regex.Match(textToProcess, @"<tool_call[ \t]+name\s*=\s*""([^""]+)""\s*>");
                    if (matchStart.Success)
                    {
                        isBufferingTool = true;
                        currentToolName = matchStart.Groups[1].Value;
                        string precedingText = textToProcess.Substring(0, matchStart.Index);

                        if (!string.IsNullOrWhiteSpace(precedingText))
                        {
                            fullText.Append(precedingText);
                            yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = precedingText };
                        }

                        string remainder = textToProcess.Substring(matchStart.Index + matchStart.Length);
                        toolBuffer.Clear();

                        var matchEnd = System.Text.RegularExpressions.Regex.Match(remainder, @"</tool_call>");
                        if (matchEnd.Success)
                        {
                            isBufferingTool = false;
                            toolBuffer.AppendLine(remainder.Substring(0, matchEnd.Index));
                            textToProcess = remainder.Substring(matchEnd.Index + matchEnd.Length);
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
                    var matchEnd = System.Text.RegularExpressions.Regex.Match(textToProcess, @"</tool_call>");
                    if (matchEnd.Success)
                    {
                        isBufferingTool = false;
                        toolBuffer.AppendLine(textToProcess.Substring(0, matchEnd.Index));
                        textToProcess = textToProcess.Substring(matchEnd.Index + matchEnd.Length);
                    }
                    else
                    {
                        toolBuffer.AppendLine(textToProcess);
                        continue;
                    }
                }

                // ?구 ?출 ?성 ???벤??발생
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
                        Console.WriteLine($"\n\x1b[1;31m?️ JSON Parse Error (\x1b[33m{currentToolName}\x1b[1;31m):\x1b[0m {ex.Message}");
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

                    if (string.IsNullOrEmpty(textToProcess))
                        continue;
                }

                if (string.IsNullOrEmpty(textToProcess))
                    continue;

                fullText.Append(textToProcess);

                yield return new LLMStreamEvent
                {
                    Type = LLMStreamEventType.TextDelta,
                    Delta = textToProcess
                };
            }

            await process.WaitForExitAsync(ct);

            string finalOutput = fullText.ToString().TrimEnd();

            if (string.IsNullOrEmpty(finalOutput) && stdErrBuffer.Length > 0)
            {
                string err = stdErrBuffer.ToString().Trim();
                finalOutput = $"[Error from agy]: {err}";
                yield return new LLMStreamEvent
                {
                    Type = LLMStreamEventType.TextDelta,
                    Delta = finalOutput + "\n"
                };
            }

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

        private static string ResolveAgyExecutable()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string defaultAgy = Path.Combine(localAppData, "agy", "bin", "agy.exe");
            if (File.Exists(defaultAgy))
            {
                return defaultAgy;
            }
            return "agy";
        }

        private static string NormalizeAgyModel(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return string.Empty;
            string key = model.Trim().ToLowerInvariant();
            if (key.StartsWith("antigravity/"))
            {
                key = key["antigravity/".Length..].Trim();
            }
            else if (key.StartsWith("agy/"))
            {
                key = key["agy/".Length..].Trim();
            }

            return key switch
            {
                "gemini-3.7-flash-high" or "gemini 3.7 flash (high)" => "Gemini 3.7 Flash (High)",
                "gemini-3.7-flash-medium" or "gemini-3.7-flash-med" or "gemini 3.7 flash (medium)" => "Gemini 3.7 Flash (Medium)",
                "gemini-3.7-flash-low" or "gemini 3.7 flash (low)" => "Gemini 3.7 Flash (Low)",
                "gemini-3.7-flash" or "gemini 3.7 flash" => "Gemini 3.7 Flash (High)",
                "gemini-3.6-flash-high" or "gemini 3.6 flash (high)" => "Gemini 3.6 Flash (High)",
                "gemini-3.6-flash-medium" or "gemini-3.6-flash-med" or "gemini 3.6 flash (medium)" => "Gemini 3.6 Flash (Medium)",
                "gemini-3.6-flash-low" or "gemini 3.6 flash (low)" => "Gemini 3.6 Flash (Low)",
                "gemini-3.6-flash" or "gemini 3.6 flash" => "Gemini 3.6 Flash (High)",
                "gemini-3.5-flash-high" or "gemini 3.5 flash (high)" => "Gemini 3.5 Flash (High)",
                "gemini-3.5-flash-medium" or "gemini-3.5-flash-med" or "gemini 3.5 flash (medium)" => "Gemini 3.5 Flash (Medium)",
                "gemini-3.5-flash-low" or "gemini 3.5 flash (low)" => "Gemini 3.5 Flash (Low)",
                "gemini-3.5-flash" or "gemini 3.5 flash" => "Gemini 3.5 Flash (High)",
                "gemini-3.1-pro-high" or "gemini 3.1 pro (high)" => "Gemini 3.1 Pro (High)",
                "gemini-3.1-pro-low" or "gemini 3.1 pro (low)" => "Gemini 3.1 Pro (Low)",
                "gemini-3.1-pro" or "gemini 3.1 pro" => "Gemini 3.1 Pro (High)",
                "claude-sonnet-4-6-thinking" or "claude-sonnet-4-6" or "claude-sonnet-4.6" or "claude sonnet 4.6 (thinking)" => "Claude Sonnet 4.6 (Thinking)",
                "claude-opus-4-6-thinking" or "claude-opus-4-6" or "claude-opus-4.6" or "claude opus 4.6 (thinking)" => "Claude Opus 4.6 (Thinking)",
                "gpt-oss-120b-high" or "gpt-oss 120b (high)" => "GPT-OSS 120B (High)",
                "gpt-oss-120b-medium" or "gpt-oss-120b-med" or "gpt-oss-120b" or "gpt-oss 120b (medium)" => "GPT-OSS 120B (Medium)",
                _ => model
            };
        }
    }
}
