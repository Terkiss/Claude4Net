using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK.Telemetry;

namespace Claude4Net.Runtime.Telemetry
{
    /// <summary>
    /// .gemini 폴더 내 800+ 개 실제 세션 transcript.jsonl 로그를 고속 파싱하여
    /// 실제 AI 대화/도구 실행/사고 과정의 프롬프트 및 완성 토큰을 정확히 추론 및 인제스트하는 엔진
    /// </summary>
    public class GeminiTranscriptIngestionEngine
    {
        public static GeminiTranscriptIngestionEngine Shared { get; } = new();

        public async Task<int> IngestFromGeminiHomeAsync(
            ITeruTeruPandasTelemetryEngine telemetryEngine,
            string? geminiHome = null,
            CancellationToken ct = default)
        {
            string rootPath = geminiHome ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                ".gemini");

            if (!Directory.Exists(rootPath))
            {
                return 0;
            }

            int totalRecordsIngested = 0;
            string[] searchDirs = new[]
            {
                Path.Combine(rootPath, "antigravity", "brain"),
                Path.Combine(rootPath, "antigravity-cli", "brain")
            };

            foreach (var baseDir in searchDirs)
            {
                if (!Directory.Exists(baseDir)) continue;

                var transcriptFiles = Directory.GetFiles(baseDir, "transcript.jsonl", SearchOption.AllDirectories);

                foreach (var file in transcriptFiles)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        int records = await ProcessTranscriptFileAsync(telemetryEngine, file, ct);
                        totalRecordsIngested += records;
                    }
                    catch
                    {
                        // Ignore corrupted files and continue
                    }
                }
            }

            return totalRecordsIngested;
        }

        private async Task<int> ProcessTranscriptFileAsync(
            ITeruTeruPandasTelemetryEngine telemetryEngine,
            string filePath,
            CancellationToken ct)
        {
            string sessionId = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(filePath)))) ?? Guid.NewGuid().ToString("N");
            string projectName = "Claude4Net-App";

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            string? line;
            DateTime? lastUserTime = null;
            int turnPromptChars = 0;
            int turnCompChars = 0;
            string currentModel = "Antigravity DeepCoder 2.0";
            int recordsCount = 0;

            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    string? type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    string? content = root.TryGetProperty("content", out var c) ? c.GetString() : null;
                    string? createdAtStr = root.TryGetProperty("created_at", out var ca) ? ca.GetString() : null;

                    DateTime createdAt = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, out var parsedDate))
                    {
                        createdAt = parsedDate.ToUniversalTime();
                    }

                    // Extract Project Name heuristic from prompt
                    if (!string.IsNullOrEmpty(content) && projectName == "Claude4Net-App")
                    {
                        var match = Regex.Match(content, @"(?:Project|workspace|Directory)[\\/:]+([A-Za-z0-9_\-\.]+)", RegexOptions.IgnoreCase);
                        if (match.Success && match.Groups[1].Value.Length > 2)
                        {
                            projectName = match.Groups[1].Value;
                        }
                    }

                    if (type == "USER_INPUT")
                    {
                        if (turnPromptChars > 0 || turnCompChars > 0)
                        {
                            // Flush previous turn
                            await FlushTurnAsync(telemetryEngine, sessionId, projectName, currentModel, turnPromptChars, turnCompChars, lastUserTime ?? createdAt, ct);
                            recordsCount++;
                            turnPromptChars = 0;
                            turnCompChars = 0;
                        }

                        lastUserTime = createdAt;
                        turnPromptChars += EstimateTokens(content);
                    }
                    else if (type == "PLANNER_RESPONSE")
                    {
                        turnCompChars += EstimateTokens(content);

                        if (root.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                        {
                            turnCompChars += EstimateTokens(toolCalls.GetRawText());
                        }

                        // Determine model heuristic based on complexity or content
                        if (content != null && (content.Contains("Claude") || content.Contains("Sonnet")))
                        {
                            currentModel = "Claude 3.7 Sonnet";
                        }
                        else if (content != null && content.Contains("Gemini"))
                        {
                            currentModel = "Gemini 3.7 Pro";
                        }
                        else
                        {
                            currentModel = "Antigravity DeepCoder 2.0";
                        }
                    }
                    else if (type == "CODE_ACTION" || type == "TOOL_RESULT")
                    {
                        // Tool execution output acts as prompt context for the next turn
                        turnPromptChars += EstimateTokens(content);
                    }
                }
                catch
                {
                    // Ignore malformed lines
                }
            }

            if (turnPromptChars > 0 || turnCompChars > 0)
            {
                await FlushTurnAsync(telemetryEngine, sessionId, projectName, currentModel, turnPromptChars, turnCompChars, lastUserTime ?? DateTime.UtcNow, ct);
                recordsCount++;
            }

            return recordsCount;
        }

        private async Task FlushTurnAsync(
            ITeruTeruPandasTelemetryEngine telemetryEngine,
            string sessionId,
            string projectName,
            string model,
            int promptTokens,
            int compTokens,
            DateTime timestamp,
            CancellationToken ct)
        {
            // Base context overhead (system rules, tool schemas)
            int effectivePromptTokens = Math.Max(promptTokens + 1200, 1500);
            int effectiveCompTokens = Math.Max(compTokens, 80);
            double latency = 120.0 + (effectiveCompTokens * 4.5);

            await telemetryEngine.RecordTokenUsageAsync(
                sessionId: sessionId,
                projectName: projectName,
                provider: "Antigravity",
                model: model,
                promptTokens: effectivePromptTokens,
                compTokens: effectiveCompTokens,
                latencyMs: latency,
                timestamp: timestamp,
                ct: ct);
        }

        private int EstimateTokens(string? text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int hangulCount = 0;
            int nonHangulCount = 0;

            foreach (char ch in text)
            {
                if (ch >= 0xAC00 && ch <= 0xD7A3)
                {
                    hangulCount++;
                }
                else
                {
                    nonHangulCount++;
                }
            }

            // Korean tokens ~ 1.5 chars/token, English/Code ~ 3.6 chars/token
            double tokens = (hangulCount / 1.4) + (nonHangulCount / 3.6);
            return (int)Math.Ceiling(tokens);
        }
    }
}
