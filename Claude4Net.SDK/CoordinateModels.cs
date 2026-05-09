using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// ?‘ì—… ì¡°ì •(Coordination)???¨ê³„ë¥??•ì˜?˜ëŠ” ?´ê±°?•ì…?ˆë‹¤.
    /// </summary>
    public enum CoordinatePhase
    {
        /// <summary> ê³„íš ?¨ê³„ </summary>
        Planning,
        /// <summary> ?¤í–‰ ?¨ê³„ </summary>
        Execution,
        /// <summary> ê²€ì¦??¨ê³„ </summary>
        Verification,
        /// <summary> ?„ë£Œ??</summary>
        Completed,
        /// <summary> ?¤íŒ¨??</summary>
        Failed
    }

    /// <summary>
    /// ?ì´?„íŠ¸????• ???•ì˜?©ë‹ˆ??
    /// </summary>
    public enum AgentRole
    {
        /// <summary> ?„ì²´ ëª©í‘œë¥??˜ë¦½?˜ê³  ?˜ìœ„ ?‘ì—…??ê´€ë¦¬í•˜??ì¤‘ì¬??</summary>
        Orchestrator,
        /// <summary> ?•ë³´ ?˜ì§‘ ë°?ì¡°ì‚¬ë¥??´ë‹¹ </summary>
        Researcher,
        /// <summary> ?¤ì œ ì½”ë“œ êµ¬í˜„???´ë‹¹ </summary>
        Coder,
        /// <summary> ì½”ë“œ ë°?ê²°ê³¼ë¬¼ì˜ ?ˆì§ˆ??ê²€??</summary>
        Reviewer,
        /// <summary> ê¸°í? ?¼ë°˜ ?‘ì—… ?˜í–‰ </summary>
        Worker
    }

    /// <summary>
    /// ?ì´?„íŠ¸???ì„¸ ?„ë¡œ???•ë³´ë¥??´ëŠ” ?´ë˜?¤ì…?ˆë‹¤.
    /// </summary>
    public class AgentProfile
    {
        /// <summary> ?ì´?„íŠ¸??ê³ ìœ  ëª…ì¹­ </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> ì£¼ëœ ??•  </summary>
        public AgentRole Role { get; set; } = AgentRole.Worker;
        /// <summary> ?„ë¬¸ ë¶„ì•¼ (?? "C#", "Security", "WebSearch") </summary>
        public List<string> Specializations { get; set; } = new();
        /// <summary> ê¶Œí•œ ëª¨ë“œ </summary>
        public PermissionMode MaxPermission { get; set; } = PermissionMode.Prompt;
        /// <summary> ?„ì¬ ë°”ìœ ?íƒœ ?¬ë? </summary>
        public bool IsBusy { get; set; }
    }

    /// <summary>
    /// ?‘ì—… ? ë‹¹ ?•ë³´ë¥??´ëŠ” ?´ë˜?¤ì…?ˆë‹¤.
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
    /// ê³µìœ  ?‘ì—… ë³´ë“œ(Shared Task Board) ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface ITaskBoard
    {
        /// <summary> ?‘ì—…??ë³´ë“œ??ì¶”ê??©ë‹ˆ?? </summary>
        void AddTask(CoordinateTask task);
        /// <summary> ?¹ì • ?‘ì—…??ê°€?¸ì˜µ?ˆë‹¤. </summary>
        CoordinateTask? GetTask(string taskId);
        /// <summary> ?¹ì • ì¡°ê±´??ë§ëŠ” ?¬ìš© ê°€?¥í•œ ?‘ì—…??ê²€?‰í•©?ˆë‹¤. </summary>
        IEnumerable<CoordinateTask> GetPendingTasks();
        /// <summary> ?‘ì—… ?íƒœë¥??…ë°?´íŠ¸?©ë‹ˆ?? </summary>
        void UpdateTask(CoordinateTask task);
        /// <summary> ?ì´?„íŠ¸ë¥??¹ì • ?‘ì—…??? ë‹¹?©ë‹ˆ?? </summary>
        bool TryAssignTask(string taskId, string agentName);
        /// <summary> ?˜ìœ„ ?‘ì—…???ì„±?˜ê³  ?ìœ„ ?‘ì—…???°ê²°?©ë‹ˆ?? </summary>
        void DecomposeTask(string parentTaskId, List<CoordinateTask> subTasks);
    }

    /// <summary>
    /// ê²€? ì??ê²°ì • ?íƒœë¥??•ì˜?˜ëŠ” ?´ê±°?•ì…?ˆë‹¤.
    /// </summary>
    public enum ReviewerDecision
    {
        /// <summary> ?€ê¸?ì¤?</summary>
        Pending,
        /// <summary> ?¹ì¸??</summary>
        Approved,
        /// <summary> ê±°ì ˆ??</summary>
        Rejected,
        /// <summary> ?˜ì • ?”ì²­ </summary>
        RequestChanges
    }

    /// <summary>
    /// ?‘ì—… ì§„í–‰??ê·¼ê±°(Evidence)ë¥?ê¸°ë¡?˜ëŠ” ?´ë˜?¤ì…?ˆë‹¤.
    /// </summary>
    public class CoordinateEvidence
    {
        /// <summary> ê·¼ê±° ê³ ìœ  ID </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString().Substring(0, 8);
        /// <summary> ?‘ì„±??(?ì´?„íŠ¸ ?´ë¦„ ?? </summary>
        public string Author { get; set; } = string.Empty;
        /// <summary> ê¸°ë¡ ?œê°„ </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        /// <summary> ê¸°ë¡ ?¹ì‹œ???¨ê³„ </summary>
        public CoordinatePhase Phase { get; set; }
        /// <summary> ê´€?¨ëœ ê²Œì´???´ë¦„ </summary>
        public string GateName { get; set; } = string.Empty;
        /// <summary> ?”ì•½ ?´ìš© </summary>
        public string Summary { get; set; } = string.Empty;
        /// <summary> ?ì„¸ ?´ìš© (? íƒ ?¬í•­) </summary>
        public string? Details { get; set; }
    }

    /// <summary>
    /// ?¨ê³„ ?„í™˜???„í•´ ?µê³¼?´ì•¼ ?˜ëŠ” ê²€ì¦?ì§€??Gate)???•ì˜?©ë‹ˆ??
    /// </summary>
    public class CoordinateGate
    {
        /// <summary> ê²Œì´??ëª…ì¹­ </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> ?µê³¼ ?¬ë? </summary>
        public bool IsPassed { get; set; }
        /// <summary> ?µê³¼ë¥??„í•´ ê·¼ê±°(Evidence) ê¸°ë¡???„ìˆ˜?¸ì? ?¬ë? </summary>
        public bool IsEvidenceRequired { get; set; } = true;
        /// <summary> ì¶”ê? ?˜ê²¬ </summary>
        public string? Comments { get; set; }
        /// <summary> ë§ˆì?ë§??…ë°?´íŠ¸ ?œê°„ </summary>
        public DateTime? UpdatedAt { get; set; }
        /// <summary> ?°ê²°??ê·¼ê±° ëª©ë¡ </summary>
        public List<CoordinateEvidence> Evidences { get; set; } = new();
        /// <summary> ?¹ì¸??</summary>
        public string? ApprovedBy { get; set; }
    }

    /// <summary>
    /// ?¬ëŸ¬ ?ì´?„íŠ¸ ê°„ì˜ ?‘ì—… ë°??¨ê³„ë³??¹ì¸???„ìš”??ì¡°ì • ?‘ì—…??ê´€ë¦¬í•˜???´ë˜?¤ì…?ˆë‹¤.
    /// </summary>
    public class CoordinateTask : TaskStateBase
    {
        /// <summary> ?‘ì—… ?œëª© </summary>
        public string Title { get; set; } = string.Empty;
        /// <summary> ?‘ì—… ?ì„¸ ?¤ëª… </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary> ?„ì¬ ì§„í–‰ ?¨ê³„ </summary>
        public CoordinatePhase CurrentPhase { get; set; } = CoordinatePhase.Planning;
        /// <summary> êµ¬ì„±??ê²€ì¦?ê²Œì´??ëª©ë¡ </summary>
        public List<CoordinateGate> Gates { get; set; } = new();
        /// <summary> ê²€???íƒœ </summary>
        public ReviewerDecision ReviewStatus { get; set; } = ReviewerDecision.Pending;
        /// <summary> ? ë‹¹???ì´?„íŠ¸ ëª…ì¹­ </summary>
        public string? AssignedAgent { get; set; }
        /// <summary> ?”êµ¬?˜ëŠ” ?ì´?„íŠ¸ ??•  </summary>
        public AgentRole RequiredRole { get; set; } = AgentRole.Worker;
        /// <summary> ?˜ì¡´?˜ê³  ?ˆëŠ” ?‘ì—… ID ëª©ë¡ </summary>
        public List<string> Dependencies { get; set; } = new();
        /// <summary> ?ìœ„ ?‘ì—… ID </summary>
        public string? ParentTaskId { get; set; }
        /// <summary> ?˜ìœ„ ?‘ì—… ID ëª©ë¡ </summary>
        public List<string> SubTaskIds { get; set; } = new();
        /// <summary> ?ì„± ?¼ì‹œ </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        /// <summary> ë§ˆì?ë§??˜ì • ?¼ì‹œ </summary>
        public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
        /// <summary> ?‘ì—… ë³€ê²??´ë ¥ </summary>
        public List<string> History { get; set; } = new();

        /// <summary> ë³‘í•© ì¤€ë¹??ìˆ˜ (0~100) </summary>
        public double ReadinessScore { get; set; }
        /// <summary> ì§„í–‰??ê°€ë¡œë§‰???”ì†Œ(Blocker) ëª©ë¡ </summary>
        public List<string> Blockers { get; set; } = new();

        public CoordinateTask()
        {
            Type = "Coordinate";
            Status = "Planning";
        }
    }
}
