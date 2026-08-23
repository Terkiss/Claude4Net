using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Claude4Net.Runtime.Telemetry
{
    public class ModelPricing
    {
        public double PromptCostPer1M { get; set; }
        public double CompCostPer1M { get; set; }
    }

    /// <summary>
    /// 2026 플래그십 AI 모델 토큰 단가표 및 비용 계산기
    /// </summary>
    public class PricingEngine
    {
        private readonly ConcurrentDictionary<string, ModelPricing> _pricingTable;

        public static PricingEngine Shared { get; } = new();

        public PricingEngine()
        {
            _pricingTable = new ConcurrentDictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase)
            {
                // Google Gemini 3.7 Series
                ["gemini-3.7-flash"] = new ModelPricing { PromptCostPer1M = 0.10, CompCostPer1M = 0.40 },
                ["gemini-3.7-pro"]   = new ModelPricing { PromptCostPer1M = 1.25, CompCostPer1M = 5.00 },
                ["gemini-3.1-flash-lite-preview"] = new ModelPricing { PromptCostPer1M = 0.075, CompCostPer1M = 0.30 },
                ["gemini-2.5-flash"] = new ModelPricing { PromptCostPer1M = 0.075, CompCostPer1M = 0.30 },
                ["gemini-2.5-pro"]   = new ModelPricing { PromptCostPer1M = 1.25, CompCostPer1M = 5.00 },

                // Antigravity AI Engine Lineup
                ["antigravity-pro"]         = new ModelPricing { PromptCostPer1M = 1.25, CompCostPer1M = 5.00 },
                ["antigravity-flash"]       = new ModelPricing { PromptCostPer1M = 0.10, CompCostPer1M = 0.40 },
                ["antigravity-flash-lite"]  = new ModelPricing { PromptCostPer1M = 0.075, CompCostPer1M = 0.30 },
                ["antigravity-deepcoder"]   = new ModelPricing { PromptCostPer1M = 3.00, CompCostPer1M = 15.00 },

                // Anthropic Claude 3.7 & 3.5 Series
                ["claude-3-7-sonnet"] = new ModelPricing { PromptCostPer1M = 3.00, CompCostPer1M = 15.00 },
                ["claude-3-7-opus"]   = new ModelPricing { PromptCostPer1M = 15.00, CompCostPer1M = 75.00 },
                ["claude-3-5-sonnet"] = new ModelPricing { PromptCostPer1M = 3.00, CompCostPer1M = 15.00 },
                ["claude-3-5-haiku"]  = new ModelPricing { PromptCostPer1M = 0.80, CompCostPer1M = 4.00 },
                ["claude-3-opus"]     = new ModelPricing { PromptCostPer1M = 15.00, CompCostPer1M = 75.00 },

                // DeepSeek Series
                ["deepseek-v3"] = new ModelPricing { PromptCostPer1M = 0.14, CompCostPer1M = 0.28 },
                ["deepseek-r1"] = new ModelPricing { PromptCostPer1M = 0.55, CompCostPer1M = 2.19 },

                // OpenAI Series
                ["gpt-4o"]      = new ModelPricing { PromptCostPer1M = 2.50, CompCostPer1M = 10.00 },
                ["gpt-4o-mini"] = new ModelPricing { PromptCostPer1M = 0.15, CompCostPer1M = 0.60 },
                ["o1"]          = new ModelPricing { PromptCostPer1M = 15.00, CompCostPer1M = 60.00 },
                ["o3-mini"]     = new ModelPricing { PromptCostPer1M = 1.10, CompCostPer1M = 4.40 },

                // Alibaba Token Plan & Qwen 2026 Lineup
                ["qwen3.8-max"]                 = new ModelPricing { PromptCostPer1M = 1.60, CompCostPer1M = 6.40 },
                ["qwen3.7-plus"]                = new ModelPricing { PromptCostPer1M = 0.40, CompCostPer1M = 1.20 },
                ["qwen3.7-max"]                 = new ModelPricing { PromptCostPer1M = 1.20, CompCostPer1M = 4.80 },
                ["qwen3.6-flash"]               = new ModelPricing { PromptCostPer1M = 0.05, CompCostPer1M = 0.20 },
                ["deepseek-v4-pro-0813"]        = new ModelPricing { PromptCostPer1M = 0.55, CompCostPer1M = 2.19 },
                ["deepseek-v4-pro"]             = new ModelPricing { PromptCostPer1M = 0.55, CompCostPer1M = 2.19 },
                ["deepseek-v4-flash-0731"]      = new ModelPricing { PromptCostPer1M = 0.08, CompCostPer1M = 0.32 },
                ["glm-5.2"]                     = new ModelPricing { PromptCostPer1M = 0.60, CompCostPer1M = 2.40 },
                ["qwen-2.5-coder-32b-instruct"] = new ModelPricing { PromptCostPer1M = 0.40, CompCostPer1M = 1.20 },
                ["qwen-2.5-coder-14b-instruct"] = new ModelPricing { PromptCostPer1M = 0.10, CompCostPer1M = 0.30 },
                ["qwen-2.5-coder-7b-instruct"]  = new ModelPricing { PromptCostPer1M = 0.05, CompCostPer1M = 0.15 },
                ["qwen-coder-plus"]             = new ModelPricing { PromptCostPer1M = 0.40, CompCostPer1M = 1.20 },
                ["qwen-coder-turbo"]            = new ModelPricing { PromptCostPer1M = 0.05, CompCostPer1M = 0.15 },
                ["qwen-max"]                    = new ModelPricing { PromptCostPer1M = 2.40, CompCostPer1M = 9.60 },
                ["qwen-plus"]                   = new ModelPricing { PromptCostPer1M = 0.40, CompCostPer1M = 1.20 },
                ["qwen-turbo"]                  = new ModelPricing { PromptCostPer1M = 0.05, CompCostPer1M = 0.15 },
                ["qwen2.5-72b-instruct"]        = new ModelPricing { PromptCostPer1M = 0.80, CompCostPer1M = 2.40 }
            };
        }

        public void RegisterModelPricing(string model, double promptCostPer1M, double compCostPer1M)
        {
            _pricingTable[model] = new ModelPricing
            {
                PromptCostPer1M = promptCostPer1M,
                CompCostPer1M = compCostPer1M
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double CalculateCost(string? model, int promptTokens, int compTokens)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return 0.0;
            }

            // Clean model string (e.g. remove provider prefix like "gemini/")
            string cleanModel = model;
            int slashIdx = cleanModel.IndexOf('/');
            if (slashIdx >= 0 && slashIdx < cleanModel.Length - 1)
            {
                cleanModel = cleanModel.Substring(slashIdx + 1);
            }

            if (!_pricingTable.TryGetValue(cleanModel, out var pricing))
            {
                // Fallback default pricing
                pricing = new ModelPricing { PromptCostPer1M = 1.00, CompCostPer1M = 3.00 };
            }

            double promptCost = (promptTokens / 1_000_000.0) * pricing.PromptCostPer1M;
            double compCost = (compTokens / 1_000_000.0) * pricing.CompCostPer1M;

            return Math.Round(promptCost + compCost, 6);
        }
    }
}
