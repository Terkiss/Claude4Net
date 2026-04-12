using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Discord;
using Discord.WebSocket;

namespace Claude4Net.Tools
{
    public class DiscordEngineInput
    {
        public ulong? channel_id { get; set; }
        public string message { get; set; } = string.Empty;
        public string? file_path { get; set; }
    }

    public class DiscordEngineTool : ITool
    {
        public string Name => "discord_send";
        public string Description => "Send a message to a specific Discord channel. If channel_id is not provided, it cannot be used.";
        
        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                channel_id = new { type = "integer", description = "The Discord channel ID to send the message to." },
                message = new { type = "string", description = "The message content to send." },
                file_path = new { type = "string", description = "(Optional) The full local file path of an image or file to attach." }
            },
            required = new[] { "message" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<DiscordEngineInput>(arguments, options)
                        ?? throw new ArgumentException("Invalid arguments");

            if (input.channel_id == null)
            {
                return new { status = "Error", message = "Channel ID is required for this tool." };
            }

            string? token = AuthManager.GetDiscordApiKey();
            if (string.IsNullOrEmpty(token)) throw new Exception("Discord API Key not found.");

            using var client = new DiscordSocketClient();
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();

            // Wait for connection
            int retry = 0;
            while (client.ConnectionState != ConnectionState.Connected && retry < 10)
            {
                await Task.Delay(500);
                retry++;
            }

            var channel = await client.GetChannelAsync(input.channel_id.Value) as IMessageChannel;
            if (channel == null)
            {
                await client.LogoutAsync();
                return new { status = "Error", message = $"Channel {input.channel_id} not found or inaccessible." };
            }

            if (!string.IsNullOrEmpty(input.file_path) && File.Exists(input.file_path))
            {
                await channel.SendFileAsync(input.file_path, input.message);
            }
            else
            {
                await channel.SendMessageAsync(input.message);
            }
            
            await client.LogoutAsync();

            return new
            {
                status = "Success",
                channel = input.channel_id,
                message = "Message sent successfully."
            };
        }
    }
}
