using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Claude4Net.SDK;

namespace Claude4Net.Discord
{
    /// <summary>
    /// ?”ìŠ¤ì½”ë“œ ?ì—???¬ìš©?ì˜ ?„êµ¬ ?¬ìš© ?¹ì¸??ì²˜ë¦¬?˜ëŠ” ?¸ë“¤?¬ì…?ˆë‹¤.
    /// ë²„íŠ¼(Approve/Deny) ?¸í„°?™ì…˜???µí•´ ë¹„ë™ê¸°ì ?¼ë¡œ ?¹ì¸ ?¬ë?ë¥?ê²°ì •?©ë‹ˆ??
    /// </summary>
    public class DiscordApprovalHandler : IRichApprovalHandler
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
        /// ë¯¼ê°???„êµ¬ ?¤í–‰ ???”ìŠ¤ì½”ë“œ ì±„ë„???¹ì¸ ?”ì²­ ë²„íŠ¼??ë³´ë‚´ê³??¬ìš©?ì˜ ?´ë¦­??ê¸°ë‹¤ë¦½ë‹ˆ??
        /// </summary>
        public async Task<bool> RequestApprovalAsync(string tool, string args)
        {
            var builder = new ComponentBuilder()
                .WithButton("Approve", $"approve-{_jobId}", ButtonStyle.Success)
                .WithButton("Deny", $"deny-{_jobId}", ButtonStyle.Danger);

            var embed = new EmbedBuilder()
                .WithTitle("?›¡ï¸?Security Approval Required")
                .WithDescription($"The agent is requesting to use a sensitive tool.")
                .AddField("Tool", $"`{tool}`", true)
                .AddField("Job ID", $"`{_jobId}`", true)
                .AddField("Arguments", $"```json\n{Truncate(args, 1000)}\n```")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp()
                .Build();

            return await SendAndAwaitApprovalAsync(embed, builder, tool, args);
        }

        /// <summary>
        /// ?Œì¼ ë³€ê²??¬í•­(Diff)???¬í•¨?˜ì—¬ ?¬ìš©?ì—ê²??¹ì¸???”ì²­?©ë‹ˆ??
        /// </summary>
        public async Task<bool> RequestApprovalWithDiffAsync(string tool, string args, FileDiffPreview diff)
        {
            var builder = new ComponentBuilder()
                .WithButton("Approve", $"approve-{_jobId}", ButtonStyle.Success)
                .WithButton("Deny", $"deny-{_jobId}", ButtonStyle.Danger);

            // Discord Embed ?„ë“œ ?œí•œ(1024?? ë°??„ì²´ ?œí•œ??ê³ ë ¤?˜ì—¬ Diff ?´ìš© ?˜ë¼?´ê¸° ê°€???ìš©
            string diffText = diff.DiffContent ?? "(no content)";
            if (diffText.Length > 1000)
            {
                diffText = diffText.Substring(0, 970) + "\n... (Diff truncated for size)";
            }

            var embed = new EmbedBuilder()
                .WithTitle("?“ File Change Approval Required")
                .WithDescription($"The agent wants to modify a file.")
                .AddField("Tool", $"`{tool}`", true)
                .AddField("File", $"`{diff.FilePath}`", true)
                .AddField("Type", $"`{diff.ChangeType}`", true)
                .AddField("Diff Preview", $"```diff\n{diffText}\n```")
                .WithColor(Color.Orange)
                .WithCurrentTimestamp()
                .Build();

            return await SendAndAwaitApprovalAsync(embed, builder, tool, args);
        }

