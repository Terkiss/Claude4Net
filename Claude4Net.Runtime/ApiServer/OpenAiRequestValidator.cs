using System.Text.Json;
using Claude4Net.Runtime.ApiServer.Models;

namespace Claude4Net.Runtime.ApiServer;

internal sealed record OpenAiRequestValidationError(string Parameter, string Message);

internal static class OpenAiRequestValidator
{
    private const int MaxEmbeddingInputItems = 256;
    private const int MaxEmbeddingInputUtf16CodeUnits = 32_768;

    private static readonly HashSet<string> ValidRoles = new(StringComparer.Ordinal)
    {
        "system",
        "user",
        "assistant",
        "tool"
    };

    public static OpenAiRequestValidationError? Validate(ChatCompletionRequest? request)
    {
        if (request is null) return Invalid("request", "The request payload cannot be null.");
        if (string.IsNullOrWhiteSpace(request.Model)) return Invalid("model", "The model field is required and cannot be blank.");
        if (request.Messages is null || request.Messages.Count == 0) return Invalid("messages", "Messages must be a non-empty array.");

        foreach (ChatMessageDto? message in request.Messages)
        {
            OpenAiRequestValidationError? error = ValidateMessage(message);
            if (error is not null) return error;
        }

        OpenAiRequestValidationError? toolsError = ValidateTools(request.Tools);
        return toolsError ?? ValidateStop(request.Stop, request.StopSpecified);
    }

    public static OpenAiRequestValidationError? Validate(TextCompletionRequest? request)
    {
        if (request is null) return Invalid("request", "The request payload cannot be null.");
        if (string.IsNullOrWhiteSpace(request.Model)) return Invalid("model", "The model field is required and cannot be blank.");
        if (!IsStringOrNonEmptyStringArray(request.Prompt))
            return Invalid("prompt", "Prompt must be a string or a non-empty array of strings.");
        return ValidateStop(request.Stop, request.StopSpecified);
    }

    public static OpenAiRequestValidationError? Validate(EmbeddingRequest? request)
    {
        if (request is null) return Invalid("request", "The request payload cannot be null.");
        if (string.IsNullOrWhiteSpace(request.Model)) return Invalid("model", "The model field is required and cannot be blank.");
        if (!IsValidEmbeddingInput(request.Input))
            return Invalid("input", "Input must be a nonblank string or an array of 1 to 256 nonblank strings, each no longer than 32768 UTF-16 code units.");
        if (request.Dimensions is <= 0) return Invalid("dimensions", "Dimensions must be a positive integer.");
        return null;
    }

    private static OpenAiRequestValidationError? ValidateMessage(ChatMessageDto? message)
    {
        if (message is null) return Invalid("messages", "Messages cannot contain null elements.");
        if (message.Role is null || !ValidRoles.Contains(message.Role))
            return Invalid("messages.role", "Message role must be system, user, assistant, or tool.");

        OpenAiRequestValidationError? toolCallsError = ValidateMessageToolCalls(message.ToolCalls);
        if (toolCallsError is not null) return toolCallsError;

        if (message.Role == "assistant" && message.Content is null)
        {
            return message.ToolCalls is { Count: > 0 }
                ? null
                : Invalid("messages.content", "Assistant content can be null only when valid tool calls are present.");
        }

        if (message.Content is null || !IsValidContent(message.Content, message.Role))
            return Invalid("messages.content", $"Content is malformed for the {message.Role} role.");
        return null;
    }

    private static OpenAiRequestValidationError? ValidateMessageToolCalls(List<ToolCallDto>? toolCalls)
    {
        if (toolCalls is null) return null;
        if (toolCalls.Count == 0) return Invalid("messages.tool_calls", "Tool calls must be non-empty when present.");

        foreach (ToolCallDto? toolCall in toolCalls)
        {
            if (toolCall?.Function is null)
                return Invalid("messages.tool_calls.function", "Tool calls require a function.");
            if (string.IsNullOrWhiteSpace(toolCall.Function.Name))
                return Invalid("messages.tool_calls.function.name", "Tool call function names cannot be blank.");
            if (toolCall.Function.Arguments is null)
                return Invalid("messages.tool_calls.function.arguments", "Tool call arguments must be strings.");
        }

        return null;
    }

