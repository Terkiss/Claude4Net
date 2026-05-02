using Discord;
using Discord.WebSocket;
using Claude4Net.SDK;
using Spectre.Console;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;

namespace Claude4Net.Discord
{
    public class DiscordOutputHandler : IOutputHandler
    {
        private readonly ISocketMessageChannel _channel;
        private readonly string _jobId;

        public DiscordOutputHandler(ISocketMessageChannel channel, string jobId)
        {
            _channel = channel;
            _jobId = jobId;
        }

        public async Task WriteAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // Update job status if tracked
            if (AppState.Tasks.TryGetValue(_jobId, out var task) && task is DiscordJob job)
            {
                if (job.DiscordStatus == DiscordJobStatus.Pending)
                {
                    job.StartedAt = DateTime.UtcNow;
                    job.Status = "Running";
                    job.DiscordStatus = DiscordJobStatus.Running;
                }
                job.LastProgressMessage = text;
            }

            // Segmenting: Discord has a 2000 character limit per message
            const int limit = 1950;
            if (text.Length <= limit)
            {
                await DiscordRetryUtils.ExecuteWithRetryAsync(() => _channel.SendMessageAsync(text));
            }
            else
            {
                int offset = 0;
                while (offset < text.Length)
                {
                    int length = Math.Min(limit, text.Length - offset);
                    string segment = text.Substring(offset, length);
                    await DiscordRetryUtils.ExecuteWithRetryAsync(() => _channel.SendMessageAsync(segment));
                    offset += length;
                    if (offset < text.Length) await Task.Delay(500); 
                }
            }
        }

        public async Task CompleteAsync(string finalMessage)
        {
            if (AppState.Tasks.TryGetValue(_jobId, out var finalTask) && finalTask is DiscordJob finalJob)
            {
                // P1 Fix: Don't overwrite Denied or Expired status
                if (finalJob.DiscordStatus == DiscordJobStatus.Denied || finalJob.DiscordStatus == DiscordJobStatus.Expired)
                {
                    return;
                }

                finalJob.Status = "Completed";
                finalJob.DiscordStatus = DiscordJobStatus.Completed;
                finalJob.CompletedAt = DateTime.UtcNow;
                finalJob.ResponseMessage = finalMessage;
                
                // Final Summary
                await DiscordRetryUtils.ExecuteWithRetryAsync(() => _channel.SendMessageAsync(DiscordResponseFormatter.FormatSuccess("Task finished successfully.", finalJob.Duration)));
            }
        }

        public async Task NotifyFailureAsync(string error)
        {
            if (AppState.Tasks.TryGetValue(_jobId, out var task) && task is DiscordJob job)
            {
                job.Status = "Failed";
                job.DiscordStatus = DiscordJobStatus.Failed;
                job.CompletedAt = DateTime.UtcNow;
                job.ErrorMessage = error;
            }
            await DiscordRetryUtils.ExecuteWithRetryAsync(() => _channel.SendMessageAsync(DiscordResponseFormatter.FormatError(error)));
        }

