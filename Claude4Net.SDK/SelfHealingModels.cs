using System;

namespace Claude4Net.SDK
{
    /// <summary>
    /// Defines the categories of errors that can occur.
    /// </summary>
    public enum ErrorCategory
    {
        /// <summary> ?????�는 ?�류 </summary>
        Unknown,
        /// <summary> 경로 ?�는 ?�일 찾기 ?�류 </summary>
        PathError,
        /// <summary> 컴파???�는 빌드 ?�류 </summary>
        BuildError,
        /// <summary> ?�위 ?�스???�패 </summary>
        TestError,
        /// <summary> 권한 부�??�류 </summary>
        PermissionError,
        /// <summary> LLM ?�공??관???�류 (API ???? </summary>
        ProviderError,
        /// <summary> ?�반?�인 ?�구 ?�행 ?�패 </summary>
        ToolFailure,
        /// <summary> MCP ?�로?�콜 ?�신 ?�류 </summary>
        McpError,
        /// <summary> ?�트?�크 ?�결 ?�류 </summary>
        NetworkError,
        /// <summary> ?�간 초과 </summary>
        TimeoutError,
        /// <summary> ?�자�??�류 ??로직 결함 </summary>
        LogicError,
        /// <summary> ?�당??Quota) 초과 ?�류 </summary>
        QuotaError,
        /// <summary> 무한 루프 감�? </summary>
        InfiniteLoop,
        /// <summary> ?�각(Hallucination) 감�? </summary>
        Hallucination
    }

    /// <summary>
    /// ?�이?�트 ?�패 ?�턴???�의?�니??
    /// </summary>
    public enum FailurePattern
    {
        None,
        /// <summary> ?�일???�구/?�자�?반복 ?�출?�는 무한 루프 </summary>
        InfiniteLoop,
        /// <summary> 존재?��? ?�는 ?�일/?�수�??�용?�는 ?�각 </summary>
        Hallucination,
        /// <summary> ?�구 ?�키마�? 준?�하지 ?�는 문법 ?�류 </summary>
        ToolUsageError,
        /// <summary> 보안 ?�책 ?�반?�로 ?�한 반복 거�? </summary>
        SecurityRejection,
        /// <summary> ?�결?��? ?�는 종속???�류 </summary>
        DependencyHell
    }

