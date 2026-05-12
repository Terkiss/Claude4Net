using System;
using System.Collections.Generic;
using Claude4Net.SDK.Events;

namespace Claude4Net.SDK
{
    /// <summary>
    /// CQRS 프로젝션 인터페이스입니다.
    /// 이벤트 스트림을 소비하여 읽기 모델(Read Model)을 구축합니다.
    /// </summary>
    public interface IEventProjection
    {
        /// <summary> 프로젝션 이름 </summary>
        string Name { get; }

        /// <summary> 단일 이벤트를 프로젝션에 적용합니다. </summary>
        void Apply(IAgentEvent @event);

        /// <summary> 프로젝션 상태를 초기화합니다. </summary>
        void Reset();
    }

    /// <summary>
    /// 세션 요약 프로젝션의 읽기 모델입니다.
    /// 세션의 전반적인 활동을 요약합니다.
    /// </summary>
    public sealed class SessionSummaryReadModel
    {
        /// <summary> 세션 ID </summary>
        public string SessionId { get; set; } = string.Empty;
        /// <summary> 세션 시작 시간 </summary>
        public DateTime? StartedAt { get; set; }
        /// <summary> 마지막 활동 시간 </summary>
        public DateTime? LastActivityAt { get; set; }
        /// <summary> 사용자 프롬프트 수 </summary>
        public int UserPromptCount { get; set; }
        /// <summary> 도구 호출 수 </summary>
        public int ToolCallCount { get; set; }
        /// <summary> 도구 오류 수 </summary>
        public int ToolErrorCount { get; set; }
        /// <summary> 최종 응답 수 </summary>
        public int FinalResponseCount { get; set; }
        /// <summary> 상태 전이 수 </summary>
        public int StateTransitionCount { get; set; }
        /// <summary> 총 이벤트 수 </summary>
        public int TotalEventCount { get; set; }
        /// <summary> 사용된 프로바이더 </summary>
        public string? Provider { get; set; }
        /// <summary> 사용된 모델 </summary>
        public string? Model { get; set; }
        /// <summary> 워크스페이스 경로 </summary>
        public string? WorkspacePath { get; set; }
    }

    /// <summary>
    /// 도구 사용 통계 프로젝션의 읽기 모델입니다.
    /// 개별 도구별 호출 빈도와 성공/실패율을 추적합니다.
    /// </summary>
    public sealed class ToolUsageReadModel
    {
        /// <summary> 도구 이름 </summary>
        public string ToolName { get; set; } = string.Empty;
        /// <summary> 호출 횟수 </summary>
        public int CallCount { get; set; }
        /// <summary> 성공 횟수 </summary>
        public int SuccessCount { get; set; }
        /// <summary> 실패 횟수 </summary>
        public int ErrorCount { get; set; }
        /// <summary> 마지막 호출 시간 </summary>
        public DateTime? LastCalledAt { get; set; }
    }

    /// <summary>
    /// 세션 요약 프로젝션입니다.
    /// 이벤트 스트림에서 세션의 전반적인 활동을 집계합니다.
    /// </summary>
    public sealed class SessionSummaryProjection : IEventProjection
    {
        public string Name => "SessionSummary";
        public SessionSummaryReadModel Model { get; private set; } = new();

        public void Apply(IAgentEvent @event)
        {
            Model.TotalEventCount++;
            Model.LastActivityAt = @event.Timestamp;

            switch (@event)
            {
                case SessionStartedEvent started:
                    Model.StartedAt = started.Timestamp;
                    Model.Provider = started.Provider;
                    Model.Model = started.Model;
                    Model.WorkspacePath = started.WorkspacePath;
                    break;

                case UserPromptReceivedEvent:
                    Model.UserPromptCount++;
                    break;

                case ToolCalledEvent:
                    Model.ToolCallCount++;
                    break;

                case ToolResultEvent toolResult:
                    if (toolResult.IsError) Model.ToolErrorCount++;
                    break;

                case FinalResponseGeneratedEvent:
                    Model.FinalResponseCount++;
                    break;

                case StateTransitionEvent:
                    Model.StateTransitionCount++;
                    break;
            }
        }

        public void Reset()
        {
            Model = new SessionSummaryReadModel();
        }
    }

    /// <summary>
    /// 도구 사용 통계 프로젝션입니다.
    /// 이벤트 스트림에서 도구별 호출 빈도와 성공/실패율을 집계합니다.
    /// </summary>
    public sealed class ToolUsageProjection : IEventProjection
    {
        public string Name => "ToolUsage";

        /// <summary> 도구별 사용 통계 (도구 이름 → 읽기 모델) </summary>
        public Dictionary<string, ToolUsageReadModel> ToolStats { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        // 현재 진행 중인 도구 호출의 ToolUseId → ToolName 매핑
        private readonly Dictionary<string, string> _pendingCalls = new();

        public void Apply(IAgentEvent @event)
        {
            switch (@event)
            {
                case ToolCalledEvent toolCalled:
                    if (!ToolStats.ContainsKey(toolCalled.ToolName))
                    {
                        ToolStats[toolCalled.ToolName] = new ToolUsageReadModel
                        {
                            ToolName = toolCalled.ToolName
                        };
                    }
                    ToolStats[toolCalled.ToolName].CallCount++;
                    ToolStats[toolCalled.ToolName].LastCalledAt = toolCalled.Timestamp;
                    _pendingCalls[toolCalled.ToolUseId] = toolCalled.ToolName;
                    break;

                case ToolResultEvent toolResult:
                    if (_pendingCalls.TryGetValue(toolResult.ToolUseId, out var toolName) &&
                        ToolStats.TryGetValue(toolName, out var stats))
                    {
                        if (toolResult.IsError)
                            stats.ErrorCount++;
                        else
                            stats.SuccessCount++;

                        _pendingCalls.Remove(toolResult.ToolUseId);
                    }
                    break;
            }
        }

        public void Reset()
        {
            ToolStats = new Dictionary<string, ToolUsageReadModel>(StringComparer.OrdinalIgnoreCase);
            _pendingCalls.Clear();
        }
    }
}
