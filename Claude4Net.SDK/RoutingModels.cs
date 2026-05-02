using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    public enum ProviderHealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy,
        CircuitBroken,
        CircuitBreakerHalfOpen
    }

    public class ProviderMetric
    {
        public string ProviderName { get; set; } = string.Empty;
        public double LatencyEma { get; set; } // Exponential Moving Average of latency in ms
        public ProviderHealthStatus Status { get; set; } = ProviderHealthStatus.Healthy;
        public int ErrorCount { get; set; }
        public int SuccessCount { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public double CostScore { get; set; } // 0.0 (Cheap) to 1.0 (Expensive)
        public double AccumulatedCost { get; set; } // Track cumulative usage cost
        public DateTime? CircuitBreakerResetTime { get; set; } // For exponential backoff
    }

    public enum RoutingIntent
    {
        Auto,
        LargeModel, // Complex reasoning (e.g., Claude 3.5 Sonnet, Gemini 1.5 Pro)
        SmallModel, // Fast, simple tasks (e.g., Gemini 1.5 Flash, Llama 3 8B)
        LocalOnly,  // Ollama
        CostEffective
    }

    public class RoutingDecision
    {
        public string SelectedProvider { get; set; } = string.Empty;
        public string SelectedModel { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public List<string> FallbackChain { get; set; } = new();
    }

    public interface ISmartRouter
    {
        RoutingDecision Route(string prompt, RoutingIntent intent = RoutingIntent.Auto);
        void UpdateMetric(string provider, double latencyMs, bool isError);
        IEnumerable<ProviderMetric> GetMetrics();
    }
}
