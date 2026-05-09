using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Claude4Net.SDK
{
    /// <summary>
    /// LLM Ïª®ÌÖç?§Ìä∏ ?àÎèÑ??Í¥ÄÎ¶¨Î? ?ÑÌï¥ ?Ä??Í∏∞Î°ù?¥ÎÇò ?ÑÍµ¨ ?§Ìñâ Í≤∞Í≥ºÎ•??ïÏ∂ï?òÍ≥† ?îÏïΩ?òÎäî Í∏∞Îä•???úÍ≥µ?©Îãà??
    /// </summary>
    public class ContextCompressor
    {
        /// <summary>
        /// ?ÑÏ≤¥ ?Ä??Í∏∞Î°ù??Î∂ÑÏÑù?òÏó¨ ?†ÌÅ∞ ?úÍ≥ÑÎ•??òÏ? ?äÎèÑÎ°??ïÏ∂ï?©Îãà??
        /// </summary>
        /// <param name="history">Î©îÏãúÏßÄ ?¥Î†• Î¶¨Ïä§??/param>
        /// <param name="counter">?†ÌÅ∞ Ïπ¥Ïö¥??/param>
        /// <param name="limit">?†ÌÅ∞ ?úÎèÑ</param>
        /// <returns>?ïÏ∂ï??Î©îÏãúÏßÄ ?¥Î†• Î¶¨Ïä§??/returns>
        public static List<object> Compress(List<object> history, ITokenCounter counter, int limit)
        {
            if (history == null || history.Count == 0) return new List<object>();

            int currentTokens = counter.CountTokens(history);
            // 80% ÎØ∏Îßå?¥Î©¥ ?ïÏ∂ï ????
            if (currentTokens < limit * 0.8) return history;

            int targetTokens = (int)(limit * 0.6);

            // ÏµúÍ∑º 5Í∞?Î©îÏãúÏßÄ????ÉÅ ?†Ï?
            int minPreserve = Math.Min(history.Count, 5);
            var tail = history.TakeLast(minPreserve).ToList();
            var headCandidates = history.Take(history.Count - minPreserve).ToList();

            var preservedHead = new List<object>();

            // ?ÑÍµ¨ ?∏Ï∂ú ??Ï∞æÍ∏∞ Î∞?Î≥¥Ï°¥
            // Anthropic/Gemini ?ïÏãù Î™®Îëê Í≥†Î†§ (ID Í∏∞Î∞ò Îß§Ïπ≠)
            var toolUseIds = new HashSet<string>();
            foreach (var msg in headCandidates)
            {
                ExtractToolIds(msg, toolUseIds);
            }

            foreach (var msg in headCandidates)
            {
                if (IsToolRelated(msg, toolUseIds))
                {
                    preservedHead.Add(msg);
                }
                else if (counter.CountTokens(preservedHead) + counter.CountTokens(tail) < targetTokens)
                {
                    // ?ÑÏßÅ ?¨Ïú†Í∞Ä ?àÏúºÎ©??ºÎ∞ò Î©îÏãúÏßÄ???ºÎ? ?†Ï? (?ûÎ?Î∂ÑÎ???
                    preservedHead.Add(msg);
                }
            }

            var result = preservedHead.Concat(tail).ToList();

            // ÎßåÏïΩ Í∑∏Îûò???úÎèÑÎ•??òÎäî?§Î©¥ (?ÑÍµ¨ ?∏Ï∂ú???àÎ¨¥ ÎßéÏùå)
            // Í∞Ä???§Îûò???ÑÍµ¨ ?∏Ï∂ú ?çÎ????úÍ±∞?òÍ±∞???çÏä§???îÏïΩ Ï≤òÎ¶¨ (?¨Í∏∞?úÎäî ?ºÎã® ?†Ï? ?ïÏ±Ö ?∞ÏÑ†)

            return result;
        }

        private static void ExtractToolIds(object message, HashSet<string> ids)
        {
            try
            {
                var json = JsonSerializer.Serialize(message);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Anthropic style
                if (root.TryGetProperty("content", out var content))
                {
                    if (content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in content.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var type) && type.GetString() == "tool_use")
                            {
                                ids.Add(item.GetProperty("id").GetString() ?? "");
                            }
                        }
                    }
                }
                // Gemini style
                if (root.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("functionCall", out var fc))
                        {
                            ids.Add(fc.GetProperty("name").GetString() ?? "");
                        }
                    }
                }
            }
            catch { }
        }

        private static bool IsToolRelated(object message, HashSet<string> toolIds)
        {
            try
            {
                var json = JsonSerializer.Serialize(message);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Anthropic style
                if (root.TryGetProperty("content", out var content))
                {
                    if (content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in content.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var type))
                            {
                                string t = type.GetString() ?? "";
                                if (t == "tool_use" && toolIds.Contains(item.GetProperty("id").GetString() ?? "")) return true;
                                if (t == "tool_result" && toolIds.Contains(item.GetProperty("tool_use_id").GetString() ?? "")) return true;
                            }
                        }
                    }
                }
                // Gemini style
                if (root.TryGetProperty("role", out var role))
                {
                    string r = role.GetString() ?? "";
                    if (r == "function" && root.TryGetProperty("parts", out var parts))
                    {
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("functionResponse", out var fr))
                            {
                                if (toolIds.Contains(fr.GetProperty("name").GetString() ?? "")) return true;
                            }
                        }
                    }
                    if (r == "model" && root.TryGetProperty("parts", out var parts2))
                    {
                        foreach (var part in parts2.EnumerateArray())
                        {
                            if (part.TryGetProperty("functionCall", out var fc))
                            {
                                if (toolIds.Contains(fc.GetProperty("name").GetString() ?? "")) return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// ?¨Îü¨ Í∞úÏùò ?ÑÍµ¨ ?§Ìñâ Í≤∞Í≥ºÍ∞Ä ?∞ÏÜç??Í≤ΩÏö∞, ?¥Î? ?òÎÇòÎ°??îÏïΩ?òÏó¨ Î¨∏Îß• Í∏∏Ïù¥Î•?Ï§ÑÏûÖ?àÎã§.
        /// </summary>
        /// <param name="toolResults">?ÑÍµ¨ Í≤∞Í≥º Í∞ùÏ≤¥ Î¶¨Ïä§??/param>
        /// <returns>?îÏïΩ???çÏä§?∏Í? ?¨Ìï®??Î¶¨Ïä§???êÎäî ?êÎ≥∏ Î¶¨Ïä§??/returns>
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
        /// ?ÑÍµ¨ Í≤∞Í≥º Í∞ùÏ≤¥?êÏÑú tool_use_idÎ•?Ï∂îÏ∂ú?©Îãà??
        /// </summary>
        /// <param name="result">?ÑÍµ¨ Í≤∞Í≥º Í∞ùÏ≤¥</param>
        /// <returns>Ï∂îÏ∂ú??ID ?êÎäî "unknown"</returns>
        private static string GetToolId(object result)
        {
            try {
                var json = JsonSerializer.Serialize(result);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tool_use_id", out var idProp)) return idProp.GetString() ?? "unknown";
                if (doc.RootElement.TryGetProperty("tool_use", out var tuProp)) return tuProp.GetProperty("id").GetString() ?? "unknown";
            } catch { }
            return "unknown";
        }
    }
}