    private static OpenAiRequestValidationError? ValidateTools(List<ToolDto>? tools)
    {
        if (tools is null) return null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ToolDto? tool in tools)
        {
            if (tool?.Function is null) return Invalid("tools.function", "Tools require a function entry.");
            if (string.IsNullOrWhiteSpace(tool.Function.Name))
                return Invalid("tools.function.name", "Tool function names cannot be blank.");
            if (!names.Add(tool.Function.Name))
                return Invalid("tools.function.name", "Tool function names must be unique.");
            if (tool.Function.ParametersSpecified && !IsJsonObject(tool.Function.Parameters))
                return Invalid("tools.function.parameters", "Tool parameters must be an object when present.");
        }

        return null;
    }

    private static OpenAiRequestValidationError? ValidateStop(object? stop, bool stopSpecified)
    {
        if (!stopSpecified) return null;
        if (stop is null) return Invalid("stop", "Stop cannot be null.");

        JsonElement element = ToElement(stop);
        if (element.ValueKind == JsonValueKind.String)
        {
            return string.IsNullOrEmpty(element.GetString())
                ? Invalid("stop", "Stop strings cannot be empty.")
                : null;
        }

        if (element.ValueKind != JsonValueKind.Array)
            return Invalid("stop", "Stop must be a non-empty string or an array of one to four non-empty strings.");

        int count = element.GetArrayLength();
        if (count is < 1 or > 4)
            return Invalid("stop", "Stop arrays must contain between one and four strings.");
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString()))
                return Invalid("stop", "Stop arrays can contain only non-empty strings.");
        }

        return null;
    }

    private static bool IsStringOrNonEmptyStringArray(object? value)
    {
        if (value is null) return false;
        JsonElement element = ToElement(value);
        if (element.ValueKind == JsonValueKind.String) return true;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0) return false;
        return element.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String);
    }

    private static bool IsValidEmbeddingInput(object? value)
    {
        if (value is null) return false;

        JsonElement element = ToElement(value);
        if (element.ValueKind == JsonValueKind.String)
            return IsValidEmbeddingInputItem(element.GetString());
        if (element.ValueKind != JsonValueKind.Array ||
            element.GetArrayLength() is < 1 or > MaxEmbeddingInputItems)
        {
            return false;
        }

        return element.EnumerateArray().All(item =>
            item.ValueKind == JsonValueKind.String && IsValidEmbeddingInputItem(item.GetString()));
    }

    private static bool IsValidEmbeddingInputItem(string? input) =>
        !string.IsNullOrWhiteSpace(input) && input.Length <= MaxEmbeddingInputUtf16CodeUnits;

    private static bool IsValidContent(object content, string role)
    {
        JsonElement element = ToElement(content);
        if (element.ValueKind == JsonValueKind.String) return true;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0) return false;

        foreach (JsonElement part in element.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String) continue;
            if (part.ValueKind != JsonValueKind.Object ||
                !part.TryGetProperty("type", out JsonElement typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(typeElement.GetString()))
            {
                return false;
            }

            string type = typeElement.GetString()!;
            if (type == "text")
            {
                if (!HasStringProperty(part, "text")) return false;
            }
            else if (type == "image_url")
            {
                if (role != "user" || !HasNestedStringProperty(part, "image_url", "url")) return false;
            }
            else if (type == "input_audio")
            {
                if (role != "user" ||
                    !HasNestedStringProperty(part, "input_audio", "data") ||
                    !HasNestedStringProperty(part, "input_audio", "format")) return false;
            }
        }

        return true;
    }

    private static bool HasStringProperty(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String;

    private static bool HasNestedStringProperty(JsonElement element, string property, string nestedProperty) =>
        element.TryGetProperty(property, out JsonElement nested) &&
        nested.ValueKind == JsonValueKind.Object &&
        HasStringProperty(nested, nestedProperty);

    private static bool IsJsonObject(object? value) =>
        value is not null && ToElement(value).ValueKind == JsonValueKind.Object;

    private static JsonElement ToElement(object value) =>
        value is JsonElement element ? element : JsonSerializer.SerializeToElement(value);

    private static OpenAiRequestValidationError Invalid(string parameter, string message) => new(parameter, message);
}
