using Discord;
using Discord.WebSocket;
using Claude4Net.SDK;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;

namespace Claude4Net.Discord
{
    public class DiscordOutputHandler : IOutputHandler
    {
        private readonly ISocketMessageChannel _channel;
        public DiscordOutputHandler(ISocketMessageChannel channel) => _channel = channel;
        public async Task WriteAsync(string text) => await _channel.SendMessageAsync(text);

        public async Task SendFileAsync(string filePath, string? text = null)
        {
            if (File.Exists(filePath))
            {
                await _channel.SendFileAsync(filePath, text);
            }
        }
    }

    public class DiscordListenerService
    {
        private readonly DiscordSocketClient _client;
        private readonly IInputBroker _broker;
        private static readonly object _logLock = new object();
        private readonly string _logFilePath;

        public DiscordListenerService(IInputBroker broker)
        {
            _broker = broker;
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            });

            _client.MessageReceived += OnMessageReceived;
            _client.Log += OnLog;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logDir = Path.Combine(baseDir, "Log", "data");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, "log.txt");
        }

        private void LogToFile(string message)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            lock (_logLock) { try { File.AppendAllText(_logFilePath, logEntry); } catch { } }
        }

        private Task OnLog(LogMessage msg) { LogToFile($"[Discord Log] {msg.ToString()}"); return Task.CompletedTask; }

        public async Task StartAsync()
        {
            string? token = AuthManager.GetDiscordApiKey();
            if (string.IsNullOrEmpty(token) || token.Trim().Equals("test", StringComparison.OrdinalIgnoreCase))
            {
                LogToFile($"[Discord] Discord API Key is '{(token ?? "null")}'. Skipping initialization.");
                return;
            }

            try
            {
                await _client.LoginAsync(TokenType.Bot, token);
                await _client.StartAsync();
                LogToFile("[Discord] Discord Listener Service started.");
            }
            catch (Exception ex) { LogToFile($"[Discord] Failed to start: {ex.Message}"); }
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot) return;

            if (message.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id))
            {
                string cleanText = message.Content;
                foreach (var user in message.MentionedUsers)
                {
                    if (user.Id == _client.CurrentUser.Id)
                        cleanText = cleanText.Replace($"<@{user.Id}>", "").Replace($"<@!{user.Id}>", "").Trim();
                }

                if (!string.IsNullOrWhiteSpace(cleanText))
                {
                    LogToFile($"[Discord] Input received from {message.Author.Username}: {cleanText}");
                    
                    // Respond back via Discord Channel
                    var context = new InputContext(cleanText, new DiscordOutputHandler(message.Channel));
                    _broker.TryWrite(context);
                }
            }
            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            await _client.StopAsync();
            await _client.LogoutAsync();
            LogToFile("[Discord] Discord Listener Service stopped.");
        }
    }
}
