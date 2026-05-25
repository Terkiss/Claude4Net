using System;
using System.Text.RegularExpressions;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 세분화된 에러 카테고리 정의
    /// </summary>
    public enum RefinedErrorCategory
    {
        Unknown,
        PathError,
        BuildError,
        TestError,
        PermissionError,
        ProviderError,
        ToolFailure,
        McpError,
        NetworkError,
        TimeoutError,
        LogicError,
        QuotaError,
        InfiniteLoop,
        Hallucination,
        
        // Refined Categories
        JsonSchemaMismatch,
        RateLimit,
        ContextLimitOver,
        SymlinkEscapeViolation
    }

    /// <summary>
    /// 에러 분류 확장기
    /// </summary>
    public static class ErrorClassifier
    {
        /// <summary>
        /// 오류 문자열을 분석하여 RefinedErrorCategory를 반환합니다.
        /// </summary>
        public static RefinedErrorCategory Classify(string toolName, string error)
        {
            if (string.IsNullOrEmpty(error)) return RefinedErrorCategory.Unknown;

            var lower = error.ToLowerInvariant();

            // 1. Symlink 탈출 위반
            if (RegexMatch(lower, @"symlink|symbolic link|escape.*path|outside.*workspace|directory.*traversal|path.*safety|unsafe.*path"))
            {
                return RefinedErrorCategory.SymlinkEscapeViolation;
            }

            // 2. Context Limit Over
            if (RegexMatch(lower, @"context.*limit|max.*tokens|token.*limit|context.*length|context.*window"))
            {
                return RefinedErrorCategory.ContextLimitOver;
            }

            // 3. Rate Limit
            if (RegexMatch(lower, @"rate.*limit|too.*many.*requests|429|quota.*exhausted.*rate"))
            {
                return RefinedErrorCategory.RateLimit;
            }

            // 4. JSON 스키마 미스매치
            if (RegexMatch(lower, @"schema.*mismatch|invalid.*arguments|unexpected.*format|json.*schema|not.*conforming.*schema"))
            {
                return RefinedErrorCategory.JsonSchemaMismatch;
            }

            // Standard fallback
            var standardCat = Claude4Net.SDK.ErrorClassifier.Classify(toolName, error);
            return MapToRefined(standardCat);
        }

        private static bool RegexMatch(string input, string pattern)
        {
            try 
            { 
                return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase); 
            }
            catch 
            { 
                return input.Contains(pattern.Split('|')[0]); 
            }
        }

        private static RefinedErrorCategory MapToRefined(ErrorCategory cat)
        {
            return cat switch
            {
                ErrorCategory.PathError => RefinedErrorCategory.PathError,
                ErrorCategory.BuildError => RefinedErrorCategory.BuildError,
                ErrorCategory.TestError => RefinedErrorCategory.TestError,
                ErrorCategory.PermissionError => RefinedErrorCategory.PermissionError,
                ErrorCategory.ProviderError => RefinedErrorCategory.ProviderError,
                ErrorCategory.ToolFailure => RefinedErrorCategory.ToolFailure,
                ErrorCategory.McpError => RefinedErrorCategory.McpError,
                ErrorCategory.NetworkError => RefinedErrorCategory.NetworkError,
                ErrorCategory.TimeoutError => RefinedErrorCategory.TimeoutError,
                ErrorCategory.LogicError => RefinedErrorCategory.LogicError,
                ErrorCategory.QuotaError => RefinedErrorCategory.QuotaError,
                ErrorCategory.InfiniteLoop => RefinedErrorCategory.InfiniteLoop,
                ErrorCategory.Hallucination => RefinedErrorCategory.Hallucination,
                _ => RefinedErrorCategory.Unknown
            };
        }

        /// <summary>
        /// SDK ErrorCategory와의 하위 호환성을 지원하는 GetRecommendedPolicy 메서드
        /// </summary>
        public static RetryPolicy GetRecommendedPolicy(ErrorCategory category)
        {
            return Claude4Net.SDK.ErrorClassifier.GetRecommendedPolicy(category);
        }

        /// <summary>
        /// RefinedErrorCategory에 맞는 재시도 정책 반환
        /// </summary>
        public static RetryPolicy GetRecommendedPolicy(RefinedErrorCategory category)
        {
            return category switch
            {
                RefinedErrorCategory.RateLimit => new RetryPolicy { Strategy = RetryStrategy.ExponentialBackoff, MaxRetries = 5, InitialDelayMs = 3000 },
                RefinedErrorCategory.JsonSchemaMismatch => new RetryPolicy { Strategy = RetryStrategy.Immediate, MaxRetries = 2 },
                RefinedErrorCategory.ContextLimitOver => new RetryPolicy { Strategy = RetryStrategy.None },
                RefinedErrorCategory.SymlinkEscapeViolation => new RetryPolicy { Strategy = RetryStrategy.None },
                _ => Enum.TryParse<ErrorCategory>(category.ToString(), out var sdkCat)
                     ? Claude4Net.SDK.ErrorClassifier.GetRecommendedPolicy(sdkCat)
                     : new RetryPolicy { Strategy = RetryStrategy.None }
            };
        }
    }
}
