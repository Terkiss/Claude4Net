using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Claude4Net.SDK
{
    /// <summary>
    /// LLM 컨텍스트 윈도우 관리를 위해 대화 기록이나 도구 실행 결과를 압축하고 요약하는 기능을 제공합니다.
    /// </summary>
    public class ContextCompressor
    {
        /// <summary>
        /// 전체 대화 기록을 분석하여 토큰 한계를 넘지 않도록 압축합니다. (현재 기본 구현)
        /// </summary>
        /// <param name="history">메시지 이력 리스트</param>
        /// <returns>압축된 메시지 이력 리스트</returns>
        public static List<object> Compress(List<object> history)
        {
            if (history.Count < 5) return history;
            // TODO: 실제 토큰 계산 기반의 압축 로직 구현 필요
            return history;
        }

        /// <summary>
        /// 여러 개의 도구 실행 결과가 연속될 경우, 이를 하나로 요약하여 문맥 길이를 줄입니다.
        /// </summary>
        /// <param name="toolResults">도구 결과 객체 리스트</param>
        /// <returns>요약된 텍스트가 포함된 리스트 또는 원본 리스트</returns>
        public static List<object> SummarizeToolResults(List<object> toolResults)
        {
            if (toolResults.Count > 3)
            {
                var summary = $"[Collapsed {toolResults.Count} tool results: {string.Join(", ", toolResults.Select(r => GetToolId(r)))}]";
                return new List<object> { new { type = "text", text = summary } };
            }
            return toolResults;
        }

        /// <summary>
        /// 도구 결과 객체에서 tool_use_id를 추출합니다.
        /// </summary>
        /// <param name="result">도구 결과 객체</param>
        /// <returns>추출된 ID 또는 "unknown"</returns>
        private static string GetToolId(object result)
        {
            try {
                var json = JsonSerializer.Serialize(result);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tool_use_id", out var idProp)) return idProp.GetString() ?? "unknown";
            } catch { }
            return "unknown";
        }
    }
}
