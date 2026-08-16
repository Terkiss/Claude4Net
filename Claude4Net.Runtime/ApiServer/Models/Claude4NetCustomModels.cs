using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Claude4Net.Runtime.ApiServer.Models
{
    public class ApiStatusResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "healthy";

        [JsonPropertyName("service")]
        public string Service { get; set; } = "Claude4Net OpenAI-Compatible In-Process API Server";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "5.4.0";

        [JsonPropertyName("port")]
        public int Port { get; set; } = 7836;

        [JsonPropertyName("active_provider")]
        public string ActiveProvider { get; set; } = string.Empty;

        [JsonPropertyName("active_model")]
        public string ActiveModel { get; set; } = string.Empty;

        [JsonPropertyName("permission_mode")]
        public string PermissionMode { get; set; } = string.Empty;

        [JsonPropertyName("workspace")]
        public string Workspace { get; set; } = string.Empty;

        [JsonPropertyName("uptime_seconds")]
        public double UptimeSeconds { get; set; }
    }

    public class ApiUsageResponse
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;

        [JsonPropertyName("total_calls")]
        public int TotalCalls { get; set; }

        [JsonPropertyName("input_tokens")]
        public long InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public long OutputTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public long TotalTokens => InputTokens + OutputTokens;

        [JsonPropertyName("total_cost")]
        public double TotalCost { get; set; }

        [JsonPropertyName("context_limit")]
        public int ContextLimit { get; set; }

        [JsonPropertyName("current_context_tokens")]
        public int CurrentContextTokens { get; set; }

        [JsonPropertyName("remaining_tokens")]
        public int RemainingTokens => Math.Max(0, ContextLimit - CurrentContextTokens);

        [JsonPropertyName("usage_percentage")]
        public double UsagePercentage => ContextLimit > 0 ? Math.Round((double)CurrentContextTokens / ContextLimit * 100.0, 2) : 0.0;

        [JsonPropertyName("context_components")]
        public Dictionary<string, int> ContextComponents { get; set; } = new();
    }

    public class AgentRunRequest
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;
    }

    public class AgentRunResponse
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;

        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;

        [JsonPropertyName("turns")]
        public int Turns { get; set; }

        [JsonPropertyName("duration_ms")]
        public double DurationMs { get; set; }
    }

    public class ToolItemDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("read_only")]
        public bool ReadOnly { get; set; }
    }

    public class SkillItemDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;
    }
}
