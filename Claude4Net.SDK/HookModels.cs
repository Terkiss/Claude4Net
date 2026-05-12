using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 훅 실행 시점을 정의합니다.
    /// </summary>
    public enum HookTiming
    {
        /// <summary> 도구 실행 전 </summary>
        BeforeToolExecution,
        /// <summary> 도구 실행 후 </summary>
        AfterToolExecution,
        /// <summary> 도구 실행 실패 시 </summary>
        OnToolError
    }

    /// <summary>
    /// 훅에 전달되는 컨텍스트 정보입니다.
    /// </summary>
    public sealed class HookContext
    {
        /// <summary> 도구 이름 </summary>
        public string ToolName { get; init; } = string.Empty;
        /// <summary> 도구 입력 인자 (JSON 문자열) </summary>
        public string? Arguments { get; init; }
        /// <summary> 도구 실행 결과 (AfterToolExecution 시점에서만 사용) </summary>
        public string? Result { get; set; }
        /// <summary> 실행 오류 여부 </summary>
        public bool IsError { get; set; }
        /// <summary> 실행 시간 (ms) </summary>
        public double? ElapsedMs { get; set; }
        /// <summary> 세션 ID </summary>
        public string? SessionId { get; init; }
        /// <summary> 훅에서 공유할 수 있는 메타데이터 </summary>
        public Dictionary<string, object> Metadata { get; init; } = new();
    }

    /// <summary>
    /// 훅 실행 결과입니다.
    /// </summary>
    public sealed class HookResult
    {
        /// <summary> 훅 이름 </summary>
        public string HookName { get; init; } = string.Empty;
        /// <summary> 실행 성공 여부 </summary>
        public bool Success { get; init; }
        /// <summary> 실행을 중단해야 하는지 여부 (Before 훅에서만 유효) </summary>
        public bool ShouldAbort { get; init; }
        /// <summary> 중단 이유 </summary>
        public string? AbortReason { get; init; }
        /// <summary> 오류 메시지 (실패 시) </summary>
        public string? Error { get; init; }

        /// <summary>
        /// 성공 결과를 생성합니다.
        /// </summary>
        public static HookResult Ok(string hookName) => new()
        {
            HookName = hookName,
            Success = true
        };

        /// <summary>
        /// 실패 결과를 생성합니다.
        /// </summary>
        public static HookResult Fail(string hookName, string error) => new()
        {
            HookName = hookName,
            Success = false,
            Error = error
        };

        /// <summary>
        /// 실행 중단 결과를 생성합니다 (Before 훅 전용).
        /// </summary>
        public static HookResult Abort(string hookName, string reason) => new()
        {
            HookName = hookName,
            Success = true,
            ShouldAbort = true,
            AbortReason = reason
        };
    }

    /// <summary>
    /// 도구 실행 파이프라인에 삽입되는 훅 인터페이스입니다.
    /// </summary>
    public interface IToolHook
    {
        /// <summary> 훅 이름 </summary>
        string Name { get; }
        /// <summary> 훅이 실행되는 시점 </summary>
        HookTiming Timing { get; }
        /// <summary> 훅 우선순위 (낮을수록 먼저 실행) </summary>
        int Priority { get; }
        /// <summary> 훅 활성화 여부 </summary>
        bool IsEnabled { get; set; }
        /// <summary> 훅을 실행합니다. </summary>
        Task<HookResult> ExecuteAsync(HookContext context);
    }
}
