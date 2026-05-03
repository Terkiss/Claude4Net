using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 시스템에서 사용할 도구(Tool)의 표준 인터페이스입니다.
    /// </summary>
    public interface ITool
    {
        /// <summary> 도구의 고유 명칭입니다. </summary>
        string Name { get; }
        /// <summary> 도구의 기능에 대한 설명입니다. LLM이 도구를 선택할 때 참조합니다. </summary>
        string Description { get; }
        /// <summary> 도구의 별칭 목록입니다. </summary>
        IEnumerable<string>? Aliases => null;
        /// <summary> 도구가 요구하는 입력 스키마(JSON Schema 등)입니다. </summary>
        object? InputSchema { get; }
        /// <summary> 병렬 실행 가능 여부를 나타냅니다. </summary>
        bool IsConcurrencySafe => false;
        /// <summary> 도구를 실행합니다. </summary>
        Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default);
    }

    /// <summary>
    /// 등록된 도구들을 관리하고 검색하는 레지스트리 인터페이스입니다.
    /// </summary>
    public interface IToolRegistry
    {
        /// <summary> 등록된 모든 도구 목록을 반환합니다. </summary>
        IReadOnlyList<ITool> GetTools();
        /// <summary> 이름으로 특정 도구를 검색합니다. </summary>
        ITool? GetTool(string name);
    }

    /// <summary>
    /// 사용자 승인이 필요한 작업에 대한 처리 인터페이스입니다.
    /// </summary>
    public interface IUserApprovalHandler
    {
        /// <summary> 특정 도구 실행에 대해 사용자에게 승인을 요청합니다. </summary>
        Task<bool> RequestApprovalAsync(string tool, string args);
    }

    /// <summary>
    /// LLM 스트림 이벤트의 유형을 정의합니다.
    /// </summary>
    public enum LLMStreamEventType
    {
        /// <summary> 사고 과정(Thinking)의 일부 </summary>
        ThinkingDelta,
        /// <summary> 텍스트 응답의 일부 </summary>
        TextDelta,
        /// <summary> 도구 호출 시작 </summary>
        ToolCallStart,
        /// <summary> 스트림 완료 </summary>
        Completed
    }

    /// <summary>
    /// 권한 처리 모드를 정의합니다.
    /// </summary>
    public enum PermissionMode
    {
        /// <summary> 기본 모드 (안전 확인 필요) </summary>
        Default,
        /// <summary> 승인 절차 없이 즉시 실행 </summary>
        Yolo,
        /// <summary> 모든 권한 검사를 우회 </summary>
        BypassPermissions
    }

    /// <summary>
    /// LLM에서 전달되는 스트림 이벤트 모델입니다.
    /// </summary>
    public class LLMStreamEvent
    {
        /// <summary> 이벤트 유형 </summary>
        public LLMStreamEventType Type { get; set; }
        /// <summary> 추가된 텍스트 조각 </summary>
        public string Delta { get; set; } = string.Empty;
        /// <summary> 도구 호출 요청 정보 (있을 경우) </summary>
        public ToolUseRequest? ToolCall { get; set; }
        /// <summary> 최종 응답 (완료 시 제공) </summary>
        public LLMResponse? FinalResponse { get; set; }
    }

    /// <summary>
    /// LLM의 도구 사용 요청 정보를 담는 모델입니다.
    /// </summary>
    public class ToolUseRequest
    {
        /// <summary> 호출 ID </summary>
        public string Id { get; set; } = string.Empty;
        /// <summary> 실행할 도구 이름 </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> 도구에 전달될 입력 파라미터 </summary>
        public object? Input { get; set; }
    }

    /// <summary>
    /// 도구 실행 결과를 담는 모델입니다.
    /// </summary>
    public class ToolUseResult
    {
        /// <summary> 요청된 도구 사용 ID </summary>
        public string ToolUseId { get; set; } = string.Empty;
        /// <summary> 실행 결과 내용 </summary>
        public object? Content { get; set; }
        /// <summary> 오류 발생 여부 </summary>
        public bool IsError { get; set; }
    }

    /// <summary>
    /// LLM의 통합 응답 모델입니다.
    /// </summary>
    public class LLMResponse
    {
        /// <summary> 생성된 텍스트 결과 </summary>
        public string Text { get; set; } = string.Empty;
        /// <summary> 포함된 도구 호출 요청 목록 </summary>
        public List<ToolUseRequest> ToolCalls { get; set; } = new();
    }

    /// <summary>
    /// LLM 서비스 제공자(Claude, Gemini 등)를 위한 인터페이스입니다.
    /// </summary>
    public interface ILLMProvider
    {
        /// <summary> 제공자 명칭 </summary>
        string Name { get; }
        /// <summary> 질의를 비동기 스트림 방식으로 실행합니다. </summary>
        IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, System.Threading.CancellationToken ct = default);
        /// <summary> 대화 내역에 메시지를 추가합니다. </summary>
        void AddMessage(object message);
        /// <summary> 현재까지의 대화 내역을 가져옵니다. </summary>
        IReadOnlyList<object> GetHistory();
    }

    /// <summary>
    /// 텍스트 임베딩을 생성을 위한 인터페이스입니다.
    /// </summary>
    public interface IEmbeddingProvider
    {
        /// <summary> 텍스트에 대한 임베딩 벡터를 비동기적으로 가져옵니다. </summary>
        Task<float[]> GetEmbeddingAsync(string text, System.Threading.CancellationToken ct = default);
    }
}
