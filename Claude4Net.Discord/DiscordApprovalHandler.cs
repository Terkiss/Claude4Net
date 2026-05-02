using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Claude4Net.SDK;

namespace Claude4Net.Discord
{
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

        public async Task<bool> RequestApprovalAsync(string tool, string args)
        {
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

            // Update Job Status
            if (AppState.Tasks.TryGetValue(_jobId, out var task) && task is DiscordJob job)
            {
                job.DiscordStatus = DiscordJobStatus.WaitingApproval;
                job.ApprovalRequiredTool = tool;
                job.ApprovalArguments = args;
                job.ApprovalMessageId = message.Id;
            }

            var tcs = new TaskCompletionSource<bool>();

            // Listen for interactions
            async Task OnInteractionCreated(SocketInteraction interaction)
            {
                if (interaction is SocketMessageComponent component)
                {
                    if (component.Data.CustomId.EndsWith($"-{_jobId}"))
                    {
                        // P1 Fix: Default Deny if whitelist is empty
                        bool isAllowed = AppState.DiscordAllowedApproverIds.Count > 0 && 
                                         AppState.DiscordAllowedApproverIds.Contains(interaction.User.Id);

                        if (!isAllowed)
                        {
                            await DiscordRetryUtils.ExecuteWithRetryAsync(() => component.RespondAsync("❌ You do not have permission to approve this action.", ephemeral: true));
                            return;
                        }

                        if (component.Data.CustomId.StartsWith("approve"))
                        {
                            if (AppState.Tasks.TryGetValue(_jobId, out var t) && t is DiscordJob j)
                            {
                                j.ApprovedByUserId = interaction.User.Id;
                                j.ApprovedAt = DateTime.UtcNow;
                                j.DiscordStatus = DiscordJobStatus.Running;
                            }
                            tcs.TrySetResult(true);
                        }
                        else if (component.Data.CustomId.StartsWith("deny"))
                        {
                            if (AppState.Tasks.TryGetValue(_jobId, out var t2) && t2 is DiscordJob j2)
                            {
                                j2.DiscordStatus = DiscordJobStatus.Denied;
                                j2.CompletedAt = DateTime.UtcNow;
                            }
                            tcs.TrySetResult(false);
                        }
                        
                        _client.InteractionCreated -= OnInteractionCreated;
                        // P2 Fix: Await the interaction ack
                        await DiscordRetryUtils.ExecuteWithRetryAsync(() => component.DeferAsync());
                    }
                }
            }

            _client.InteractionCreated += OnInteractionCreated;

            // Timeout after 5 minutes
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
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

            bool result = await tcs.Task;

            // Cleanup message
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
