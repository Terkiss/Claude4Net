using System;
using System.Linq;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;

namespace Claude4Net.Tests
{
    public class D05SmartRouterTests
    {
        [Fact]
        public void SmartRouter_ShouldUpdateLatencyEMA()
        {
            var router = new SmartRouter();
            
            // Initial EMA is 1000
            // Alpha is 0.3
            
            router.UpdateMetric("gemini", 100, false);
            // 1st update: 100 * 0.3 + 1000 * 0.7 = 30 + 700 = 730
            
            router.UpdateMetric("gemini", 200, false);
            // 2nd update: 200 * 0.3 + 730 * 0.7 = 60 + 511 = 571
            
            var metrics = router.GetMetrics().First(m => m.ProviderName == "gemini");
            Assert.Equal(571, metrics.LatencyEma, 1);
        }

        [Fact]
        public void SmartRouter_ShouldCircuitBreakAfterFailures()
        {
            var router = new SmartRouter();
            // Threshold is 5
            for (int i = 0; i < 5; i++)
            {
                router.UpdateMetric("claude", 500, true);
            }
            
            var metrics = router.GetMetrics().First(m => m.ProviderName == "claude");
            Assert.Equal(ProviderHealthStatus.CircuitBroken, metrics.Status);
            
            var decision = router.Route("test request");
            // Should not select claude because it's circuit-broken
            Assert.NotEqual("claude", decision.SelectedProvider);
        }

        [Fact]
        public void SmartRouter_ShouldRespectIntent()
        {
            var router = new SmartRouter();
            
            // Large model intent should favor Claude
            var decision = router.Route("Refactor this complex logic", RoutingIntent.LargeModel);
            Assert.Equal("claude", decision.SelectedProvider);
            
            // Small model intent should favor Gemini
            var decision2 = router.Route("Hi", RoutingIntent.SmallModel);
            Assert.Equal("gemini", decision2.SelectedProvider);
            
            // LocalOnly should choose a local provider (ollama or gemini-cli)
            var decision3 = router.Route("Sensitive data processing", RoutingIntent.LocalOnly);
            Assert.True(decision3.SelectedProvider == "ollama" || decision3.SelectedProvider == "gemini-cli");
        }

        [Fact]
        public void SmartRouter_ShouldFallbackWhenPreferredIsBroken()
        {
            var router = new SmartRouter();
            
            // Break Claude
            for (int i = 0; i < 5; i++) router.UpdateMetric("claude", 500, true);
            
            // Intent is LargeModel (normally Claude), but it's broken
            var decision = router.Route("Complex task", RoutingIntent.LargeModel);
            
            Assert.NotEqual("claude", decision.SelectedProvider);
            Assert.Equal("gemini", decision.SelectedProvider); // Gemini is the next best for LargeModel
        }

        [Fact]
        public void SmartRouter_ShouldTransitionToHalfOpenAfterBackoff()
        {
            var router = new SmartRouter();
            // 1. Break Claude
            for (int i = 0; i < 5; i++) router.UpdateMetric("claude", 500, true);
            
            var metrics = router.GetMetrics().First(m => m.ProviderName == "claude");
            Assert.Equal(ProviderHealthStatus.CircuitBroken, metrics.Status);
            Assert.NotNull(metrics.CircuitBreakerResetTime);

            // 2. Mock time passage (In a real app we'd use a clock abstraction, but here we can check if Route handles it)
            // For testing purposes, we can manually set the reset time to the past
            metrics.CircuitBreakerResetTime = DateTime.UtcNow.AddMinutes(-1);
            
            // 3. Route call should trigger Half-Open transition
            var decision = router.Route("Test");
            Assert.Equal(ProviderHealthStatus.CircuitBreakerHalfOpen, metrics.Status);
            
            // 4. Successful call in Half-Open should recover to Healthy
            router.UpdateMetric("claude", 200, false);
            Assert.Equal(ProviderHealthStatus.Healthy, metrics.Status);
            Assert.Equal(0, metrics.ErrorCount);
        }

        [Fact]
        public void SmartRouter_ShouldApplyCostPenalty()
        {
            var router = new SmartRouter();
            
            // 1. Initial state: Gemini is favored for SmallModel due to lower cost (0.4 vs 0.8)
            var decision1 = router.Route("Small task", RoutingIntent.SmallModel);
            Assert.Equal("gemini", decision1.SelectedProvider);

            // 2. Simulate usage of Gemini to increase its accumulated cost
            // Accumulated cost penalty is 0.5 * AccumulatedCost
            for (int i = 0; i < 50; i++)
            {
                router.UpdateMetric("gemini", 1000, false); // Add cost
            }
            
            // 3. Now Gemini should have significant penalty.
            // Check if it's no longer the top choice or at least its score dropped.
            var decision2 = router.Route("Small task", RoutingIntent.SmallModel);
            Assert.NotEqual("gemini", decision2.SelectedProvider);
            Assert.True(decision2.SelectedProvider == "claude" || decision2.SelectedProvider == "ollama");
        }

        [Fact]
        public void SmartRouter_ShouldTrackAccumulatedCost()
        {
            var router = new SmartRouter();
            router.UpdateMetric("claude", 1000, false); // 1s latency
            
            var metrics = router.GetMetrics().First(m => m.ProviderName == "claude");
            // Cost = CostScore(0.8) * Latency(1.0s) = 0.8
            Assert.Equal(0.8, metrics.AccumulatedCost, 2);
            
            router.UpdateMetric("claude", 500, false); // 0.5s latency
            // New cost = 0.8 + (0.8 * 0.5) = 1.2
            Assert.Equal(1.2, metrics.AccumulatedCost, 2);
        }

        [Fact]
        public void LocalModel_ShouldNotBeDePrioritized_DueToHighLatency()
        {
            var router = new SmartRouter();
            
            // 1. Record extremely high latency for Ollama (20 seconds)
            router.UpdateMetric("ollama", 20000, false);
            
            // 2. Record low latency for Gemini (100 ms)
            router.UpdateMetric("gemini", 100, false);
            
            // 3. Even with 20s latency, Ollama should still be preferred over Gemini 
            // due to the +500 local bonus and latency penalty exemption.
            var decision = router.Route("Any task");
            
            Assert.Equal("ollama", decision.SelectedProvider);
            Assert.Contains("Health: Healthy", decision.Reason);
        }

        [Fact]
        public void SmartRouter_ShouldRouteToExplicitProvider_EvenForSmallModel()
        {
            var router = new SmartRouter();
            var originalProvider = AppState.ActiveProvider;
            var originalIsSet = AppState.IsProviderExplicitlySet;
            
            try
            {
                // Set AppState.ActiveProvider to simulate !login geminicli
                AppState.ActiveProvider = "gemini-cli";
                AppState.IsProviderExplicitlySet = true;

                // Normal SmallModel intent would favor ollama, but ActiveProvider boost should override it
                var decision = router.Route("안녕"); // < 100 chars, so SmallModel

                Assert.Equal("gemini-cli", decision.SelectedProvider);
            }
            finally
            {
                // Reset for other tests
                AppState.ActiveProvider = originalProvider;
                AppState.IsProviderExplicitlySet = originalIsSet;
            }
        }

        [Fact]
        public void SmartRouter_ShouldIncludeGeminiCliInMetrics()
        {
            var router = new SmartRouter();
            var metrics = router.GetMetrics();
            
            Assert.Contains(metrics, m => m.ProviderName == "gemini-cli");
        }
    }
}
