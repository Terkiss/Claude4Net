using System;

namespace Claude4Net.SDK
{
    /// <summary>
    /// Discord에서 요청된 작업의 상태를 정의하는 열거형입니다.
    /// </summary>
    public enum DiscordJobStatus
    {
        /// <summary> 대기 중 </summary>
        Pending,
        /// <summary> 실행 중 </summary>
        Running,
        /// <summary> 사용자 승인 대기 중 </summary>
        WaitingApproval,
        /// <summary> 완료됨 </summary>
        Completed,
        /// <summary> 실패함 </summary>
        Failed,
        /// <summary> 승인 거절됨 </summary>
        Denied,
        /// <summary> 만료됨 </summary>
        Expired
    }

    /// <summary>
    /// Discord 연동 작업의 세부 상태를 관리하는 클래스입니다.
    /// </summary>
    public class DiscordJob : TaskStateBase
    {
        /// <summary> 요청이 발생한 Discord 서버(Guild) ID </summary>
        public ulong GuildId { get; set; }
        /// <summary> 요청이 발생한 채널 ID </summary>
        public ulong ChannelId { get; set; }
        /// <summary> 요청 메시지 ID </summary>
        public ulong MessageId { get; set; }
        /// <summary> Discord 작업 전용 상태 </summary>
        public DiscordJobStatus DiscordStatus { get; set; }
        /// <summary> 작업 생성 일시 </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary> 작업 시작 일시 </summary>
        public DateTime? StartedAt { get; set; }
        /// <summary> 작업 완료 일시 </summary>
        public DateTime? CompletedAt { get; set; }
        /// <summary> 최종 응답 메시지 </summary>
        public string? ResponseMessage { get; set; }
        /// <summary> 오류 발생 시의 메시지 </summary>
        public string? ErrorMessage { get; set; }
        /// <summary> 마지막으로 보고된 진행 상태 메시지 </summary>
        public string? LastProgressMessage { get; set; }
        
        // --- 승인 관련 정보 ---
        /// <summary> 승인이 필요한 도구 이름 </summary>
        public string? ApprovalRequiredTool { get; set; }
        /// <summary> 도구 실행에 사용된 인자(Arguments) </summary>
        public string? ApprovalArguments { get; set; }
        /// <summary> 작업을 승인한 Discord 사용자 ID </summary>
        public ulong? ApprovedByUserId { get; set; }
        /// <summary> 승인 일시 </summary>
        public DateTime? ApprovedAt { get; set; }
        /// <summary> 승인 요청용 메시지 ID </summary>
        public ulong? ApprovalMessageId { get; set; }
        
        public DiscordJob()
        {
            Type = "DiscordJob";
            Status = "Pending";
        }

        /// <summary> 작업 소요 시간 </summary>
        public TimeSpan? Duration => CompletedAt - StartedAt;
    }
}