        public async Task SendFileAsync(string filePath, string? text = null)
        {
            if (File.Exists(filePath))
            {
                await DiscordRetryUtils.ExecuteWithRetryAsync(() => _channel.SendFileAsync(filePath, text));
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
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
                AlwaysDownloadUsers = false
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
            
            // Graceful Fallback for missing/test tokens
            if (string.IsNullOrEmpty(token) || 
                token.Trim().Equals("test", StringComparison.OrdinalIgnoreCase) ||
                token.Trim().StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            {
                LogToFile("[Discord] Discord API Token is missing or invalid. Listener will stay idle.");
                AnsiConsole.MarkupLine("[yellow]⚠ Discord token missing. Discord integration disabled.[/]");
                return;
            }

            try
            {
                await _client.LoginAsync(TokenType.Bot, token);
                await _client.StartAsync();
                LogToFile("[Discord] Discord Listener Service started.");
            }
            catch (Exception ex) 
            { 
                LogToFile($"[Discord] Critical failure during login: {ex.Message}");
                AnsiConsole.MarkupLine($"[bold red]Discord Error:[/] {Markup.Escape(ex.Message)}");
            }
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot) return;

            bool isMentioned = message.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id);
            bool isPrivate = message.Channel is IPrivateChannel;

            if (isMentioned || isPrivate)
            {
                string cleanText = message.Content;
                if (isMentioned)
                {
                    foreach (var user in message.MentionedUsers)
                    {
                        if (user.Id == _client.CurrentUser.Id)
                            cleanText = cleanText.Replace($"<@{user.Id}>", "").Replace($"<@!{user.Id}>", "").Trim();
                    }
                }

                if (!string.IsNullOrWhiteSpace(cleanText))
                {
                    // P2 Fix: Job Status Query Command
                    if (cleanText.StartsWith("!job ", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetId = cleanText.Substring(5).Trim();

                        // Permission Check for !job query
                        bool isAllowed = AppState.DiscordAllowedApproverIds.Count > 0 && 
                                         AppState.DiscordAllowedApproverIds.Contains(message.Author.Id);

                        if (!isAllowed)
                        {
                            await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.Channel.SendMessageAsync("❌ You do not have permission to query job status."));
                            return;
                        }

                        if (AppState.Tasks.TryGetValue(targetId, out var t) && t is DiscordJob jobInfo)
                        {
                            var embed = new EmbedBuilder()
                                .WithTitle($"Job Status: {targetId}")
                                .AddField("Status", $"`{jobInfo.DiscordStatus}`", true)
                                .AddField("Internal Status", $"`{jobInfo.Status}`", true)
                                .AddField("Created", $"{jobInfo.CreatedAt:HH:mm:ss}", true)
                                .WithColor(jobInfo.DiscordStatus switch {
                                    DiscordJobStatus.Completed => global::Discord.Color.Green,
                                    DiscordJobStatus.Failed or DiscordJobStatus.Denied or DiscordJobStatus.Expired => global::Discord.Color.Red,
                                    DiscordJobStatus.WaitingApproval => global::Discord.Color.Gold,
                                    _ => global::Discord.Color.Blue
                                });

                            if (jobInfo.StartedAt.HasValue)
                                embed.AddField("Started", $"{jobInfo.StartedAt:HH:mm:ss}", true);
                            
                            if (jobInfo.CompletedAt.HasValue)
                                embed.AddField("Finished", $"{jobInfo.CompletedAt:HH:mm:ss}", true);

                            if (!string.IsNullOrEmpty(jobInfo.ApprovalRequiredTool))
                                embed.AddField("Approval Required For", $"`{jobInfo.ApprovalRequiredTool}`");

                            if (jobInfo.ApprovedByUserId.HasValue)
                                embed.AddField("Approved By", $"<@{jobInfo.ApprovedByUserId}> at {jobInfo.ApprovedAt:HH:mm:ss}");

                            if (!string.IsNullOrEmpty(jobInfo.ErrorMessage))
                                embed.AddField("Error", $"```\n{jobInfo.ErrorMessage}\n```");

                            await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.Channel.SendMessageAsync(embed: embed.Build()));
                        }
                        else
                        {
                            await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.Channel.SendMessageAsync($"❌ Job `{targetId}` not found."));
                        }
                        return;
                    }

                    LogToFile($"[Discord] Input received from {message.Author.Username}: {cleanText}");
                    
                    // Create Async Job Model
                    var guildId = (message.Channel as IGuildChannel)?.GuildId ?? 0;
                    var jobId = $"discord-{guildId}-{message.Channel.Id}-{message.Id}";
                    
                    var job = new DiscordJob
                    {
                        Id = jobId,
                        GuildId = guildId,
                        ChannelId = message.Channel.Id,
                        MessageId = message.Id,
                        Status = "Pending",
                        DiscordStatus = DiscordJobStatus.Pending
                    };
                    
                    AppState.Tasks[jobId] = job;

                    // Support long-running tasks: Add a reaction to show we've received it
                    try { await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.AddReactionAsync(new global::Discord.Emoji("👀"))); } catch { }

                    // Notify Start
                    await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.Channel.SendMessageAsync(DiscordResponseFormatter.FormatStart(message.Author.Username, cleanText)));

                    string enrichedText = $"[System Context: Discord Message from @{message.Author.Username} in Channel ID: {message.Channel.Id}]\n{cleanText}";
                    var approvalHandler = new DiscordApprovalHandler(_client, message.Channel, message.Id, jobId);
                    var context = new InputContext(enrichedText, new DiscordOutputHandler(message.Channel, jobId), approvalHandler);
                    _broker.TryWrite(context);
                }
            }
            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_client.LoginState == LoginState.LoggedIn)
            {
                await _client.StopAsync();
                await _client.LogoutAsync();
            }
            LogToFile("[Discord] Discord Listener Service stopped.");
        }
    }
}
