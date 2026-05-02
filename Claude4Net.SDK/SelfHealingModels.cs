using System;

namespace Claude4Net.SDK
{
    public enum ErrorCategory
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
        QuotaError
    }

    public enum RetryStrategy
    {
        None,
        Immediate,
        FixedInterval,
        ExponentialBackoff,
        CircuitBreaker
    }

    public class RetryPolicy
    {
        public RetryStrategy Strategy { get; set; } = RetryStrategy.None;
        public int MaxRetries { get; set; } = 3;
        public int InitialDelayMs { get; set; } = 1000;
        public string? Condition { get; set; } // Regex or simple tag
    }

    public class SelfHealingDiagnosis
    {
        public string ToolName { get; set; } = string.Empty;
        public ErrorCategory Category { get; set; }
        public int FailureCount { get; set; }
        public string CommonErrorMessage { get; set; } = string.Empty;
        public string Suggestion { get; set; } = string.Empty;
        public RetryPolicy? RecommendedRetry { get; set; }
    }

    public static class ErrorClassifier
    {
        public static ErrorCategory Classify(string toolName, string error)
        {
            if (string.IsNullOrEmpty(error)) return ErrorCategory.Unknown;

            var lower = error.ToLowerInvariant();
            
            // 1. Quota & Limits
            if (RegexMatch(lower, "quota|limit|exhausted|429"))
                return ErrorCategory.QuotaError;

            // 2. Network & Connectivity
            if (RegexMatch(lower, "network|connection.*reset|dns|endpoint|socket|refused|unreachable"))
                return ErrorCategory.NetworkError;

            // 3. Timeouts
            if (RegexMatch(lower, "timeout|timed out|deadline|exceeded.*time"))
                return ErrorCategory.TimeoutError;

            // 4. Permissions
            if (RegexMatch(lower, "access denied|permission|unauthorized|forbidden|403|401"))
                return ErrorCategory.PermissionError;
            
            // 5. Paths
            if (RegexMatch(lower, "not found|find part of the path|directorynotfound|no such file"))
                return ErrorCategory.PathError;
            
            // 6. Build
            if (RegexMatch(lower, "build failed|compile error|cs[0-9]{4}|msbuild|csc.exe|dotnet build"))
                return ErrorCategory.BuildError;
            
            // 7. Tests
            if (RegexMatch(lower, "test failed|assertionfailed|xunit|nunit|failed.*tests"))
                return ErrorCategory.TestError;
            
            // 8. Provider Specific
            if (RegexMatch(lower, "api key|provider error|anthropic-version|model.*not.*found"))
                return ErrorCategory.ProviderError;
            
            // 9. MCP
            if (RegexMatch(lower, "mcp|jsonrpc|stdio-transport|server.*disconnected"))
                return ErrorCategory.McpError;

            // 10. Logic
            if (RegexMatch(lower, "invalid argument|schema mismatch|not supported|unexpected format"))
                return ErrorCategory.LogicError;

            return ErrorCategory.ToolFailure;
        }

        private static bool RegexMatch(string input, string pattern)
        {
            try { return System.Text.RegularExpressions.Regex.IsMatch(input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
            catch { return input.Contains(pattern.Split('|')[0]); } // Fallback
        }

        public static RetryPolicy GetRecommendedPolicy(ErrorCategory category)
        {
            return category switch
            {
                ErrorCategory.QuotaError => new RetryPolicy { Strategy = RetryStrategy.ExponentialBackoff, MaxRetries = 5, InitialDelayMs = 5000 },
                ErrorCategory.NetworkError => new RetryPolicy { Strategy = RetryStrategy.ExponentialBackoff, MaxRetries = 3, InitialDelayMs = 2000 },
                ErrorCategory.TimeoutError => new RetryPolicy { Strategy = RetryStrategy.FixedInterval, MaxRetries = 2, InitialDelayMs = 3000 },
                ErrorCategory.ProviderError => new RetryPolicy { Strategy = RetryStrategy.FixedInterval, MaxRetries = 3, InitialDelayMs = 1000 },
                ErrorCategory.ToolFailure => new RetryPolicy { Strategy = RetryStrategy.Immediate, MaxRetries = 1 },
                _ => new RetryPolicy { Strategy = RetryStrategy.None }
            };
        }
    }
}
