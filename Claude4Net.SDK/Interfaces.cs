using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;

namespace Claude4Net.SDK
{
    /// <summary>
    /// ?œìŠ¤?œì—???¬ìš©???„êµ¬(Tool)???œì? ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface ITool
    {
        /// <summary> ?„êµ¬??ê³ ìœ  ëª…ì¹­?…ë‹ˆ?? </summary>
        string Name { get; }
        /// <summary> ?„êµ¬??ê¸°ëŠ¥???€???¤ëª…?…ë‹ˆ?? LLM???„êµ¬ë¥?? íƒ????ì°¸ì¡°?©ë‹ˆ?? </summary>
        string Description { get; }
        /// <summary> ?„êµ¬??ë³„ì¹­ ëª©ë¡?…ë‹ˆ?? </summary>
        IEnumerable<string>? Aliases => null;
        /// <summary> ?„êµ¬ê°€ ?”êµ¬?˜ëŠ” ?…ë ¥ ?¤í‚¤ë§?JSON Schema ???…ë‹ˆ?? </summary>
        object? InputSchema { get; }
        /// <summary> ë³‘ë ¬ ?¤í–‰ ê°€???¬ë?ë¥??˜í??…ë‹ˆ?? </summary>
        bool IsConcurrencySafe => false;
        /// <summary> ?„êµ¬ë¥??¤í–‰?©ë‹ˆ?? </summary>
        Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default);
    }

    /// <summary>
    /// ?¤í–‰ ??ë³€ê²??¬í•­???€???„ë¦¬ë·°ë? ?œê³µ?????ˆëŠ” ?„êµ¬ ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface IPreviewableTool : ITool
    {
        /// <summary> ?¤í–‰ ??ë°œìƒ??ë³€ê²??¬í•­???€???„ë¦¬ë·°ë? ?ì„±?©ë‹ˆ?? </summary>
        Task<FileDiffPreview?> GetPreviewAsync(string arguments);
    }

    /// <summary>
    /// ?±ë¡???„êµ¬?¤ì„ ê´€ë¦¬í•˜ê³?ê²€?‰í•˜???ˆì??¤íŠ¸ë¦??¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface IToolRegistry
    {
        /// <summary> ?±ë¡??ëª¨ë“  ?„êµ¬ ëª©ë¡??ë°˜í™˜?©ë‹ˆ?? </summary>
        IReadOnlyList<ITool> GetTools();
        /// <summary> ?´ë¦„?¼ë¡œ ?¹ì • ?„êµ¬ë¥?ê²€?‰í•©?ˆë‹¤. </summary>
        ITool? GetTool(string name);
    }

    /// <summary>
    /// ?¬ìš©???¹ì¸???„ìš”???‘ì—…???€??ì²˜ë¦¬ ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface IUserApprovalHandler
    {
        /// <summary> ?¹ì • ?„êµ¬ ?¤í–‰???€???¬ìš©?ì—ê²??¹ì¸???”ì²­?©ë‹ˆ?? </summary>
        Task<bool> RequestApprovalAsync(string tool, string args);
    }

    /// <summary>
    /// ?ë???ì»¨í…?¤íŠ¸(Diff ??ë¥??¬í•¨?˜ì—¬ ?¹ì¸???”ì²­?????ˆëŠ” ?¸ë“¤???¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface IRichApprovalHandler : IUserApprovalHandler
    {
        /// <summary> ?Œì¼ ë³€ê²??¬í•­(Diff)???¬í•¨?˜ì—¬ ?¬ìš©?ì—ê²??¹ì¸???”ì²­?©ë‹ˆ?? </summary>
        Task<bool> RequestApprovalWithDiffAsync(string tool, string args, FileDiffPreview diff);
    }

    /// <summary>
    /// LLM ?¤íŠ¸ë¦??´ë²¤?¸ì˜ ? í˜•???•ì˜?©ë‹ˆ??
    /// </summary>
    public enum LLMStreamEventType
    {
        /// <summary> ?¬ê³  ê³¼ì •(Thinking)???¼ë? </summary>
        ThinkingDelta,
        /// <summary> ?ìŠ¤???‘ë‹µ???¼ë? </summary>
        TextDelta,
        /// <summary> ?„êµ¬ ?¸ì¶œ ?œì‘ </summary>
        ToolCallStart,
        /// <summary> ?¤íŠ¸ë¦??„ë£Œ </summary>
        Completed
    }

    /// <summary>
    /// ê¶Œí•œ ì²˜ë¦¬ ëª¨ë“œë¥??•ì˜?©ë‹ˆ??
    /// </summary>
    public enum PermissionMode
    {
        /// <summary> Read-only mode. Write and shell execution are blocked. </summary>
        ReadOnly,
        /// <summary> Workspace writes are allowed through normal safety checks. </summary>
        WorkspaceWrite,
        /// <summary> Sensitive workspace actions require user approval. </summary>
        Prompt,
        /// <summary> Full access mode. Outside-workspace access still requires explicit approval. </summary>
        DangerFullAccess,
        /// <summary> Legacy alias for Prompt. </summary>
        Default,
        /// <summary> Legacy alias for DangerFullAccess. </summary>
        Yolo,
        /// <summary> Legacy alias for DangerFullAccess. </summary>
        BypassPermissions
    }

    /// <summary>
    /// LLM?ì„œ ?„ë‹¬?˜ëŠ” ?¤íŠ¸ë¦??´ë²¤??ëª¨ë¸?…ë‹ˆ??
    /// </summary>
    public class LLMStreamEvent
    {
        /// <summary> ?´ë²¤??? í˜• </summary>
        public LLMStreamEventType Type { get; set; }
        /// <summary> ì¶”ê????ìŠ¤??ì¡°ê° </summary>
        public string Delta { get; set; } = string.Empty;
        /// <summary> ?„êµ¬ ?¸ì¶œ ?”ì²­ ?•ë³´ (?ˆì„ ê²½ìš°) </summary>
        public ToolUseRequest? ToolCall { get; set; }
        /// <summary> ìµœì¢… ?‘ë‹µ (?„ë£Œ ???œê³µ) </summary>
        public LLMResponse? FinalResponse { get; set; }
    }

    /// <summary>
    /// LLM???„êµ¬ ?¬ìš© ?”ì²­ ?•ë³´ë¥??´ëŠ” ëª¨ë¸?…ë‹ˆ??
    /// </summary>
    public class ToolUseRequest
    {
        /// <summary> ?¸ì¶œ ID </summary>
        public string Id { get; set; } = string.Empty;
        /// <summary> ?¤í–‰???„êµ¬ ?´ë¦„ </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> ?„êµ¬???„ë‹¬???…ë ¥ ?Œë¼ë¯¸í„° </summary>
        public object? Input { get; set; }
    }

    /// <summary>
    /// ?„êµ¬ ?¤í–‰ ê²°ê³¼ë¥??´ëŠ” ëª¨ë¸?…ë‹ˆ??
    /// </summary>
    public class ToolUseResult
    {
        /// <summary> ?”ì²­???„êµ¬ ?¬ìš© ID </summary>
        public string ToolUseId { get; set; } = string.Empty;
        /// <summary> ?¤í–‰ ê²°ê³¼ ?´ìš© </summary>
        public object? Content { get; set; }
        /// <summary> ?¤ë¥˜ ë°œìƒ ?¬ë? </summary>
        public bool IsError { get; set; }
    }

    /// <summary>
    /// LLM???µí•© ?‘ë‹µ ëª¨ë¸?…ë‹ˆ??
    /// </summary>
    public class LLMResponse
    {
        /// <summary> ?ì„±???ìŠ¤??ê²°ê³¼ </summary>
        public string Text { get; set; } = string.Empty;
        /// <summary> ?¬í•¨???„êµ¬ ?¸ì¶œ ?”ì²­ ëª©ë¡ </summary>
        public List<ToolUseRequest> ToolCalls { get; set; } = new();
    }

    /// <summary>
    /// ? í° ê°œìˆ˜ë¥?ê³„ì‚°?˜ê¸° ?„í•œ ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface ITokenCounter
    {
        /// <summary> ì£¼ì–´ì§??ìŠ¤?¸ì˜ ? í° ê°œìˆ˜ë¥?ê³„ì‚°?©ë‹ˆ?? </summary>
        int CountTokens(string text);
        /// <summary> ë©”ì‹œì§€ ê°ì²´??? í° ê°œìˆ˜ë¥?ê³„ì‚°?©ë‹ˆ?? </summary>
        int CountTokens(object message);
        /// <summary> ?€???´ì—­ ?„ì²´??? í° ê°œìˆ˜ë¥?ê³„ì‚°?©ë‹ˆ?? </summary>
        int CountTokens(IEnumerable<object> messages);
    }

    /// <summary>
    /// ê²°ì •ë¡ ì ??ê¸°ë³¸ ? í° ì¹´ìš´??êµ¬í˜„ì²´ì…?ˆë‹¤. (ê°„ë‹¨???´ë¦¬?¤í‹± ?¬ìš©)
    /// </summary>
    public class DefaultTokenCounter : ITokenCounter
    {
        public int CountTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // ?¼ë°˜?ì¸ ?ì–´ ê¸°ì? 4ê¸€?ë‹¹ 1? í°, ?œê??€ ê¸€?ë‹¹ ??2? í°?¼ë¡œ ê³„ì‚°?˜ëŠ” ?´ë¦¬?¤í‹±
            // ?¬ê¸°?œëŠ” ë³´ìˆ˜?ìœ¼ë¡?(ê¸€????/ 2) + 2 ?•ë„ë¡?ê³„ì‚°?˜ì—¬ ?ˆì „?˜ê²Œ ì¶”ì •
            return (text.Length / 2) + 2;
        }

        public int CountTokens(object message)
        {
            if (message == null) return 0;
            try
            {
                var json = JsonSerializer.Serialize(message);
                return CountTokens(json);
            }
            catch
            {
                return 0;
            }
        }

        public int CountTokens(IEnumerable<object> messages)
        {
            if (messages == null) return 0;
            return messages.Sum(m => CountTokens(m));
        }
    }

    /// <summary>
    /// LLM ?œë¹„???œê³µ??Claude, Gemini ??ë¥??„í•œ ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface ILLMProvider
    {
        /// <summary> ?œê³µ??ëª…ì¹­ </summary>
        string Name { get; }
        /// <summary> ì§ˆì˜ë¥?ë¹„ë™ê¸??¤íŠ¸ë¦?ë°©ì‹?¼ë¡œ ?¤í–‰?©ë‹ˆ?? </summary>
        IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, System.Threading.CancellationToken ct = default);
        /// <summary> ?€???´ì—­??ë©”ì‹œì§€ë¥?ì¶”ê??©ë‹ˆ?? </summary>
        void AddMessage(object message);
        /// <summary> ?„ì¬ê¹Œì????€???´ì—­??ê°€?¸ì˜µ?ˆë‹¤. </summary>
        IReadOnlyList<object> GetHistory();
        /// <summary> ?€???´ì—­???ˆë¡œ??ëª©ë¡?¼ë¡œ ?€ì²´í•©?ˆë‹¤. (?•ì¶• ?±ì—???¬ìš©) </summary>
        void SetHistory(IEnumerable<object> history);
        /// <summary> ?´ë‹¹ ?œê³µ?ìš© ? í° ì¹´ìš´?°ë? ê°€?¸ì˜µ?ˆë‹¤. </summary>
        ITokenCounter TokenCounter { get; }
        /// <summary> ?´ë‹¹ ?œê³µ?ì˜ ?„ì¬ ëª¨ë¸ ì»¨í…?¤íŠ¸ ?œí•œ??ê°€?¸ì˜µ?ˆë‹¤. </summary>
        int ContextLimit { get; }
    }

    /// <summary>
    /// ?ìŠ¤???„ë² ?©ì„ ?ì„±???„í•œ ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface IEmbeddingProvider
    {
        /// <summary> ?ìŠ¤?¸ì— ?€???„ë² ??ë²¡í„°ë¥?ë¹„ë™ê¸°ì ?¼ë¡œ ê°€?¸ì˜µ?ˆë‹¤. </summary>
        Task<float[]> GetEmbeddingAsync(string text, System.Threading.CancellationToken ct = default);
    }

    /// <summary>
    /// ?ì´?„íŠ¸ ?´ë²¤?¸ë? ?€?œë³´???±ìœ¼ë¡??„íŒŒ?˜ê¸° ?„í•œ ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface IAgentEventBroadcaster
    {
        /// <summary> ?ì´?„íŠ¸ ?´ë²¤?¸ë? ë¸Œë¡œ?œìº?¤íŠ¸?©ë‹ˆ?? </summary>
        Task BroadcastAsync(Events.IAgentEvent @event);
        /// <summary> ?¹ì¸ ?”ì²­??ë¸Œë¡œ?œìº?¤íŠ¸?©ë‹ˆ?? </summary>
        Task BroadcastApprovalRequestAsync(string requestId, string message);
    }
}
