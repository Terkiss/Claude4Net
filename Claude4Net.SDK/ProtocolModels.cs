using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude4Net.SDK
{
    /// <summary>
    /// JSON-RPC 2.0 요청 모델입니다. MCP(Model Context Protocol) 및 LSP 통신에 사용됩니다.
    /// </summary>
    public class JsonRpcRequest
    {
        /// <summary> JSON-RPC 버전 (기본값 "2.0") </summary>
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
        /// <summary> 요청 식별자 </summary>
        [JsonPropertyName("id")]
        public object? Id { get; set; }
        /// <summary> 호출할 메서드 이름 </summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;
        /// <summary> 메서드 파라미터 </summary>
        [JsonPropertyName("params")]
        public object? Params { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 응답 모델입니다.
    /// </summary>
    public class JsonRpcResponse
    {
        /// <summary> JSON-RPC 버전 </summary>
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
        /// <summary> 요청 식별자 (요청 시와 동일) </summary>
        [JsonPropertyName("id")]
        public object? Id { get; set; }
        /// <summary> 성공 시 결과 데이터 </summary>
        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }
        /// <summary> 오류 발생 시 오류 정보 </summary>
        [JsonPropertyName("error")]
        public JsonElement? Error { get; set; }
    }

    /// <summary>
    /// JSON-RPC 오류 세부 정보를 담는 모델입니다.
    /// </summary>
    public class JsonRpcError
    {
        /// <summary> 오류 코드 </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }
        /// <summary> 오류 메시지 </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        /// <summary> 추가 오류 데이터 </summary>
        [JsonPropertyName("data")]
        public object? Data { get; set; }
    }

    /// <summary>
    /// MCP(Model Context Protocol) 서버에서 제공하는 도구 정보 모델입니다.
    /// </summary>
    public class McpTool
    {
        /// <summary> 도구 이름 </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        /// <summary> 도구 설명 </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        /// <summary> 입력 파라미터 스키마 </summary>
        [JsonPropertyName("inputSchema")]
        public JsonElement InputSchema { get; set; }
    }

    /// <summary>
    /// MCP 도구 호출 결과를 담는 모델입니다.
    /// </summary>
    public class McpCallToolResult
    {
        /// <summary> 반환된 콘텐츠 목록 </summary>
        [JsonPropertyName("content")]
        public List<McpContent> Content { get; set; } = new();
        /// <summary> 실행 중 오류 발생 여부 </summary>
        [JsonPropertyName("isError")]
        public bool IsError { get; set; }
    }

    /// <summary>
    /// MCP 응답의 개별 콘텐츠 항목입니다.
    /// </summary>
    public class McpContent
    {
        /// <summary> 콘텐츠 유형 (예: "text") </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";
        /// <summary> 텍스트 내용 </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
