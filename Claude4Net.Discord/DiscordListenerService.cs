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
    /// <summary>
    /// 디스코드 환경에서의 출력(응답)을 처리하는 핸들러입니다.
    /// 2000자 글자수 제한 처리 및 작업 상태 업데이트 로직을 포함합니다.
    /// </summary>
    public class DiscordOutputHandler : IOutputHandler
    {
        private readonly ISocketMessageChannel _channel;
        private readonly string _jobId;

        public DiscordOutputHandler(ISocketMessageChannel channel, string jobId)
        {
            _channel = channel;
            _jobId = jobId;
        }

        /// <summary>
        /// 텍스트 메시지를 디스코드 채널로 전송합니다. 2000자 초과 시 분할 전송합니다.
        /// </summary>
        public async Task WriteAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // [상태 업데이트] 작업이 시작되었음을 AppState에 기록
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

            // [분할 전송] 디스코드의 메시지당 2000자 제한을 회피하기 위해 1950자 단위로 자릅니다.
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
                    if (offset < text.Length) await Task.Delay(500); // 속도 제한(Rate Limit) 방지를 위한 짧은 지연
                }
            }
        }

        /// <summary>
        /// 에이전트 작업 완료 시 최종 결과 요약을 전송하고 상태를 업데이트합니다.
        /// </summary>
        public async Task CompleteAsync(string finalMessage)
        {
            if (AppState.Tasks.TryGetValue(_jobId, out var finalTask) && finalTask is DiscordJob finalJob)
            {
                // 거부되거나 만료된 상태라면 완료로 덮어쓰지 않음
                if (finalJob.DiscordStatus == DiscordJobStatus.Denied || finalJob.DiscordStatus == DiscordJobStatus.Expired)
                {
                    return;
                }

                finalJob.Status = "Completed";
                finalJob.DiscordStatus = DiscordJobStatus.Completed;
                finalJob.CompletedAt = DateTime.UtcNow;
                finalJob.ResponseMessage = finalMessage;
                
                // 최종 요약 메시지 전송
                await DiscordRetryUtils.ExecuteWithRetryAsync(() => _channel.SendMessageAsync(DiscordResponseFormatter.FormatSuccess("Task finished successfully.", finalJob.Duration)));
            }
        }

        /// <summary>
        /// 작업 실패 시 에러 메시지를 전송하고 상태를 업데이트합니다.
        /// </summary>
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

        /// <summary>
        /// 이미지나 문서 등의 파일을 디스코드 채널로 전송합니다.
        /// </summary>
        public async Task SendFileAsync(string filePath, string? text = null)
        {
            if (File.Exists(filePath))
            {
                await DiscordRetryUtils.ExecuteWithRetryAsync(() => _channel.SendFileAsync(filePath, text));
            }
        }
    }

    /// <summary>
    /// 디스코드 봇의 게이트웨이 이벤트를 수신하고 메시지를 처리하는 서비스입니다.
    /// </summary>
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
                // 메시지 내용 수신을 위해 MessageContent 인텐트 활성화 필요
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

        /// <summary>
        /// 디스코드 봇을 로그인시키고 백그라운드 리스닝을 시작합니다.
        /// </summary>
        public async Task StartAsync()
        {
            string? token = AuthManager.GetDiscordApiKey();
            
            // 토큰 유효성 검사
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

        /// <summary>
        /// 새로운 메시지가 수신되었을 때 호출되는 이벤트 핸들러입니다.
        /// </summary>
        private async Task OnMessageReceived(SocketMessage message)
        {
            // 봇 자신의 메시지는 무시
            if (message.Author.IsBot) return;

            // 멘션되었거나 DM인 경우에만 처리
            bool isMentioned = message.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id);
            bool isPrivate = message.Channel is IPrivateChannel;

            if (isMentioned || isPrivate)
            {
                string cleanText = message.Content;
                if (isMentioned)
                {
                    // 멘션 태그 제거
                    foreach (var user in message.MentionedUsers)
                    {
                        if (user.Id == _client.CurrentUser.Id)
                            cleanText = cleanText.Replace($"<@{user.Id}>", "").Replace($"<@!{user.Id}>", "").Trim();
                    }
                }

                if (!string.IsNullOrWhiteSpace(cleanText))
                {
                    // [특수 명령] 작업 상태 조회 (!job <id>)
                    if (cleanText.StartsWith("!job ", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetId = cleanText.Substring(5).Trim();

                        // 권한 확인 (허용된 승인자만 조회 가능)
                        bool isAllowed = AppState.DiscordAllowedApproverIds.Count > 0 && 
                                         AppState.DiscordAllowedApproverIds.Contains(message.Author.Id);

                        if (!isAllowed)
                        {
                            await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.Channel.SendMessageAsync("❌ You do not have permission to query job status."));
                            return;
                        }

                        if (AppState.Tasks.TryGetValue(targetId, out var t) && t is DiscordJob jobInfo)
                        {
                            // Embed를 사용한 가독성 높은 상태 출력
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
                    
                    // [Job 생성] 추적을 위한 고유 Job ID 생성 및 초기화
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

                    // 수신 확인 리액션 추가
                    try { await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.AddReactionAsync(new global::Discord.Emoji("👀"))); } catch { }

                    // 작업 시작 알림 전송
                    await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.Channel.SendMessageAsync(DiscordResponseFormatter.FormatStart(message.Author.Username, cleanText)));

                    // [에이전트 연동] 시스템 컨텍스트를 추가하여 입력 브로커에 전달
                    string enrichedText = $"[System Context: Discord Message from @{message.Author.Username} in Channel ID: {message.Channel.Id}]\n{cleanText}";
                    var approvalHandler = new DiscordApprovalHandler(_client, message.Channel, message.Id, jobId);
                    var context = new InputContext(enrichedText, new DiscordOutputHandler(message.Channel, jobId), approvalHandler);
                    
                    // 브로커에 메시지를 써서 에이전트 루프가 이를 가져가도록 유도 (비동기 흐름 시작)
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
