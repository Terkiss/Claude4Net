using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class ContextCompressor
    {
        public static List<object> Compress(List<object> history)
        {
            if (history.Count < 5) return history;

            var compressed = new List<object>();
            
            // To keep simple, we'll only look at tool results.
            // In a real implementation, we would group consecutive tool results.
            
            // Placeholder: Currently just returns original. 
            // Implementation of actual grouping logic follows.
            
            return history;
        }

        public static List<object> SummarizeToolResults(List<object> toolResults)
        {
            // If we have many tool results, we can collapse them.
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
