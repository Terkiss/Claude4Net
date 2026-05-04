using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Claude4Net.SDK;

namespace Claude4Net.Discord
{
    /// <summary>
    /// 디스코드 상에서 사용자의 도구 사용 승인을 처리하는 핸들러입니다.
    /// 버튼(Approve/Deny) 인터랙션을 통해 비동기적으로 승인 여부를 결정합니다.
    /// </summary>
    public class DiscordApprovalHandler : IUserApprovalHandler
    {
        private readonly ISocketMessageChannel _channel;
        private readonly ulong _originalMessageId;
        private readonly string _jobId;
        private readonly DiscordSocketClient _client;

        public DiscordApprovalHandler(DiscordSocketClient client, ISocketMessageChannel channel, ulong messageId, string jobId)
        {
            _client = client;
            _channel = channel;
            _originalMessageId = messageId;
            _jobId = jobId;
        }

        /// <summary>
        /// 민감한 도구 실행 전 디스코드 채널에 승인 요청 버튼을 보내고 사용자의 클릭을 기다립니다.
        /// </summary>
        /// <param name="tool">도구 이름</param>
        /// <param name="args">도구 실행 인자(JSON)</param>
        /// <returns>승인 여부 (true: 승인, false: 거절 또는 만료)</returns>
        public async Task<bool> RequestApprovalAsync(string tool, string args)
        {
            // 1. [UI 구성] 승인/거절 버튼 및 상세 정보를 담은 Embed 메시지 생성
            var builder = new ComponentBuilder()
                .WithButton("Approve", $"approve-{_jobId}", ButtonStyle.Success)
                .WithButton("Deny", $"deny-{_jobId}", ButtonStyle.Danger);

            var embed = new EmbedBuilder()
                .WithTitle("🛡️ Security Approval Required")
                .WithDescription($"The agent is requesting to use a sensitive tool.")
                .AddField("Tool", $"`{tool}`", true)
                .AddField("Job ID", $"`{_jobId}`", true)
                .AddField("Arguments", $"```json\n{args}\n```")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp()
                .Build();

            var message = await DiscordRetryUtils.ExecuteWithRetryAsync(() => _channel.SendMessageAsync(embed: embed, components: builder.Build()));

            // 2. [상태 기록] AppState의 Job 정보를 '승인 대기 중'으로 업데이트
            if (AppState.Tasks.TryGetValue(_jobId, out var task) && task is DiscordJob job)
            {
                job.DiscordStatus = DiscordJobStatus.WaitingApproval;
                job.ApprovalRequiredTool = tool;
                job.ApprovalArguments = args;
                job.ApprovalMessageId = message.Id;
            }

            // 3. [비동기 제어] TaskCompletionSource를 사용하여 버튼 클릭 이벤트를 대기합니다.
            var tcs = new TaskCompletionSource<bool>();

            // 인터랙션 이벤트 핸들러 정의
            async Task OnInteractionCreated(SocketInteraction interaction)
            {
                if (interaction is SocketMessageComponent component)
                {
                    // 해당 작업(Job ID)과 관련된 버튼 클릭인지 확인
                    if (component.Data.CustomId.EndsWith($"-{_jobId}"))
                    {
                        // [보안] 화이트리스트에 등록된 승인자만 클릭 가능하도록 제어
                        bool isAllowed = AppState.DiscordAllowedApproverIds.Count > 0 && 
                                         AppState.DiscordAllowedApproverIds.Contains(interaction.User.Id);

                        if (!isAllowed)
                        {
                            await DiscordRetryUtils.ExecuteWithRetryAsync(() => component.RespondAsync("❌ You do not have permission to approve this action.", ephemeral: true));
                            return;
                        }

                        if (component.Data.CustomId.StartsWith("approve"))
                        {
                            // 승인됨
                            if (AppState.Tasks.TryGetValue(_jobId, out var t) && t is DiscordJob j)
                            {
                                j.ApprovedByUserId = interaction.User.Id;
                                j.ApprovedAt = DateTime.UtcNow;
                                j.DiscordStatus = DiscordJobStatus.Running;
                            }
                            tcs.TrySetResult(true); // Task 완료 처리
                        }
                        else if (component.Data.CustomId.StartsWith("deny"))
                        {
                            // 거절됨
                            if (AppState.Tasks.TryGetValue(_jobId, out var t2) && t2 is DiscordJob j2)
                            {
                                j2.DiscordStatus = DiscordJobStatus.Denied;
                                j2.CompletedAt = DateTime.UtcNow;
                            }
                            tcs.TrySetResult(false); // Task 완료 처리
                        }
                        
                        // 핸들러 해제
                        _client.InteractionCreated -= OnInteractionCreated;
                        // 인터랙션 응답 지연 (생각 중... 표시 방지)
                        await DiscordRetryUtils.ExecuteWithRetryAsync(() => component.DeferAsync());
                    }
                }
            }

            // 이벤트 핸들러 등록
            _client.InteractionCreated += OnInteractionCreated;

            // 4. [타임아웃 처리] 5분 동안 응답이 없으면 자동으로 만료(거절) 처리합니다.
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                // 타임아웃 발생 시 이벤트 핸들러 해제 및 UI 업데이트
                _client.InteractionCreated -= OnInteractionCreated;
                await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.ModifyAsync(msg => 
                {
                    msg.Content = "⏳ Approval request expired.";
                    msg.Embed = null;
                    msg.Components = new ComponentBuilder().Build();
                }));
                
                if (AppState.Tasks.TryGetValue(_jobId, out var expiredJob) && expiredJob is DiscordJob dJob)
                    dJob.DiscordStatus = DiscordJobStatus.Expired;
                    
                return false;
            }

            // 결과 획득
            bool result = await tcs.Task;

            // 5. [UI 정리] 버튼을 제거하고 최종 승인/거절 상태를 표시합니다.
            await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.ModifyAsync(msg => 
            {
                msg.Content = result ? "✅ Approved." : "❌ Denied.";
                msg.Embed = null;
                msg.Components = new ComponentBuilder().Build();
            }));

            return result;
        }
    }
}
