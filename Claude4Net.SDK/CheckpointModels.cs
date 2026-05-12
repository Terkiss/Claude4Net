using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 체크포인트의 메타데이터를 담는 레코드입니다.
    /// </summary>
    public class CheckpointManifest
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string ToolCallId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> ChangedFiles { get; set; } = new();
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public string? ConversationSummary { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
