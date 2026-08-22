using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude4Net.Runtime.ApiServer.Models
{
    public class ChatCompletionRequest
    {
        private object? _stop;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessageDto> Messages { get; set; } = new();

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;

        [JsonPropertyName("stream_options")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public StreamOptionsDto? StreamOptions { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("max_completion_tokens")]
        public int? MaxCompletionTokens { get; set; }

        public int? EffectiveMaxTokens => MaxCompletionTokens ?? MaxTokens;

        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }

        [JsonPropertyName("stop")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Stop
        {
            get => _stop;
            set
            {
                _stop = value;
                StopSpecified = true;
            }
        }

        [JsonIgnore]
        internal bool StopSpecified { get; private set; }

        [JsonPropertyName("presence_penalty")]
        public double? PresencePenalty { get; set; }

        [JsonPropertyName("frequency_penalty")]
        public double? FrequencyPenalty { get; set; }

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        [JsonPropertyName("response_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResponseFormatDto? ResponseFormat { get; set; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ToolDto>? Tools { get; set; }

        [JsonPropertyName("tool_choice")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? ToolChoice { get; set; }

        [JsonPropertyName("user")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? User { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class StreamOptionsDto
    {
        [JsonPropertyName("include_usage")]
        public bool IncludeUsage { get; set; } = false;

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class ResponseFormatDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text"; // "text", "json_object", "json_schema"

        [JsonPropertyName("json_schema")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? JsonSchema { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public abstract class MultimodalContentPart
    {
        [JsonPropertyName("type")]
        public abstract string Type { get; }
    }

    public class TextContentPart : MultimodalContentPart
    {
        public override string Type => "text";

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    public class ImageUrlContentPart : MultimodalContentPart
    {
        public override string Type => "image_url";

        [JsonPropertyName("image_url")]
        public ImageUrlDetail ImageUrl { get; set; } = new();
    }

    public class ImageUrlDetail
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("detail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; set; } // "auto", "low", "high"
    }

    public class InputAudioContentPart : MultimodalContentPart
    {
        public override string Type => "input_audio";

        [JsonPropertyName("input_audio")]
        public InputAudioDetail InputAudio { get; set; } = new();
    }

    public class InputAudioDetail
    {
        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;

        [JsonPropertyName("format")]
        public string Format { get; set; } = "wav";
    }

    public class UnknownContentPart : MultimodalContentPart
    {
        private readonly string _type;
        public override string Type => _type;

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

        public UnknownContentPart(string type)
        {
            _type = type;
        }
    }

    public class ChatMessageDto
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public object? Content { get; set; }

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ToolCallDto>? ToolCalls { get; set; }

        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; }

        [JsonPropertyName("reasoning_content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReasoningContent { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

        public List<MultimodalContentPart> GetContentParts()
        {
            var parts = new List<MultimodalContentPart>();
            if (Content == null) return parts;
            if (Content is string s)
            {
                parts.Add(new TextContentPart { Text = s });
                return parts;
            }

            JsonElement elem;
            if (Content is JsonElement je)
            {
                elem = je;
            }
            else
            {
                try
                {
                    elem = JsonSerializer.SerializeToElement(Content);
                }
                catch
                {
                    parts.Add(new TextContentPart { Text = Content.ToString() ?? string.Empty });
                    return parts;
                }
            }

            if (elem.ValueKind == JsonValueKind.String)
            {
                parts.Add(new TextContentPart { Text = elem.GetString() ?? string.Empty });
                return parts;
            }
            if (elem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in elem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(new TextContentPart { Text = item.GetString() ?? string.Empty });
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        string type = item.TryGetProperty("type", out var tp) ? (tp.GetString() ?? "text") : "text";
                        if (type == "text" && item.TryGetProperty("text", out var txt))
                        {
                            parts.Add(new TextContentPart { Text = txt.GetString() ?? string.Empty });
                        }
                        else if (type == "image_url" && item.TryGetProperty("image_url", out var imgObj))
                        {
                            string url = imgObj.TryGetProperty("url", out var u) ? (u.GetString() ?? "") : "";
                            string? detail = imgObj.TryGetProperty("detail", out var d) ? d.GetString() : null;
                            parts.Add(new ImageUrlContentPart { ImageUrl = new ImageUrlDetail { Url = url, Detail = detail } });
                        }
                        else if (type == "input_audio" && item.TryGetProperty("input_audio", out var audioObj))
                        {
                            string data = audioObj.TryGetProperty("data", out var ad) ? (ad.GetString() ?? "") : "";
                            string fmt = audioObj.TryGetProperty("format", out var af) ? (af.GetString() ?? "wav") : "wav";
                            parts.Add(new InputAudioContentPart { InputAudio = new InputAudioDetail { Data = data, Format = fmt } });
                        }
                        else
                        {
                            parts.Add(new UnknownContentPart(type));
                        }
                    }
                }
            }
            return parts;
        }

        public string GetContentString()
        {
            if (Content == null) return string.Empty;
            if (Content is string s) return s;

            var parts = GetContentParts();
            if (parts.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var part in parts)
                {
                    if (part is TextContentPart tcp)
                    {
                        sb.Append(tcp.Text);
                    }
                    else if (part is ImageUrlContentPart img)
                    {
                        sb.Append($" [Image: {img.ImageUrl.Url}] ");
                    }
                    else if (part is InputAudioContentPart)
                    {
                        sb.Append(" [Audio Attachment] ");
                    }
                }
                return sb.ToString();
            }

            return Content.ToString() ?? string.Empty;
        }
    }

    public class ChatCompletionResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("object")]
        public string Object { get; set; } = "chat.completion";

        [JsonPropertyName("created")]
        public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("system_fingerprint")]
        public string SystemFingerprint { get; set; } = "fp_claude4net";

        [JsonPropertyName("choices")]
        public List<ChatChoiceDto> Choices { get; set; } = new();

        [JsonPropertyName("usage")]
        public CompletionUsageDto Usage { get; set; } = new();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class ChatChoiceDto
    {
        [JsonPropertyName("index")]
        public int Index { get; set; } = 0;

        [JsonPropertyName("message")]
        public ChatMessageDto Message { get; set; } = new();

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; } = "stop";

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class ChatCompletionChunk
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("object")]
        public string Object { get; set; } = "chat.completion.chunk";

        [JsonPropertyName("created")]
        public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("system_fingerprint")]
        public string SystemFingerprint { get; set; } = "fp_claude4net";

        [JsonPropertyName("choices")]
        public List<ChatChunkChoiceDto> Choices { get; set; } = new();

        [JsonPropertyName("usage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CompletionUsageDto? Usage { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class ChatChunkChoiceDto
    {
        [JsonPropertyName("index")]
        public int Index { get; set; } = 0;

        [JsonPropertyName("delta")]
        public ChatChunkDeltaDto Delta { get; set; } = new();

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class ChatChunkDeltaDto
    {
        [JsonPropertyName("role")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Content { get; set; }

        /// <summary>
        /// OpenAI-Compatible 3rd-Party Extension Field (DeepSeek-R1 / Open WebUI / LiteLLM) for streaming thinking reasoning steps.
        /// Note: Standard official OpenAI API does not emit reasoning in delta chunks.
        /// </summary>
        [JsonPropertyName("reasoning_content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReasoningContent { get; set; }

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ToolCallDto>? ToolCalls { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class ToolDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public FunctionDto Function { get; set; } = new();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class FunctionDto
    {
        private object? _parameters;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }

        [JsonPropertyName("parameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Parameters
        {
            get => _parameters;
            set
            {
                _parameters = value;
                ParametersSpecified = true;
            }
        }

        [JsonIgnore]
        internal bool ParametersSpecified { get; private set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class ToolCallDto
    {
        [JsonPropertyName("index")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Index { get; set; }

        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public FunctionCallDto Function { get; set; } = new();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class FunctionCallDto
    {
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public string Arguments { get; set; } = string.Empty;

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class EmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "text-embedding-004";

        [JsonPropertyName("input")]
        public object Input { get; set; } = string.Empty;

        [JsonPropertyName("dimensions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Dimensions { get; set; }

        [JsonPropertyName("encoding_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EncodingFormat { get; set; } = "float";

        [JsonPropertyName("user")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? User { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

        public List<string> GetInputs()
        {
            var list = new List<string>();
            if (Input == null) return list;
            if (Input is string s)
            {
                list.Add(s);
            }
            else if (Input is JsonElement elem)
            {
                if (elem.ValueKind == JsonValueKind.String)
                {
                    list.Add(elem.GetString() ?? "");
                }
                else if (elem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in elem.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            list.Add(item.GetString() ?? "");
                        else
                            list.Add(item.ToString());
                    }
                }
            }
            else if (Input is IEnumerable<string> strEnum)
            {
                list.AddRange(strEnum);
            }
            else
            {
                list.Add(Input.ToString() ?? "");
            }
            return list;
        }
    }

    public class EmbeddingResponse
    {
        [JsonPropertyName("object")]
        public string Object { get; set; } = "list";

        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; set; } = new();

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("usage")]
        public EmbeddingUsage Usage { get; set; } = new();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class EmbeddingData
    {
        [JsonPropertyName("object")]
        public string Object { get; set; } = "embedding";

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public object Embedding { get; set; } = new List<float>();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class EmbeddingUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens => PromptTokens;
    }

    public class CompletionUsageDto
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens => PromptTokens + CompletionTokens;

        [JsonPropertyName("completion_tokens_details")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CompletionTokensDetailsDto? CompletionTokensDetails { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class CompletionTokensDetailsDto
    {
        [JsonPropertyName("reasoning_tokens")]
        public int ReasoningTokens { get; set; } = 0;

        [JsonPropertyName("accepted_prediction_tokens")]
        public int AcceptedPredictionTokens { get; set; } = 0;

        [JsonPropertyName("rejected_prediction_tokens")]
        public int RejectedPredictionTokens { get; set; } = 0;
    }

    public class ModelListResponse
    {
        [JsonPropertyName("object")]
        public string Object { get; set; } = "list";

        [JsonPropertyName("data")]
        public List<ModelCardDto> Data { get; set; } = new();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class ModelCardDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("object")]
        public string Object { get; set; } = "model";

        [JsonPropertyName("created")]
        public long Created { get; set; } = 1700000000;

        [JsonPropertyName("owned_by")]
        public string OwnedBy { get; set; } = "claude4net";

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class TextCompletionRequest
    {
        private object? _stop;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public object Prompt { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("stop")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Stop
        {
            get => _stop;
            set
            {
                _stop = value;
                StopSpecified = true;
            }
        }

        [JsonIgnore]
        internal bool StopSpecified { get; private set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

        public string GetPromptString()
        {
            if (Prompt == null) return string.Empty;
            if (Prompt is string s) return s;
            if (Prompt is JsonElement elem)
            {
                if (elem.ValueKind == JsonValueKind.String)
                    return elem.GetString() ?? string.Empty;
                if (elem.ValueKind == JsonValueKind.Array)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in elem.EnumerateArray())
                    {
                        sb.AppendLine(item.ToString());
                    }
                    return sb.ToString().TrimEnd();
                }
                return elem.ToString();
            }
            return Prompt.ToString() ?? string.Empty;
        }
    }

    public class TextCompletionResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("object")]
        public string Object { get; set; } = "text_completion";

        [JsonPropertyName("created")]
        public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("system_fingerprint")]
        public string SystemFingerprint { get; set; } = "fp_claude4net";

        [JsonPropertyName("choices")]
        public List<TextChoiceDto> Choices { get; set; } = new();

        [JsonPropertyName("usage")]
        public CompletionUsageDto Usage { get; set; } = new();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class TextChoiceDto
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("index")]
        public int Index { get; set; } = 0;

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; } = "stop";

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }
}
