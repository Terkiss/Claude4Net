using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 세션 핸드오프 정보를 담는 레코드입니다.
    /// </summary>
    public class SessionHandoffRecord
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "In Progress"; // Completed, Blocked, Needs Review
        public string Summary { get; set; } = string.Empty;
        public List<string> Accomplishments { get; set; } = new();
        public List<string> RemainingTasks { get; set; } = new();
        public List<string> EvidenceFiles { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