    /// <summary>
    /// ?�이?�트 교정???�한 치유 지�??�래?�입?�다.
    /// </summary>
    public class HealingDirective
    {
        public FailurePattern Pattern { get; set; }
        public string Instruction { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int RelevanceScore { get; set; } = 100;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// ?�시???�략???�의?�는 ?�거?�입?�다.
    /// </summary>
    public enum RetryStrategy
    {
        /// <summary> ?�시??????</summary>
        None,
        /// <summary> 즉시 ?�시??</summary>
        Immediate,
        /// <summary> 고정 간격 ?�시??</summary>
        FixedInterval,
        /// <summary> 지??백오??(?�진?�으�?간격 증�?) </summary>
        ExponentialBackoff,
        /// <summary> ?�킷 브레?�커 ?�동 </summary>
        CircuitBreaker
    }

    /// <summary>
    /// ?�시???�책???�정?�는 ?�래?�입?�다.
    /// </summary>
    public class RetryPolicy
    {
        /// <summary> ?�시???�략 </summary>
        public RetryStrategy Strategy { get; set; } = RetryStrategy.None;
        /// <summary> 최�? ?�시???�수 </summary>
        public int MaxRetries { get; set; } = 3;
        /// <summary> 초기 지???�간 (ms) </summary>
        public int InitialDelayMs { get; set; } = 1000;
        /// <summary> ?�시??조건 (?�규???�는 ?�그) </summary>
        public string? Condition { get; set; }
    }

    /// <summary>
    /// ?��? 치유(Self-Healing)�??�한 진단 결과�??�는 ?�래?�입?�다.
    /// </summary>
    public class SelfHealingDiagnosis
    {
        /// <summary> ?�???�구 ?�름 </summary>
        public string ToolName { get; set; } = string.Empty;
        /// <summary> 진단???�류 범주 </summary>
        public ErrorCategory Category { get; set; }
        /// <summary> ?�패 ?�수 </summary>
        public int FailureCount { get; set; }
        /// <summary> 공통?�으�??��??�는 ?�류 메시지 ?�약 </summary>
        public string CommonErrorMessage { get; set; } = string.Empty;
        /// <summary> ?�결???�한 ?�안 ?�용 </summary>
        public string Suggestion { get; set; } = string.Empty;
        /// <summary> 권장?�는 ?�시???�책 </summary>
        public RetryPolicy? RecommendedRetry { get; set; }
    }

    /// <summary>
    /// ?�류 메시지�?분석?�여 범주�?분류?�고 ?�???�책??결정?�는 ?�적 ?�래?�입?�다.
    /// </summary>
    public static class ErrorClassifier
    {
        /// <summary>
        /// ?�류 문자?�을 분석?�여 ErrorCategory�?반환?�니??
        /// </summary>
        /// <param name="toolName">발생???�구 ?�름</param>
        /// <param name="error">?�류 메시지 본문</param>
        /// <returns>분류???�류 범주</returns>
        public static ErrorCategory Classify(string toolName, string error)
        {
            if (string.IsNullOrEmpty(error)) return ErrorCategory.Unknown;

            var lower = error.ToLowerInvariant();

            // 1. ?�당??�??�한 (Quota & Limits)
            if (RegexMatch(lower, "quota|limit|exhausted|429"))
                return ErrorCategory.QuotaError;

            // 2. ?�트?�크 �??�결??(Network & Connectivity)
            if (RegexMatch(lower, "network|connection.*reset|dns|endpoint|socket|refused|unreachable"))
                return ErrorCategory.NetworkError;

            // 3. ?�간 초과 (Timeouts)
            if (RegexMatch(lower, "timeout|timed out|deadline|exceeded.*time"))
                return ErrorCategory.TimeoutError;

            // 4. 권한 관??(Permissions)
            if (RegexMatch(lower, "access denied|permission|unauthorized|forbidden|403|401"))
                return ErrorCategory.PermissionError;

            // 5. 경로 �??�일 ?�스??(Paths)
            if (RegexMatch(lower, "not found|find part of the path|directorynotfound|no such file"))
                return ErrorCategory.PathError;

            // 6. 빌드/컴파??(Build)
            if (RegexMatch(lower, "build failed|compile error|cs[0-9]{4}|msbuild|csc.exe|dotnet build"))
                return ErrorCategory.BuildError;

            // 7. ?�스??(Tests)
            if (RegexMatch(lower, "test failed|assertionfailed|xunit|nunit|failed.*tests"))
                return ErrorCategory.TestError;

            // 8. ?�공???�정 ?�류 (Provider Specific)
            if (RegexMatch(lower, "api key|provider error|anthropic-version|model.*not.*found"))
                return ErrorCategory.ProviderError;

            // 9. MCP ?�신 ?�류
            if (RegexMatch(lower, "mcp|jsonrpc|stdio-transport|server.*disconnected"))
                return ErrorCategory.McpError;

            // 10. 로직/?�자 ?�류 (Logic)
            if (RegexMatch(lower, "invalid argument|schema mismatch|not supported|unexpected format"))
                return ErrorCategory.LogicError;

            return ErrorCategory.ToolFailure;
        }

        /// <summary>
        /// ?�규??매칭???�행?�며, ?�패 ???��?검?�을 ?�행?�니??
        /// </summary>
        private static bool RegexMatch(string input, string pattern)
        {
            try { return System.Text.RegularExpressions.Regex.IsMatch(input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
            catch { return input.Contains(pattern.Split('|')[0]); } // ?�규???�류 ??�?번째 ?�워?�로 ?��?검??
        }

        /// <summary>
        /// ?�류 범주???�른 권장 ?�시???�책??가?�옵?�다.
        /// </summary>
        /// <param name="category">?�류 범주</param>
        /// <returns>권장 ?�시???�책</returns>
        public static RetryPolicy GetRecommendedPolicy(ErrorCategory category)
        {
            return category switch
            {
                // ?�당??초과??�?지???�간???�반??지??백오???�용
                ErrorCategory.QuotaError => new RetryPolicy { Strategy = RetryStrategy.ExponentialBackoff, MaxRetries = 5, InitialDelayMs = 5000 },
                // ?�트?�크 ?�류???�반?�인 지??백오???�용
                ErrorCategory.NetworkError => new RetryPolicy { Strategy = RetryStrategy.ExponentialBackoff, MaxRetries = 3, InitialDelayMs = 2000 },
                // ?�?�아?��? 고정 간격?�로 ?�간 ?��????�시??
                ErrorCategory.TimeoutError => new RetryPolicy { Strategy = RetryStrategy.FixedInterval, MaxRetries = 2, InitialDelayMs = 3000 },
                // ?�공???�류??짧�? ?��????�시??
                ErrorCategory.ProviderError => new RetryPolicy { Strategy = RetryStrategy.FixedInterval, MaxRetries = 3, InitialDelayMs = 1000 },
                // ?�반 ?�구 ?�패??1??즉시 ?�시???�도
                ErrorCategory.ToolFailure => new RetryPolicy { Strategy = RetryStrategy.Immediate, MaxRetries = 1 },
                // ?�머지???�시?�하지 ?�음
                _ => new RetryPolicy { Strategy = RetryStrategy.None }
            };
        }
    }
}
