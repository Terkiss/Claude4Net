using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class SmartRouter : ISmartRouter
    {
        private readonly ConcurrentDictionary<string, ProviderMetric> _metrics = new();
        private const double Alpha = 0.3; // EMA smoothing factor
        private const int CircuitBreakerThreshold = 5;
        private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);

        public SmartRouter()
        {
            // Initialize known providers with default cost scores
            InitializeProvider("claude", 0.8);
            InitializeProvider("gemini", 0.4);
            InitializeProvider("ollama", 0.1);
        }

        private void InitializeProvider(string name, double costScore)
        {
            _metrics[name] = new ProviderMetric
            {
                ProviderName = name,
                LatencyEma = 1000, // Initial guess 1s
                Status = ProviderHealthStatus.Healthy,
                CostScore = costScore,
                AccumulatedCost = 0,
                LastUpdated = DateTime.UtcNow
            };
        }

        public RoutingDecision Route(string prompt, RoutingIntent intent = RoutingIntent.Auto)
        {
            var now = DateTime.UtcNow;
            var candidates = _metrics.Values.ToList();

            // Check for Half-Open transition
            foreach (var m in candidates.Where(m => m.Status == ProviderHealthStatus.CircuitBroken))
            {
                if (m.CircuitBreakerResetTime.HasValue && now >= m.CircuitBreakerResetTime.Value)
                {
                    m.Status = ProviderHealthStatus.CircuitBreakerHalfOpen;
                }
            }

            var healthyProviders = candidates
                .Where(m => m.Status != ProviderHealthStatus.CircuitBroken && m.Status != ProviderHealthStatus.Unhealthy)
                .ToList();

            if (!healthyProviders.Any())
            {
                return new RoutingDecision
                {
                    SelectedProvider = "ollama", // Final safety fallback
                    SelectedModel = "llama3",
                    Reason = "All remote providers are unhealthy or circuit-broken. Falling back to local Ollama."
                };
            }

            // Simple heuristic to detect intent from prompt if Auto
            if (intent == RoutingIntent.Auto)
            {
                if (prompt.Length > 1000 || prompt.Contains("complex") || prompt.Contains("refactor"))
                    intent = RoutingIntent.LargeModel;
                else if (prompt.Length < 100)
                    intent = RoutingIntent.SmallModel;
            }

            // Scoring logic
            var scored = healthyProviders.Select(m => new
            {
                Metric = m,
                Score = CalculateScore(m, intent, prompt)
            }).OrderByDescending(x => x.Score).ToList();

            var top = scored.First();

            return new RoutingDecision
            {
                SelectedProvider = top.Metric.ProviderName,
                SelectedModel = DefaultModelFor(top.Metric.ProviderName, intent),
                Reason = $"Selected {top.Metric.ProviderName} for {intent} intent (Score: {top.Score:F2}, Latency: {top.Metric.LatencyEma:F0}ms, Health: {top.Metric.Status})",
                FallbackChain = scored.Skip(1).Select(x => x.Metric.ProviderName).ToList()
            };
        }

        private double CalculateScore(ProviderMetric m, RoutingIntent intent, string prompt)
        {
            double score = 100.0;

            // 1. Latency Penalty (Normalize: 100ms = -1 point)
            score -= (m.LatencyEma / 100.0);

            // 2. Base Cost Weight
            double costWeight = (intent == RoutingIntent.CostEffective) ? 50.0 : 10.0;
            score -= (m.CostScore * costWeight);

            // 3. Accumulated Cost Penalty (Cost-Aware Routing)
            // Penalty increases as accumulated cost grows to balance load/budget
            score -= (m.AccumulatedCost * 0.5);

            // 4. Intent Alignment
            switch (intent)
            {
                case RoutingIntent.LargeModel:
                    if (m.ProviderName == "claude") score += 30.0;
                    if (m.ProviderName == "gemini") score += 10.0;
                    break;
                case RoutingIntent.SmallModel:
                    if (m.ProviderName == "gemini") score += 30.0;
                    if (m.ProviderName == "ollama") score += 20.0;
                    break;
                case RoutingIntent.LocalOnly:
                    if (m.ProviderName == "ollama") score += 1000.0;
                    break;
            }

            // 5. Health status penalty
            if (m.Status == ProviderHealthStatus.Degraded) score -= 40.0;
            if (m.Status == ProviderHealthStatus.CircuitBreakerHalfOpen) score -= 60.0; // Probe carefully

            return score;
        }

        private string DefaultModelFor(string provider, RoutingIntent intent)
        {
            return provider switch
            {
                "claude" => (intent == RoutingIntent.LargeModel) ? "claude-3-5-sonnet-20240620" : "claude-3-haiku-20240307",
                "gemini" => (intent == RoutingIntent.LargeModel) ? "gemini-1.5-pro" : "gemini-1.5-flash",
                "ollama" => "llama3",
                _ => AppState.ActiveModel
            };
        }

        public void UpdateMetric(string provider, double latencyMs, bool isError)
        {
            _metrics.AddOrUpdate(provider, 
                _ => new ProviderMetric { 
                    ProviderName = provider, 
                    LatencyEma = latencyMs, 
                    Status = isError ? ProviderHealthStatus.Degraded : ProviderHealthStatus.Healthy,
                    AccumulatedCost = isError ? 0 : (latencyMs / 1000.0), // Simplified cost estimate
                    LastUpdated = DateTime.UtcNow
                },
                (name, old) =>
                {
                    // EMA: NewValue * Alpha + OldValue * (1 - Alpha)
                    old.LatencyEma = (Alpha * latencyMs) + (1 - Alpha) * old.LatencyEma;
                    
                    if (isError)
                    {
                        old.ErrorCount++;
                        old.SuccessCount = 0;
                        if (old.ErrorCount >= CircuitBreakerThreshold)
                        {
                            old.Status = ProviderHealthStatus.CircuitBroken;
                            // Exponential backoff: Base * 2^(errorCount - threshold)
                            int backoffFactor = Math.Min(old.ErrorCount - CircuitBreakerThreshold, 6);
                            old.CircuitBreakerResetTime = DateTime.UtcNow.Add(BaseBackoff.Multiply(Math.Pow(2, backoffFactor)));
                        }
                        else
                        {
                            old.Status = ProviderHealthStatus.Degraded;
                        }
                    }
                    else
                    {
                        old.SuccessCount++;
                        // Successful call on Half-Open or Healthy
                        if (old.Status == ProviderHealthStatus.CircuitBreakerHalfOpen || old.SuccessCount >= 3)
                        {
                            old.ErrorCount = 0;
                            old.Status = ProviderHealthStatus.Healthy;
                            old.CircuitBreakerResetTime = null;
                        }

                        // Add to accumulated cost (mock logic: cost proportional to latency and base score)
                        old.AccumulatedCost += (old.CostScore * (latencyMs / 1000.0));
                    }
                    old.LastUpdated = DateTime.UtcNow;
                    return old;
                });
        }

        public IEnumerable<ProviderMetric> GetMetrics() => _metrics.Values;
    }
}
