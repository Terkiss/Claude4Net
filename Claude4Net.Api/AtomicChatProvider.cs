using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace Claude4Net.Providers
{
    public class AtomicChatProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        private static readonly string _atomicChatBaseUrl = Environment.GetEnvironmentVariable("ATOMIC_CHAT_BASE_URL") ?? "http://127.0.0.1:1337";

        private string ApiUrl(string path) => $"{_atomicChatBaseUrl}/v1{path}";

        public async Task<bool> CheckAtomicChatRunningAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var resp = await client.GetAsync(ApiUrl("/models"));
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<string>> ListAtomicChatModelsAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var resp = await client.GetAsync(ApiUrl("/models"));
                resp.EnsureSuccessStatusCode();
                var data = await resp.Content.ReadFromJsonAsync<JsonElement>();
                var models = new List<string>();
                if (data.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in dataProp.EnumerateArray())
                    {
                        if (m.TryGetProperty("id", out var idProp))
                        {
                            models.Add(idProp.GetString() ?? "");
                        }
                    }
                }
                return models;
            }
            catch
            {
                // Console.WriteLine("Could not list Atomic Chat models");
                return new List<string>();
            }
        }

        public async Task<Dictionary<string, object>> AtomicChatAsync(
            string model,
            List<Dictionary<string, object>> messages,
            string? system = null,
            int maxTokens = 4096,
            double temperature = 1.0)
        {
            var chatMessages = new List<Dictionary<string, object>>(messages);
            if (!string.IsNullOrEmpty(system))
            {
                chatMessages.Insert(0, new Dictionary<string, object> { { "role", "system" }, { "content", system } });
            }

            var payload = new
            {
                model = model,
                messages = chatMessages,
                max_tokens = maxTokens,
                temperature = temperature,
                stream = false
            };

            var resp = await _httpClient.PostAsJsonAsync(ApiUrl("/chat/completions"), payload);
            resp.EnsureSuccessStatusCode();
            var data = await resp.Content.ReadFromJsonAsync<JsonElement>();

            var assistantText = "";
            if (data.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                {
                    assistantText = content.GetString() ?? "";
                }
            }

            var inputTokens = 0;
            var outputTokens = 0;
            if (data.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var promptTokens)) inputTokens = promptTokens.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var completionTokens)) outputTokens = completionTokens.GetInt32();
            }

            return new Dictionary<string, object>
            {
                { "id", data.TryGetProperty("id", out var idProp) ? idProp.GetString()! : "msg_atomic_chat" },
                { "type", "message" },
                { "role", "assistant" },
                { "content", new List<object> { new { type = "text", text = assistantText } } },
                { "model", model },
                { "stop_reason", "end_turn" },
                { "stop_sequence", null! },
                { "usage", new Dictionary<string, int> { { "input_tokens", inputTokens }, { "output_tokens", outputTokens } } }
            };
        }
    }
}
