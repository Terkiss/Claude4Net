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
    /// 외부 'gemini' CLI 도구를 프로세스로 실행하여 Google Gemini 모델의 기능을 대행하는 프로바이더입니다.
    /// CLI 출력을 캡처하고 XML 형식의 도구 호출을 파싱하여 처리합니다.
    /// </summary>
    public class GeminiCliProvider : ILLMProvider
    {
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// GeminiCliProvider의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="toolRegistry">도구 정보 레지스트리</param>
        public GeminiCliProvider(IToolRegistry toolRegistry)
        {
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// 프로바이더 이름입니다.
        /// </summary>
        public string Name => "gemini-cli";

        /// <summary>
        /// 대화 히스토리에 메시지를 추가합니다.
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
        /// 현재 대화 히스토리를 반환합니다.
        /// </summary>
        /// <returns>메시지 목록</returns>
        public IReadOnlyList<object> GetHistory()
        {
            return _conversationHistory.AsReadOnly();
        }

        /// <summary>
        /// gemini CLI를 호출하여 프롬프트를 전송하고 출력을 스트리밍합니다. 
        /// XML 형태의 도구 호출을 실시간으로 감지하여 이벤트로 발생시킵니다.
        /// </summary>
        /// <param name="prompt">사용자 입력 프롬프트</param>
        /// <param name="model">모델명</param>
        /// <param name="ct">작업 취소 토큰</param>
        /// <returns>스트리밍 이벤트 열거자</returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", content = prompt });
            }

            var tools = _toolRegistry?.GetTools();

            // 도구 정의 및 사용 규칙을 프롬프트에 포함
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
            
            // 자율 진화 스킬 정보 로드
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

            // 최종 프롬프트 조합
            string combinedPrompt = $"{systemPrompt}\n\n[CRITICAL INSTRUCTION]\n반드시 모든 사고(Thinking) 과정과 출력, 대답, 분석 내용을 한국어(Korean)로만 작성하세요.\n\n{toolDefs}\n\n{historyDump}\n\n[CURRENT USER PROMPT]:\n{prompt}";

            var (fileName, arguments) = GetExecutionCommand(model);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
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

            // CLI에 프롬프트 입력
            await process.StandardInput.WriteAsync(combinedPrompt);
            process.StandardInput.Close();

            var interceptedTools = new List<ToolUseRequest>();
            bool isBufferingTool = false;
            StringBuilder toolBuffer = new();
            string currentToolName = "";

            var fullText = new StringBuilder();

            process.ErrorDataReceived += (sender, args) => { /* 에러 드레인 */ };
            process.BeginErrorReadLine();

            using var reader = process.StandardOutput;

            // CLI 출력 실시간 파싱 및 이벤트 전송
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    process.Kill(true);
                    break;
                }

                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                // ANSI 이스케이프 코드 제거
                string cleanLine = System.Text.RegularExpressions.Regex.Replace(line, @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@~])", "");

                // 불필요한 알림 문구 정제
                if (cleanLine.Contains("MCP issues detected"))
                {
                    cleanLine = cleanLine.Replace("MCP issues detected. ", "")
                                         .Replace("Run /mcp list for status.", "")
                                         .Replace("MCP issues detected.", "");
                }

                // <tool_call> 태그 감지 및 버퍼링 로직
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

                // 도구 호출 완성 시 이벤트 발생
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

        /// <summary>
        /// 현재 운영체제에 적합한 gemini CLI 실행 명령어와 인자를 반환합니다.
        /// (macOS support prepared, native verification pending)
        /// </summary>
        public static (string FileName, string Arguments) GetExecutionCommand(string? model)
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            return GetExecutionCommand(model, isWindows);
        }

        /// <summary>
        /// 특정 플랫폼에 대한 gemini CLI 실행 명령어 구성을 반환합니다. (테스트용)
        /// </summary>
        public static (string FileName, string Arguments) GetExecutionCommand(string? model, bool isWindows)
        {
            if (isWindows)
            {
                string modelArg = !string.IsNullOrEmpty(model) ? $"-m \"{model}\" " : "";
                return ("cmd.exe", $"/c gemini {modelArg}-y -p \" \"");
            }
            else
            {
                // Unix: Use single quotes inside -lc "..." to avoid double quote breakage from inner modelArg
                // Native macOS verification pending
                string modelArg = !string.IsNullOrEmpty(model) ? $"-m {QuoteForUnixShell(model)} " : "";
                return ("/bin/bash", $"-lc \"gemini {modelArg}-y -p ' '\"");
            }
        }

        private static string QuoteForUnixShell(string value)
        {
            return $"'{value.Replace("'", "'\\''")}'";
        }
    }
}
