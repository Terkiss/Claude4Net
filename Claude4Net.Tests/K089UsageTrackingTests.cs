using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    public class K089UsageTrackingTests
    {
        [Fact]
        public void UsageProjection_CalculatesCorrectCostAndTokens_FromStandardEvents()
        {
            var registry = new ProviderRegistry();
            var projection = new UsageProjection(registry);

            var now = DateTime.UtcNow;
            var events = new List<IAgentEvent>
            {
                new SessionStartedEvent { Provider = "claude", Model = "claude-3-5-sonnet-20241022", Timestamp = now },
                new UserPromptReceivedEvent { Prompt = "12345678", Timestamp = now.AddSeconds(1) }, // 8 chars -> 2 input tokens
                new AgentThoughtEvent { Thought = "123456789012", Timestamp = now.AddSeconds(3) } // 12 chars -> 3 output tokens, latency = 2000ms
            };

            foreach (var @event in events)
            {
                projection.Apply(@event);
            }

            var model = projection.Model;
            Assert.Equal(2, model.TotalInputTokens);
            Assert.Equal(3, model.TotalOutputTokens);
            Assert.Equal(2, model.TotalCalls); // 1 prompt, 1 thought with latency

            // Sonnet fallback price: input: 3.00, output: 15.00
            // cost = (2 * 3.00 + 3 * 15.00) / 1,000,000 = (6.00 + 45.00) / 1,000,000 = 0.000051
            Assert.Equal(0.000051, model.TotalCost, 6);

            // Latency EMA: initial 1000, new 2000 -> 0.3 * 2000 + 0.7 * 1000 = 600 + 700 = 1300
            Assert.Equal(1300.0, model.LatencyEma, 1);
        }

        [Fact]
        public void UsageProjection_AppliesCustomMetadataPricing()
        {
            var registry = new ProviderRegistry();
            var customProvider = new ProviderDescriptor
            {
                Id = "custom-provider",
                Label = "Custom Provider",
                TransportKind = "openai-compat",
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "custom-model",
                    Large = "custom-model"
                },
                Metadata = new Dictionary<string, object?>
                {
                    { "InputTokenPricePerMillion", 10.0 },
                    { "OutputTokenPricePerMillion", 50.0 }
                }
            };
            registry.Register(customProvider);

            var projection = new UsageProjection(registry);
            var now = DateTime.UtcNow;

            var events = new List<IAgentEvent>
            {
                new SessionStartedEvent { Provider = "custom-provider", Model = "custom-model", Timestamp = now },
                new LlmUsageEvent
                {
                    Provider = "custom-provider",
                    Model = "custom-model",
                    InputTokens = 100_000,
                    OutputTokens = 50_000,
                    LatencyMs = 500,
                    Timestamp = now.AddSeconds(1)
                }
            };

            foreach (var @event in events)
            {
                projection.Apply(@event);
            }

            var model = projection.Model;
            // Cost calculation:
            // Input cost: 100,000 * 10.0 / 1,000,000 = 1.00 USD
            // Output cost: 50,000 * 50.0 / 1,000,000 = 2.50 USD
            // Total cost: 3.50 USD
            Assert.Equal(3.50, model.TotalCost, 2);
            Assert.Equal(100000, model.TotalInputTokens);
            Assert.Equal(50000, model.TotalOutputTokens);
            Assert.Equal(1, model.TotalCalls);
            // Latency EMA: 0.3 * 500 + 0.7 * 1000 = 150 + 700 = 850
            Assert.Equal(850.0, model.LatencyEma, 1);
        }

        [Fact]
        public void UsageProjection_FallbackPrices_ApplyToGemini()
        {
            var registry = new ProviderRegistry();
            var projection = new UsageProjection(registry);

            var now = DateTime.UtcNow;
            var events = new List<IAgentEvent>
            {
                new SessionStartedEvent { Provider = "gemini", Model = "gemini-1.5-pro", Timestamp = now },
                new LlmUsageEvent
                {
                    InputTokens = 1_000_000,
                    OutputTokens = 1_000_000,
                    LatencyMs = 1200,
                    Timestamp = now.AddSeconds(1)
                }
            };

            foreach (var @event in events)
            {
                projection.Apply(@event);
            }

            var model = projection.Model;
            // Gemini Pro fallback price: input: 1.25, output: 5.00
            // total cost = 1.25 + 5.00 = 6.25
            Assert.Equal(6.25, model.TotalCost, 2);
        }

        [Fact]
        public void UsageProjection_CalculatesCorrectLatencyEmaConvergence()
        {
            var registry = new ProviderRegistry();
            var projection = new UsageProjection(registry);
            var now = DateTime.UtcNow;

            // Apply 4 events with 500ms latency each
            // Init: 1000
            // 1st: 0.3*500 + 0.7*1000 = 850
            // 2nd: 0.3*500 + 0.7*850 = 745
            // 3rd: 0.3*500 + 0.7*745 = 671.5
            // 4th: 0.3*500 + 0.7*671.5 = 620.05
            var events = new List<IAgentEvent>
            {
                new SessionStartedEvent { Provider = "claude", Model = "claude-3-haiku", Timestamp = now },
                new LlmUsageEvent { LatencyMs = 500, Timestamp = now.AddSeconds(1) },
                new LlmUsageEvent { LatencyMs = 500, Timestamp = now.AddSeconds(2) },
                new LlmUsageEvent { LatencyMs = 500, Timestamp = now.AddSeconds(3) },
                new LlmUsageEvent { LatencyMs = 500, Timestamp = now.AddSeconds(4) }
            };

            foreach (var @event in events)
            {
                projection.Apply(@event);
            }

            Assert.Equal(620.05, projection.Model.LatencyEma, 2);
        }
    }
}
