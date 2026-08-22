using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;

namespace Claude4Net.SDK
{
    /// <summary>
    /// ?�스?�에???�용???�구(Tool)???��? ?�터?�이?�입?�다.
    /// </summary>
    public interface ITool
    {
        /// <summary> ?�구??고유 명칭?�니?? </summary>
        string Name { get; }
        /// <summary> ?�구??기능???�???�명?�니?? LLM???�구�??�택????참조?�니?? </summary>
        string Description { get; }
        /// <summary> ?�구??별칭 목록?�니?? </summary>
        IEnumerable<string>? Aliases => null;
        /// <summary> ?�구가 ?�구?�는 ?�력 ?�키�?JSON Schema ???�니?? </summary>
        object? InputSchema { get; }
        /// <summary> 병렬 ?�행 가???��?�??��??�니?? </summary>
        bool IsConcurrencySafe => false;
        /// <summary> ?�구�??�행?�니?? </summary>
        Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default);
    }

    /// <summary>
    /// ?�행 ??변�??�항???�???�리뷰�? ?�공?????�는 ?�구 ?�터?�이?�입?�다.
    /// </summary>
    public interface IPreviewableTool : ITool
    {
        /// <summary> ?�행 ??발생??변�??�항???�???�리뷰�? ?�성?�니?? </summary>
        Task<FileDiffPreview?> GetPreviewAsync(string arguments);
    }

    /// <summary>
    /// ?�록???�구?�을 관리하�?검?�하???��??�트�??�터?�이?�입?�다.
    /// </summary>
    public interface IToolRegistry
    {
        /// <summary> ?�록??모든 ?�구 목록??반환?�니?? </summary>
        IReadOnlyList<ITool> GetTools();
        /// <summary> ?�름?�로 ?�정 ?�구�?검?�합?�다. </summary>
        ITool? GetTool(string name);
    }

    /// <summary>
    /// ?�용???�인???�요???�업???�??처리 ?�터?�이?�입?�다.
    /// </summary>
    public interface IUserApprovalHandler
    {
        /// <summary> ?�정 ?�구 ?�행???�???�용?�에�??�인???�청?�니?? </summary>
        Task<bool> RequestApprovalAsync(string tool, string args);
    }

    /// <summary>
    /// ?��???컨텍?�트(Diff ??�??�함?�여 ?�인???�청?????�는 ?�들???�터?�이?�입?�다.
    /// </summary>
    public interface IRichApprovalHandler : IUserApprovalHandler
    {
        /// <summary> ?�일 변�??�항(Diff)???�함?�여 ?�용?�에�??�인???�청?�니?? </summary>
        Task<bool> RequestApprovalWithDiffAsync(string tool, string args, FileDiffPreview diff);
    }

    /// <summary>
    /// LLM ?�트�??�벤?�의 ?�형???�의?�니??
    /// </summary>
    public enum LLMStreamEventType
    {
        /// <summary> ?�고 과정(Thinking)???��? </summary>
        ThinkingDelta,
        /// <summary> ?�스???�답???��? </summary>
        TextDelta,
        /// <summary> ?�구 ?�출 ?�작 </summary>
        ToolCallStart,
        /// <summary> ?�트�??�료 </summary>
        Completed
    }

    /// <summary>
    /// 권한 처리 모드�??�의?�니??
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
    /// LLM?�서 ?�달?�는 ?�트�??�벤??모델?�니??
    /// </summary>
    public class LLMStreamEvent
    {
        /// <summary> ?�벤???�형 </summary>
        public LLMStreamEventType Type { get; set; }
        /// <summary> 추�????�스??조각 </summary>
        public string Delta { get; set; } = string.Empty;
        /// <summary> ?�구 ?�출 ?�청 ?�보 (?�을 경우) </summary>
        public ToolUseRequest? ToolCall { get; set; }
        /// <summary> 최종 ?�답 (?�료 ???�공) </summary>
        public LLMResponse? FinalResponse { get; set; }
    }

    /// <summary>
    /// LLM???�구 ?�용 ?�청 ?�보�??�는 모델?�니??
    /// </summary>
    public class ToolUseRequest
    {
        /// <summary> ?�출 ID </summary>
        public string Id { get; set; } = string.Empty;
        /// <summary> ?�행???�구 ?�름 </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> ?�구???�달???�력 ?�라미터 </summary>
        public object? Input { get; set; }
    }

    /// <summary>
    /// ?�구 ?�행 결과�??�는 모델?�니??
    /// </summary>
    public class ToolUseResult
    {
        /// <summary> ?�청???�구 ?�용 ID </summary>
        public string ToolUseId { get; set; } = string.Empty;
        /// <summary> ?�행 결과 ?�용 </summary>
        public object? Content { get; set; }
        /// <summary> ?�류 발생 ?��? </summary>
        public bool IsError { get; set; }
    }

    /// <summary>
    /// LLM???�합 ?�답 모델?�니??
    /// </summary>
    public class LLMResponse
    {
        /// <summary> ?�성???�스??결과 </summary>
        public string Text { get; set; } = string.Empty;
        /// <summary> ?�함???�구 ?�출 ?�청 목록 </summary>
        public List<ToolUseRequest> ToolCalls { get; set; } = new();
    }

    /// <summary>
    /// ?�큰 개수�?계산?�기 ?�한 ?�터?�이?�입?�다.
    /// </summary>
    public interface ITokenCounter
    {
        /// <summary> 주어�??�스?�의 ?�큰 개수�?계산?�니?? </summary>
        int CountTokens(string text);
        /// <summary> 메시지 객체???�큰 개수�?계산?�니?? </summary>
        int CountTokens(object message);
        /// <summary> ?�???�역 ?�체???�큰 개수�?계산?�니?? </summary>
        int CountTokens(IEnumerable<object> messages);
    }

    /// <summary>
    /// 결정론적??기본 ?�큰 카운??구현체입?�다. (간단???�리?�틱 ?�용)
    /// </summary>
    public class DefaultTokenCounter : ITokenCounter
    {
        public int CountTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // ?�반?�인 ?�어 기�? 4글?�당 1?�큰, ?��??� 글?�당 ??2?�큰?�로 계산?�는 ?�리?�틱
            // ?�기?�는 보수?�으�?(글????/ 2) + 2 ?�도�?계산?�여 ?�전?�게 추정
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
    /// LLM ?�비???�공??Claude, Gemini ??�??�한 ?�터?�이?�입?�다.
    /// </summary>
    public interface ILLMProvider
    {
        /// <summary> ?�공??명칭 </summary>
        string Name { get; }
        /// <summary> 질의�?비동�??�트�?방식?�로 ?�행?�니?? </summary>
        IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, System.Threading.CancellationToken ct = default);
        /// <summary> ?�???�역??메시지�?추�??�니?? </summary>
        void AddMessage(object message);
        /// <summary> ?�재까�????�???�역??가?�옵?�다. </summary>
        IReadOnlyList<object> GetHistory();
        /// <summary> ?�???�역???�로??목록?�로 ?�체합?�다. (?�축 ?�에???�용) </summary>
        void SetHistory(IEnumerable<object> history);
        /// <summary> ?�당 ?�공?�용 ?�큰 카운?��? 가?�옵?�다. </summary>
        ITokenCounter TokenCounter { get; }
        /// <summary> ?�당 ?�공?�의 ?�재 모델 컨텍?�트 ?�한??가?�옵?�다. </summary>
        int ContextLimit { get; }
    }

    /// <summary>
    /// ?�스???�베?�을 ?�성???�한 ?�터?�이?�입?�다.
    /// </summary>
    public interface IEmbeddingProvider
    {
        string ProviderId { get; }
        string ModelId { get; }
        /// <summary> ?�스?�에 ?�???�베??벡터�?비동기적?�로 가?�옵?�다. </summary>
        Task<float[]> GetEmbeddingAsync(string text, System.Threading.CancellationToken ct = default);
    }

    /// <summary>
    /// ?�이?�트 ?�벤?��? ?�?�보???�으�??�파?�기 ?�한 ?�터?�이?�입?�다.
    /// </summary>
    public interface IAgentEventBroadcaster
    {
        /// <summary> ?�이?�트 ?�벤?��? 브로?�캐?�트?�니?? </summary>
        Task BroadcastAsync(Events.IAgentEvent @event);
        /// <summary> ?�인 ?�청??브로?�캐?�트?�니?? </summary>
        Task BroadcastApprovalRequestAsync(string requestId, string message);
    }
}