        private async Task<bool> SendAndAwaitApprovalAsync(Embed embed, ComponentBuilder components, string tool, string args)
        {
            var message = await DiscordRetryUtils.ExecuteWithRetryAsync(() => _channel.SendMessageAsync(embed: embed, components: components.Build()));

            // 2. [?íƒœ ê¸°ë¡] AppState??Job ?•ë³´ë¥?'?¹ì¸ ?€ê¸?ì¤??¼ë¡œ ?…ë°?´íŠ¸
            if (AppState.Tasks.TryGetValue(_jobId, out var task) && task is DiscordJob job)
            {
                job.DiscordStatus = DiscordJobStatus.WaitingApproval;
                job.ApprovalRequiredTool = tool;
                job.ApprovalArguments = args;
                job.ApprovalMessageId = message.Id;
            }

            // 3. [ë¹„ë™ê¸??œì–´] TaskCompletionSourceë¥??¬ìš©?˜ì—¬ ë²„íŠ¼ ?´ë¦­ ?´ë²¤?¸ë? ?€ê¸°í•©?ˆë‹¤.
            var tcs = new TaskCompletionSource<bool>();

            // ?¸í„°?™ì…˜ ?´ë²¤???¸ë“¤???•ì˜
            async Task OnInteractionCreated(SocketInteraction interaction)
            {
                if (interaction is SocketMessageComponent component)
                {
                    // ?´ë‹¹ ?‘ì—…(Job ID)ê³?ê´€?¨ëœ ë²„íŠ¼ ?´ë¦­?¸ì? ?•ì¸
                    if (component.Data.CustomId.EndsWith($"-{_jobId}"))
                    {
                        // [ë³´ì•ˆ] ?”ì´?¸ë¦¬?¤íŠ¸???±ë¡???¹ì¸?ë§Œ ?´ë¦­ ê°€?¥í•˜?„ë¡ ?œì–´
                        bool isAllowed = AppState.DiscordAllowedApproverIds.Count > 0 &&
                                         AppState.DiscordAllowedApproverIds.Contains(interaction.User.Id);

                        if (!isAllowed)
                        {
                            await DiscordRetryUtils.ExecuteWithRetryAsync(() => component.RespondAsync("??You do not have permission to approve this action.", ephemeral: true));
                            return;
                        }

                        if (component.Data.CustomId.StartsWith("approve"))
                        {
                            // ?¹ì¸??
                            if (AppState.Tasks.TryGetValue(_jobId, out var t) && t is DiscordJob j)
                            {
                                j.ApprovedByUserId = interaction.User.Id;
                                j.ApprovedAt = DateTime.UtcNow;
                                j.DiscordStatus = DiscordJobStatus.Running;
                            }
                            tcs.TrySetResult(true); // Task ?„ë£Œ ì²˜ë¦¬
                        }
                        else if (component.Data.CustomId.StartsWith("deny"))
                        {
                            // ê±°ì ˆ??
                            if (AppState.Tasks.TryGetValue(_jobId, out var t2) && t2 is DiscordJob j2)
                            {
                                j2.DiscordStatus = DiscordJobStatus.Denied;
                                j2.CompletedAt = DateTime.UtcNow;
                            }
                            tcs.TrySetResult(false); // Task ?„ë£Œ ì²˜ë¦¬
                        }

                        // ?¸ë“¤???´ì œ
                        _client.InteractionCreated -= OnInteractionCreated;
                        // ?¸í„°?™ì…˜ ?‘ë‹µ ì§€??(?ê° ì¤?.. ?œì‹œ ë°©ì?)
                        await DiscordRetryUtils.ExecuteWithRetryAsync(() => component.DeferAsync());
                    }
                }
            }

            // ?´ë²¤???¸ë“¤???±ë¡
            _client.InteractionCreated += OnInteractionCreated;

            // 4. [?€?„ì•„??ì²˜ë¦¬] 5ë¶??™ì•ˆ ?‘ë‹µ???†ìœ¼ë©??ë™?¼ë¡œ ë§Œë£Œ(ê±°ì ˆ) ì²˜ë¦¬?©ë‹ˆ??
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                // ?€?„ì•„??ë°œìƒ ???´ë²¤???¸ë“¤???´ì œ ë°?UI ?…ë°?´íŠ¸
                _client.InteractionCreated -= OnInteractionCreated;
                await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.ModifyAsync(msg =>
                {
                    msg.Content = "??Approval request expired.";
                    msg.Embed = null;
                    msg.Components = new ComponentBuilder().Build();
                }));

                if (AppState.Tasks.TryGetValue(_jobId, out var expiredJob) && expiredJob is DiscordJob dJob)
                    dJob.DiscordStatus = DiscordJobStatus.Expired;

                return false;
            }

            // ê²°ê³¼ ?ë“
            bool result = await tcs.Task;

            // 5. [UI ?•ë¦¬] ë²„íŠ¼???œê±°?˜ê³  ìµœì¢… ?¹ì¸/ê±°ì ˆ ?íƒœë¥??œì‹œ?©ë‹ˆ??
            await DiscordRetryUtils.ExecuteWithRetryAsync(() => message.ModifyAsync(msg =>
            {
                msg.Content = result ? "??Approved." : "??Denied.";
                msg.Embed = null;
                msg.Components = new ComponentBuilder().Build();
            }));

            return result;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }
    }
}
