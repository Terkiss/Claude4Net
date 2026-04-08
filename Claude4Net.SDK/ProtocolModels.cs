using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude4Net.SDK
{
    public class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
        [JsonPropertyName("id")]
        public object? Id { get; set; }
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;
        [JsonPropertyName("params")]
        public object? Params { get; set; }
    }

    public class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
        [JsonPropertyName("id")]
        public object? Id { get; set; }
        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }
        [JsonPropertyName("error")]
        public JsonElement? Error { get; set; }
    }

    public class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        [JsonPropertyName("data")]
        public object? Data { get; set; }
    }

    public class McpTool
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        [JsonPropertyName("inputSchema")]
        public JsonElement InputSchema { get; set; }
    }

    public class McpCallToolResult
    {
        [JsonPropertyName("content")]
        public List<McpContent> Content { get; set; } = new();
        [JsonPropertyName("isError")]
        public bool IsError { get; set; }
    }

    public class McpContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
