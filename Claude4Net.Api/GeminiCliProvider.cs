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
    /// ?¸ë? 'gemini' CLI ?„êµ¬ë¥??„ë¡œ?¸ìŠ¤ë¡??¤í–‰?˜ì—¬ Google Gemini ëª¨ë¸??ê¸°ëŠ¥???€?‰í•˜???„ë¡œë°”ì´?”ì…?ˆë‹¤.
    /// CLI ì¶œë ¥??ìº¡ì²˜?˜ê³  XML ?•ì‹???„êµ¬ ?¸ì¶œ???Œì‹±?˜ì—¬ ì²˜ë¦¬?©ë‹ˆ??
    /// </summary>
    public class GeminiCliProvider : ILLMProvider
    {
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// GeminiCliProvider?????¸ìŠ¤?´ìŠ¤ë¥?ì´ˆê¸°?”í•©?ˆë‹¤.
        /// </summary>
        /// <param name="toolRegistry">?„êµ¬ ?•ë³´ ?ˆì??¤íŠ¸ë¦?/param>
        public GeminiCliProvider(IToolRegistry toolRegistry)
        {
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// ?„ë¡œë°”ì´???´ë¦„?…ë‹ˆ??
        /// </summary>
        public string Name => "gemini-cli";

        /// <summary>
        /// ?´ë‹¹ ?œê³µ?ìš© ? í° ì¹´ìš´?°ë? ê°€?¸ì˜µ?ˆë‹¤.
        /// </summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>
        /// ?´ë‹¹ ?œê³µ?ì˜ ?„ì¬ ëª¨ë¸ ì»¨í…?¤íŠ¸ ?œí•œ??ê°€?¸ì˜µ?ˆë‹¤. (Gemini 1.5 ê¸°ì? 1M)
        /// </summary>
        public int ContextLimit => 1000000;

        /// <summary>
        /// ?€???ˆìŠ¤? ë¦¬??ë©”ì‹œì§€ë¥?ì¶”ê??©ë‹ˆ??
        /// </summary>
        /// <param name="message">ë©”ì‹œì§€ ê°ì²´</param>
        public void AddMessage(object message)
        {
            if (message != null)
            {
                _conversationHistory.Add(message);
            }
        }

        /// <summary>
        /// ?„ì¬ ?€???ˆìŠ¤? ë¦¬ë¥?ë°˜í™˜?©ë‹ˆ??
        /// </summary>
        /// <returns>ë©”ì‹œì§€ ëª©ë¡</returns>
        public IReadOnlyList<object> GetHistory()
        {
            return _conversationHistory.AsReadOnly();
        }

        /// <summary>
        /// ?€???ˆìŠ¤? ë¦¬ë¥??ˆë¡œ??ëª©ë¡?¼ë¡œ ?€ì²´í•©?ˆë‹¤.
        /// </summary>
        /// <param name="history">?€ì²´í•  ë©”ì‹œì§€ ëª©ë¡</param>
        public void SetHistory(IEnumerable<object> history)
        {
            _conversationHistory.Clear();
            if (history != null) _conversationHistory.AddRange(history);
        }

        /// <summary>
        /// gemini CLIë¥??¸ì¶œ?˜ì—¬ ?„ë¡¬?„íŠ¸ë¥??„ì†¡?˜ê³  ì¶œë ¥???¤íŠ¸ë¦¬ë°?©ë‹ˆ??
        /// XML ?•íƒœ???„êµ¬ ?¸ì¶œ???¤ì‹œê°„ìœ¼ë¡?ê°ì??˜ì—¬ ?´ë²¤?¸ë¡œ ë°œìƒ?œí‚µ?ˆë‹¤.
        /// </summary>
        /// <param name="prompt">?¬ìš©???…ë ¥ ?„ë¡¬?„íŠ¸</param>
        /// <param name="model">ëª¨ë¸ëª?/param>
        /// <param name="ct">?‘ì—… ì·¨ì†Œ ? í°</param>
        /// <returns>?¤íŠ¸ë¦¬ë° ?´ë²¤???´ê±°??/returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", content = prompt });
            }

            var tools = _toolRegistry?.GetTools();

            // ?„êµ¬ ?•ì˜ ë°??¬ìš© ê·œì¹™???„ë¡¬?„íŠ¸???¬í•¨
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

            // ?ìœ¨ ì§„í™” ?¤í‚¬ ?•ë³´ ë¡œë“œ
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

            // ìµœì¢… ?„ë¡¬?„íŠ¸ ì¡°í•©
            string combinedPrompt = $"{systemPrompt}\n\n[CRITICAL INSTRUCTION]\në°˜ë“œ??ëª¨ë“  ?¬ê³ (Thinking) ê³¼ì •ê³?ì¶œë ¥, ?€?? ë¶„ì„ ?´ìš©???œêµ­??Korean)ë¡œë§Œ ?‘ì„±?˜ì„¸??\n\n{toolDefs}\n\n{historyDump}\n\n[CURRENT USER PROMPT]:\n{prompt}";

            string modelArg = !string.IsNullOrEmpty(model) ? $"-m \"{model}\" " : "";
            string arguments = $"/c gemini {modelArg}-y -p \" \"";

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

            // CLI???„ë¡¬?„íŠ¸ ?…ë ¥
            await process.StandardInput.WriteAsync(combinedPrompt);
            process.StandardInput.Close();

            var interceptedTools = new List<ToolUseRequest>();
            bool isBufferingTool = false;
            StringBuilder toolBuffer = new();
            string currentToolName = "";

            var fullText = new StringBuilder();

            process.ErrorDataReceived += (sender, args) => { /* ?ëŸ¬ ?œë ˆ??*/ };
            process.BeginErrorReadLine();

            using var reader = process.StandardOutput;

            // CLI ì¶œë ¥ ?¤ì‹œê°??Œì‹± ë°??´ë²¤???„ì†¡
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    process.Kill(true);
                    break;
                }

                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                // ANSI ?´ìŠ¤ì¼€?´í”„ ì½”ë“œ ?œê±°
                string cleanLine = System.Text.RegularExpressions.Regex.Replace(line, @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@~])", "");

                // ë¶ˆí•„?”í•œ ?Œë¦¼ ë¬¸êµ¬ ?•ì œ
                if (cleanLine.Contains("MCP issues detected"))
                {
                    cleanLine = cleanLine.Replace("MCP issues detected. ", "")
                                         .Replace("Run /mcp list for status.", "")
                                         .Replace("MCP issues detected.", "");
                }

                // <tool_call> ?œê·¸ ê°ì? ë°?ë²„í¼ë§?ë¡œì§
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

                // ?„êµ¬ ?¸ì¶œ ?„ì„± ???´ë²¤??ë°œìƒ
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
                        Console.WriteLine($"\n\x1b[1;31m? ï¸ JSON Parse Error (\x1b[33m{currentToolName}\x1b[1;31m):\x1b[0m {ex.Message}");
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
