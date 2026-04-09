using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Claude4Net.SDK
{
    public class ContextCompressor
    {
        public static List<object> Compress(List<object> history)
        {
            if (history.Count < 5) return history;
            return history;
        }

        public static List<object> SummarizeToolResults(List<object> toolResults)
        {
            if (toolResults.Count > 3)
            {
                var summary = $"[Collapsed {toolResults.Count} tool results: {string.Join(", ", toolResults.Select(r => GetToolId(r)))}]";
                return new List<object> { new { type = "text", text = summary } };
            }
            return toolResults;
        }

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
