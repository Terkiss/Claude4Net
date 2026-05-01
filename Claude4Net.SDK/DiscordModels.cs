using System;

namespace Claude4Net.SDK
{
    public enum DiscordJobStatus
    {
        Pending,
        Running,
        Completed,
        Failed
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
        
        public DiscordJob()
        {
            Type = "DiscordJob";
            Status = "Pending";
        }

        public TimeSpan? Duration => CompletedAt - StartedAt;
    }
}
