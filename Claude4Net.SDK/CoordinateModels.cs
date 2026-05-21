using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// ?묒뾽 議곗젙(Coordination)???④퀎瑜??뺤쓽?섎뒗 ?닿굅?뺤엯?덈떎.
    /// </summary>
    public enum CoordinatePhase
    {
        /// <summary> 怨꾪쉷 ?④퀎 </summary>
        Planning,
        /// <summary> ?ㅽ뻾 ?④퀎 </summary>
        Execution,
        /// <summary> 寃利??④퀎 </summary>
        Verification,
        /// <summary> ?꾨즺??</summary>
        Completed,
        /// <summary> ?ㅽ뙣??</summary>
        Failed
    }

    /// <summary>
    /// ?먯씠?꾪듃????븷???뺤쓽?⑸땲??
    /// </summary>
    public enum AgentRole
    {
        /// <summary> ?꾩껜 紐⑺몴瑜??섎┰?섍퀬 ?섏쐞 ?묒뾽??愿由ы븯??以묒옱??</summary>
        Orchestrator,
        /// <summary> ?뺣낫 ?섏쭛 諛?議곗궗瑜??대떦 </summary>
        Researcher,
        /// <summary> ?ㅼ젣 肄붾뱶 援ы쁽???대떦 </summary>
        Coder,
        /// <summary> 肄붾뱶 諛?寃곌낵臾쇱쓽 ?덉쭏??寃??</summary>
        Reviewer,
        /// <summary> 湲고? ?쇰컲 ?묒뾽 ?섑뻾 </summary>
        Worker
    }

    /// <summary>
    /// ?먯씠?꾪듃???곸꽭 ?꾨줈???뺣낫瑜??대뒗 ?대옒?ㅼ엯?덈떎.
    /// </summary>
    public class AgentProfile
    {
        /// <summary> ?먯씠?꾪듃??怨좎쑀 紐낆묶 </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> 二쇰맂 ??븷 </summary>
        public AgentRole Role { get; set; } = AgentRole.Worker;
        /// <summary> ?꾨Ц 遺꾩빞 (?? "C#", "Security", "WebSearch") </summary>
        public List<string> Specializations { get; set; } = new();
        /// <summary> 沅뚰븳 紐⑤뱶 </summary>
        public PermissionMode MaxPermission { get; set; } = PermissionMode.Prompt;
        /// <summary> ?꾩옱 諛붿걶 ?곹깭 ?щ? </summary>
        public bool IsBusy { get; set; }
    }

    /// <summary>
    /// ?묒뾽 ?좊떦 ?뺣낫瑜??대뒗 ?대옒?ㅼ엯?덈떎.
    /// </summary>
    public class TaskAssignment
    {
        public string TaskId { get; set; } = string.Empty;
        public string AgentName { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public string? ResultSummary { get; set; }
    }

    /// <summary>
    /// 怨듭쑀 ?묒뾽 蹂대뱶(Shared Task Board) ?명꽣?섏씠?ㅼ엯?덈떎.
    /// </summary>
    public interface ITaskBoard
    {
        /// <summary> ?묒뾽??蹂대뱶??異붽??⑸땲?? </summary>
        void AddTask(CoordinateTask task);
        /// <summary> ?뱀젙 ?묒뾽??媛?몄샃?덈떎. </summary>
        CoordinateTask? GetTask(string taskId);
        /// <summary> ?뱀젙 議곌굔??留욌뒗 ?ъ슜 媛?ν븳 ?묒뾽??寃?됲빀?덈떎. </summary>
        IEnumerable<CoordinateTask> GetPendingTasks();
        /// <summary> ?묒뾽 ?곹깭瑜??낅뜲?댄듃?⑸땲?? </summary>
        void UpdateTask(CoordinateTask task);
        /// <summary> ?먯씠?꾪듃瑜??뱀젙 ?묒뾽???좊떦?⑸땲?? </summary>
        bool TryAssignTask(string taskId, string agentName);
        /// <summary> ?섏쐞 ?묒뾽???앹꽦?섍퀬 ?곸쐞 ?묒뾽???곌껐?⑸땲?? </summary>
        void DecomposeTask(string parentTaskId, List<CoordinateTask> subTasks);
    }

    /// <summary>
    /// 寃?좎옄??寃곗젙 ?곹깭瑜??뺤쓽?섎뒗 ?닿굅?뺤엯?덈떎.
    /// </summary>
    public enum ReviewerDecision
    {
        /// <summary> ?湲?以?</summary>
        Pending,
        /// <summary> ?뱀씤??</summary>
        Approved,
        /// <summary> 嫄곗젅??</summary>
        Rejected,
        /// <summary> ?섏젙 ?붿껌 </summary>
        RequestChanges
    }

    /// <summary>
    /// ?묒뾽 吏꾪뻾??洹쇨굅(Evidence)瑜?湲곕줉?섎뒗 ?대옒?ㅼ엯?덈떎.
    /// </summary>
    public class CoordinateEvidence
    {
        /// <summary> 洹쇨굅 怨좎쑀 ID </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString().Substring(0, 8);
        /// <summary> ?묒꽦??(?먯씠?꾪듃 ?대쫫 ?? </summary>
        public string Author { get; set; } = string.Empty;
        /// <summary> 湲곕줉 ?쒓컙 </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        /// <summary> 湲곕줉 ?뱀떆???④퀎 </summary>
        public CoordinatePhase Phase { get; set; }
        /// <summary> 愿?⑤맂 寃뚯씠???대쫫 </summary>
        public string GateName { get; set; } = string.Empty;
        /// <summary> ?붿빟 ?댁슜 </summary>
        public string Summary { get; set; } = string.Empty;
        /// <summary> ?곸꽭 ?댁슜 (?좏깮 ?ы빆) </summary>
        public string? Details { get; set; }
    }

    /// <summary>
    /// ?④퀎 ?꾪솚???꾪빐 ?듦낵?댁빞 ?섎뒗 寃利?吏??Gate)???뺤쓽?⑸땲??
    /// </summary>
    public class CoordinateGate
    {
        /// <summary> 寃뚯씠??紐낆묶 </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> ?듦낵 ?щ? </summary>
        public bool IsPassed { get; set; }
        /// <summary> ?듦낵瑜??꾪빐 洹쇨굅(Evidence) 湲곕줉???꾩닔?몄? ?щ? </summary>
        public bool IsEvidenceRequired { get; set; } = true;
        /// <summary> 異붽? ?섍껄 </summary>
        public string? Comments { get; set; }
        /// <summary> 留덉?留??낅뜲?댄듃 ?쒓컙 </summary>
        public DateTime? UpdatedAt { get; set; }
        /// <summary> ?곌껐??洹쇨굅 紐⑸줉 </summary>
        public List<CoordinateEvidence> Evidences { get; set; } = new();
        /// <summary> ?뱀씤??</summary>
        public string? ApprovedBy { get; set; }
    }

    /// <summary>
    /// ?щ윭 ?먯씠?꾪듃 媛꾩쓽 ?묒뾽 諛??④퀎蹂??뱀씤???꾩슂??議곗젙 ?묒뾽??愿由ы븯???대옒?ㅼ엯?덈떎.
    /// </summary>
    public class CoordinateTask : TaskStateBase
    {
        /// <summary> ?묒뾽 ?쒕ぉ </summary>
        public string Title { get; set; } = string.Empty;
        /// <summary> ?묒뾽 ?곸꽭 ?ㅻ챸 </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary> ?꾩옱 吏꾪뻾 ?④퀎 </summary>
        public CoordinatePhase CurrentPhase { get; set; } = CoordinatePhase.Planning;
        /// <summary> 援ъ꽦??寃利?寃뚯씠??紐⑸줉 </summary>
        public List<CoordinateGate> Gates { get; set; } = new();
        /// <summary> 寃???곹깭 </summary>
        public ReviewerDecision ReviewStatus { get; set; } = ReviewerDecision.Pending;
        /// <summary> ?좊떦???먯씠?꾪듃 紐낆묶 </summary>
        public string? AssignedAgent { get; set; }
        /// <summary> ?붽뎄?섎뒗 ?먯씠?꾪듃 ??븷 </summary>
        public AgentRole RequiredRole { get; set; } = AgentRole.Worker;
        /// <summary> ?섏〈?섍퀬 ?덈뒗 ?묒뾽 ID 紐⑸줉 </summary>
        public List<string> Dependencies { get; set; } = new();
        /// <summary> ?곸쐞 ?묒뾽 ID </summary>
        public string? ParentTaskId { get; set; } public string? SpecId { get; set; } public DateTime? SpecLockedAt { get; set; }
        /// <summary> ?섏쐞 ?묒뾽 ID 紐⑸줉 </summary>
        public List<string> SubTaskIds { get; set; } = new();
        /// <summary> ?앹꽦 ?쇱떆 </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        /// <summary> 留덉?留??섏젙 ?쇱떆 </summary>
        public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
        /// <summary> ?묒뾽 蹂寃??대젰 </summary>
        public List<string> History { get; set; } = new();

        /// <summary> 蹂묓빀 以鍮??먯닔 (0~100) </summary>
        public double ReadinessScore { get; set; }
        /// <summary> 吏꾪뻾??媛濡쒕쭑???붿냼(Blocker) 紐⑸줉 </summary>
        public List<string> Blockers { get; set; } = new();

        public CoordinateTask()
        {
            Type = "Coordinate";
            Status = "Planning";
        }
    }
}
