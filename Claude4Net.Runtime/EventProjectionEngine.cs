using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// CQRS 프로젝션 엔진입니다.
    /// 이벤트 스트림을 읽어 등록된 프로젝션에 적용하고, 읽기 모델을 구축합니다.
    /// </summary>
    public sealed class EventProjectionEngine
    {
        private readonly IAgentEventStore _eventStore;
        private readonly List<IEventProjection> _projections = new();
        private long _lastProcessedVersion;

        /// <summary>
        /// 프로젝션 엔진을 초기화합니다.
        /// </summary>
        /// <param name="eventStore">이벤트를 읽을 이벤트 스토어</param>
        public EventProjectionEngine(IAgentEventStore eventStore)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        }

        /// <summary>
        /// 프로젝션을 등록합니다.
        /// </summary>
        public EventProjectionEngine RegisterProjection(IEventProjection projection)
        {
            if (projection == null) throw new ArgumentNullException(nameof(projection));
            _projections.Add(projection);
            return this;
        }

        /// <summary>
        /// 등록된 모든 프로젝션을 반환합니다.
        /// </summary>
        public IReadOnlyList<IEventProjection> Projections => _projections.AsReadOnly();

        /// <summary>
        /// 이벤트 스트림을 재생하여 모든 프로젝션을 구축합니다.
        /// </summary>
        /// <param name="sessionId">재생할 세션 ID</param>
        /// <param name="fromVersion">시작 버전 (기본값: 0, 처음부터)</param>
        public async Task ReplayAsync(string sessionId, long fromVersion = 0)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));

            var events = await _eventStore.GetEventsAsync(sessionId, fromVersion);

            foreach (var @event in events)
            {
                foreach (var projection in _projections)
                {
                    projection.Apply(@event);
                }
                _lastProcessedVersion = @event.Version;
            }
        }

        /// <summary>
        /// 모든 프로젝션을 초기화하고 처음부터 재구축합니다.
        /// </summary>
        /// <param name="sessionId">재구축할 세션 ID</param>
        public async Task RebuildAsync(string sessionId)
        {
            foreach (var projection in _projections)
            {
                projection.Reset();
            }
            _lastProcessedVersion = 0;
            await ReplayAsync(sessionId, 0);
        }

        /// <summary>
        /// 마지막으로 처리한 버전 이후의 새 이벤트만 적용합니다 (증분 업데이트).
        /// </summary>
        /// <param name="sessionId">세션 ID</param>
        public async Task CatchUpAsync(string sessionId)
        {
            await ReplayAsync(sessionId, _lastProcessedVersion);
        }

        /// <summary>
        /// 타입으로 특정 프로젝션을 가져옵니다.
        /// </summary>
        public T? GetProjection<T>() where T : class, IEventProjection
        {
            return _projections.OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// 마지막으로 처리된 이벤트 버전을 반환합니다.
        /// </summary>
        public long LastProcessedVersion => _lastProcessedVersion;

        public void ApplyEvents(IEnumerable<IAgentEvent> events)
        {
            foreach (var @event in events)
            {
                foreach (var projection in _projections)
                {
                    projection.Apply(@event);
                }
                _lastProcessedVersion = @event.Version;
            }
        }
    }

    public class UsageReadModel
    {
        public string SessionId { get; set; } = string.Empty;
        public int TotalCalls { get; set; }
        public int TotalInputTokens { get; set; }
        public int TotalOutputTokens { get; set; }
        public double TotalCost { get; set; }
        public double LatencyEma { get; set; } = 1000.0;
        public Dictionary<string, ModelUsageMetrics> ModelMetrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class ModelUsageMetrics
    {
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int CallCount { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public double LatencyEma { get; set; } = 1000.0;
        public double AccumulatedCost { get; set; }
    }

    public class LlmUsageEvent : AgentEventBase
    {
        public override string EventType => "LlmUsage";
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public double LatencyMs { get; set; }
    }

    public class UsageProjection : IEventProjection
    {
        private readonly ProviderRegistry _registry;
        private DateTime? _lastRequestTime;
        private const double Alpha = 0.3;

        public string Name => "Usage";
        public UsageReadModel Model { get; private set; } = new();

        public UsageProjection(ProviderRegistry? registry = null)
        {
            _registry = registry ?? ProviderRegistry.CreateWithDefaults();
        }

        public void Reset()
        {
            Model = new UsageReadModel();
            _lastRequestTime = null;
        }

        private string _currentProvider = "unknown";
        private string _currentModel = "unknown";

        public void Apply(IAgentEvent @event)
        {
            switch (@event)
            {
                case SessionStartedEvent sessionStarted:
                    _currentProvider = string.IsNullOrWhiteSpace(sessionStarted.Provider) ? "unknown" : sessionStarted.Provider;
                    _currentModel = string.IsNullOrWhiteSpace(sessionStarted.Model) ? "unknown" : sessionStarted.Model;
                    break;

                case TaskAttemptStartedEvent taskStarted:
                    if (!string.IsNullOrWhiteSpace(taskStarted.ProviderId))
                        _currentProvider = taskStarted.ProviderId;
                    if (!string.IsNullOrWhiteSpace(taskStarted.ModelId))
                        _currentModel = taskStarted.ModelId;
                    break;

                case UserPromptReceivedEvent userPrompt:
                    if (_lastRequestTime == null)
                    {
                        _lastRequestTime = userPrompt.Timestamp;
                    }
                    // Estimate input tokens
                    int estimatedInput = Math.Max(1, userPrompt.Prompt.Length / 4);
                    RecordUsage(_currentProvider, _currentModel, estimatedInput, 0, null);
                    break;

                case ToolResultEvent toolResult:
                    if (_lastRequestTime == null)
                    {
                        _lastRequestTime = toolResult.Timestamp;
                    }
                    break;

                case AgentThoughtEvent thought:
                    if (_lastRequestTime != null)
                    {
                        double latency = Math.Max(0, (thought.Timestamp - _lastRequestTime.Value).TotalMilliseconds);
                        _lastRequestTime = null;
                        int estimatedOutput = Math.Max(1, thought.Thought.Length / 4);
                        RecordUsage(_currentProvider, _currentModel, 0, estimatedOutput, latency);
                    }
                    else
                    {
                        int estimatedOutput = Math.Max(1, thought.Thought.Length / 4);
                        RecordUsage(_currentProvider, _currentModel, 0, estimatedOutput, null);
                    }
                    break;

                case FinalResponseGeneratedEvent finalResponse:
                    if (_lastRequestTime != null)
                    {
                        double latency = Math.Max(0, (finalResponse.Timestamp - _lastRequestTime.Value).TotalMilliseconds);
                        _lastRequestTime = null;
                        int estimatedOutput = Math.Max(1, finalResponse.Response.Length / 4);
                        RecordUsage(_currentProvider, _currentModel, 0, estimatedOutput, latency);
                    }
                    else
                    {
                        int estimatedOutput = Math.Max(1, finalResponse.Response.Length / 4);
                        RecordUsage(_currentProvider, _currentModel, 0, estimatedOutput, null);
                    }
                    break;

                case ToolCalledEvent:
                    if (_lastRequestTime != null)
                    {
                        double latency = Math.Max(0, (@event.Timestamp - _lastRequestTime.Value).TotalMilliseconds);
                        _lastRequestTime = null;
                        // Tool call event itself doesn't generate large output, but we update latency
                        RecordUsage(_currentProvider, _currentModel, 0, 0, latency);
                    }
                    break;

                case LlmUsageEvent llmUsage:
                    RecordUsage(
                        string.IsNullOrWhiteSpace(llmUsage.Provider) ? _currentProvider : llmUsage.Provider,
                        string.IsNullOrWhiteSpace(llmUsage.Model) ? _currentModel : llmUsage.Model,
                        llmUsage.InputTokens,
                        llmUsage.OutputTokens,
                        llmUsage.LatencyMs,
                        isExplicitCall: true
                    );
                    break;
            }
        }

        private void RecordUsage(string provider, string model, int inputTokens, int outputTokens, double? latencyMs, bool isExplicitCall = false)
        {
            string key = $"{provider}:{model}";
            if (!Model.ModelMetrics.TryGetValue(key, out var metrics))
            {
                metrics = new ModelUsageMetrics
                {
                    Provider = provider,
                    Model = model,
                    LatencyEma = latencyMs ?? 1000.0
                };
                Model.ModelMetrics[key] = metrics;
            }

            if (isExplicitCall || inputTokens > 0 || outputTokens > 0 || latencyMs != null)
            {
                metrics.CallCount++;
                Model.TotalCalls++;
            }

            metrics.InputTokens += inputTokens;
            metrics.OutputTokens += outputTokens;
            Model.TotalInputTokens += inputTokens;
            Model.TotalOutputTokens += outputTokens;

            if (latencyMs.HasValue)
            {
                // Update Model EMA
                metrics.LatencyEma = (Alpha * latencyMs.Value) + (1 - Alpha) * metrics.LatencyEma;

                // Update Global EMA
                Model.LatencyEma = (Alpha * latencyMs.Value) + (1 - Alpha) * Model.LatencyEma;
            }

            // Calculate cost
            double cost = CalculateCost(provider, model, inputTokens, outputTokens);
            metrics.AccumulatedCost += cost;
            Model.TotalCost += cost;
        }

        private double CalculateCost(string provider, string model, int inputTokens, int outputTokens)
        {
            double inputPricePerMillion = 0.0;
            double outputPricePerMillion = 0.0;

            var desc = _registry.Get(provider);
            if (desc != null && desc.Metadata != null)
            {
                if (desc.Metadata.TryGetValue("InputTokenPricePerMillion", out var inPriceObj) ||
                    desc.Metadata.TryGetValue("input_token_price_per_million", out inPriceObj))
                {
                    if (inPriceObj != null)
                        double.TryParse(inPriceObj.ToString(), out inputPricePerMillion);
                }

                if (desc.Metadata.TryGetValue("OutputTokenPricePerMillion", out var outPriceObj) ||
                    desc.Metadata.TryGetValue("output_token_price_per_million", out outPriceObj))
                {
                    if (outPriceObj != null)
                        double.TryParse(outPriceObj.ToString(), out outputPricePerMillion);
                }
            }

            // If metadata values are 0, try fallback pricing
            if (inputPricePerMillion == 0.0 && outputPricePerMillion == 0.0)
            {
                string lowerProvider = provider.ToLowerInvariant();
                string lowerModel = model.ToLowerInvariant();

                if (lowerProvider == "claude")
                {
                    if (lowerModel.Contains("sonnet"))
                    {
                        inputPricePerMillion = 3.00;
                        outputPricePerMillion = 15.00;
                    }
                    else if (lowerModel.Contains("haiku"))
                    {
                        inputPricePerMillion = 0.25;
                        outputPricePerMillion = 1.25;
                    }
                    else
                    {
                        inputPricePerMillion = 3.00;
                        outputPricePerMillion = 15.00;
                    }
                }
                else if (lowerProvider == "gemini")
                {
                    if (lowerModel.Contains("pro"))
                    {
                        inputPricePerMillion = 1.25;
                        outputPricePerMillion = 5.00;
                    }
                    else if (lowerModel.Contains("flash"))
                    {
                        inputPricePerMillion = 0.075;
                        outputPricePerMillion = 0.30;
                    }
                    else
                    {
                        inputPricePerMillion = 0.075;
                        outputPricePerMillion = 0.30;
                    }
                }
            }

            return ((inputTokens * inputPricePerMillion) + (outputTokens * outputPricePerMillion)) / 1_000_000.0;
        }
    }
}
