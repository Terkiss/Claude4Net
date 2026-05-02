using System;

namespace Claude4Net.SDK
{
    public enum DiscordJobStatus
    {
        Pending,
        Running,
        WaitingApproval,
        Completed,
        Failed,
        Denied,
        Expired
    }

    public class DiscordJob : TaskStateBase
    {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong MessageId { get; set; }
        public DiscordJobStatus DiscordStatus { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ResponseMessage { get; set; }
        public string? ErrorMessage { get; set; }
        public string? LastProgressMessage { get; set; }
        
        // Approval Info
        public string? ApprovalRequiredTool { get; set; }
        public string? ApprovalArguments { get; set; }
        public ulong? ApprovedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public ulong? ApprovalMessageId { get; set; }
        
        public DiscordJob()
        {
            Type = "DiscordJob";
            Status = "Pending";
        }

        public TimeSpan? Duration => CompletedAt - StartedAt;
    }
}
