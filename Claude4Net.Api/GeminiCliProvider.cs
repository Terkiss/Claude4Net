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

            var systemPrompt = "";

            // CLI 명령줄에서 --system 같은 전용 인자가 제공되지 않으므로, 
            // 시스템 프롬프트를 일반 프롬프트 위쪽에 결합하여 전달합니다.
            // 추가로 모든 출력과 사고 과정(Thinking)을 한국어로 진행하도록 강제 지시를 덧붙입니다.
            string combinedPrompt = $"{systemPrompt}\n\n[CRITICAL INSTRUCTION]\n반드시 모든 사고(Thinking) 과정과 출력, 대답, 분석 내용을 한국어(Korean)로만 작성하세요.\n\n[User Prompt]:\n{prompt}";

            // cmd.exe의 인자 길이 제한 및 개행 문자(\n) 잘림 문제를 해결하기 위해,
            // -p 인자는 비워두고 긴 멀티라인 프롬프트를 표준 입력(StandardInput) 스트림으로 안전하게 밀어넣습니다.
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

            // 문자열이 길거나 개행이 포함되어도 안전하게 전달됩니다.
            await process.StandardInput.WriteAsync(combinedPrompt);
            process.StandardInput.Close(); // 표준 입력을 닫아서 입력 스트림이 끝났음을 알림


            var fullText = new StringBuilder();
            using var reader = process.StandardOutput;

            while (!reader.EndOfStream)
            {
                if (ct.IsCancellationRequested)
                {
                    process.Kill(true);
                    break;
                }

                // 텍스트를 한 줄씩 또는 버퍼로 읽어서 1차 가공 후 스트리밍
                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                // ANSI Escape 코드 제거 (콘솔 제어 문자 등으로 인해 매칭이 안되는 현상 방지)
                string cleanLine = System.Text.RegularExpressions.Regex.Replace(line, @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", "");

                // 불필요한 CLI 경고/시스템 안내 메시지가 첫 줄에 섞여 나오는 것 교정
                if (cleanLine.Contains("MCP issues detected"))
                {
                    cleanLine = cleanLine.Replace("MCP issues detected. ", "");
                    cleanLine = cleanLine.Replace("Run /mcp list for status.", "");
                    cleanLine = cleanLine.Replace("MCP issues detected.", "");
                }

                // CLI 경고 등을 필터링하고 남은 알맹이가 하얀 공백뿐이라면 쓸데없는 개행을 내보내지 않고 무시
                if (string.IsNullOrWhiteSpace(cleanLine))
                    continue;

                // AI의 응답 델타가 있을 경우에만 이벤트 방출
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

            // 응답 내용은 이후 히스토리 진행을 위해 추가
            if (!string.IsNullOrEmpty(finalOutput))
            {
                _conversationHistory.Add(new { role = "model", content = finalOutput });
            }

            yield return new LLMStreamEvent
            {
                Type = LLMStreamEventType.Completed,
                FinalResponse = new LLMResponse { Text = finalOutput }
            };
        }
    }
}
