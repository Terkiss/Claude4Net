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
            
            // LocalOnly should always choose Ollama
            var decision3 = router.Route("Sensitive data processing", RoutingIntent.LocalOnly);
            Assert.Equal("ollama", decision3.SelectedProvider);
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
        public void SmartRouter_ShouldFallbackToOllamaWhenAllRemoteBroken()
        {
            var router = new SmartRouter();
            
            // Break all including ollama
            for (int i = 0; i < 5; i++)
            {
                router.UpdateMetric("claude", 500, true);
                router.UpdateMetric("gemini", 500, true);
                router.UpdateMetric("ollama", 500, true);
            }
            
            var decision = router.Route("Any task");
            Assert.Equal("ollama", decision.SelectedProvider);
            Assert.Contains("Falling back to local Ollama", decision.Reason);
        }
    }
}
