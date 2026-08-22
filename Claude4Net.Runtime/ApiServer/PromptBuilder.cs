using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Claude4Net.Runtime.ApiServer.Models;

namespace Claude4Net.Runtime.ApiServer
{
    /// <summary>
    /// Builds LLM prompts from OpenAI-format chat messages, tools, and response format directives.
    /// </summary>
    public static class PromptBuilder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <summary>
        /// Converts a list of chat messages (+ optional tools and response_format) into a single prompt string.
        /// </summary>
        public static string BuildFromMessages(List<ChatMessageDto> messages, List<ToolDto>? tools = null, ResponseFormatDto? responseFormat = null)
        {
            var sb = new StringBuilder();

            // 1. Structured JSON output instructions
            if (responseFormat != null)
            {
                if (string.Equals(responseFormat.Type, "json_object", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("[SYSTEM]: You MUST format your response as a valid JSON object.\n");
                }
                else if (string.Equals(responseFormat.Type, "json_schema", StringComparison.OrdinalIgnoreCase) && responseFormat.JsonSchema != null)
                {
                    sb.AppendLine($"[SYSTEM]: You MUST format your response as a valid JSON object strictly matching this schema: {JsonSerializer.Serialize(responseFormat.JsonSchema, JsonOptions)}\n");
                }
            }

            // 2. Tools instructions
            if (tools != null && tools.Count > 0)
            {
                sb.AppendLine("[SYSTEM]: You have access to the following tools:");
                foreach (var tool in tools)
                {
                    sb.AppendLine($"- Tool: {tool.Function.Name}");
                    if (!string.IsNullOrEmpty(tool.Function.Description))
                        sb.AppendLine($"  Description: {tool.Function.Description}");
                    if (tool.Function.Parameters != null)
                        sb.AppendLine($"  Parameters: {JsonSerializer.Serialize(tool.Function.Parameters, JsonOptions)}");
                }
                sb.AppendLine("To invoke a tool, output: " + GetToolInvocationFormat() + "\n");
            }

            if (messages.Count == 1 && (tools == null || tools.Count == 0) && responseFormat == null)
            {
                return messages[0].GetContentString();
            }

            foreach (var msg in messages)
            {
                sb.AppendLine($"[{msg.Role.ToUpperInvariant()}]: {msg.GetContentString()}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns the tool invocation format string (separated to avoid XML-like literals in source).
        /// </summary>
        private static string GetToolInvocationFormat()
        {
            return "<invoke name=\"tool_name\">" +
                   "<parameter name=\"param_name\">value" +
                   "</parameter>" +
                   "</invoke>";
        }
    }
}
