using System;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 발생한 오류의 범주를 정의하는 열거형입니다.
    /// </summary>
    public enum ErrorCategory
    {
        /// <summary> 알 수 없는 오류 </summary>
        Unknown,
        /// <summary> 경로 또는 파일 찾기 오류 </summary>
        PathError,
        /// <summary> 컴파일 또는 빌드 오류 </summary>
        BuildError,
        /// <summary> 단위 테스트 실패 </summary>
        TestError,
        /// <summary> 권한 부족 오류 </summary>
        PermissionError,
        /// <summary> LLM 제공자 관련 오류 (API 키 등) </summary>
        ProviderError,
        /// <summary> 일반적인 도구 실행 실패 </summary>
        ToolFailure,
        /// <summary> MCP 프로토콜 통신 오류 </summary>
        McpError,
        /// <summary> 네트워크 연결 오류 </summary>
        NetworkError,
        /// <summary> 시간 초과 </summary>
        TimeoutError,
        /// <summary> 인자값 오류 등 로직 결함 </summary>
        LogicError,
        /// <summary> 할당량(Quota) 초과 오류 </summary>
        QuotaError
    }

    /// <summary>
    /// 재시도 전략을 정의하는 열거형입니다.
    /// </summary>
    public enum RetryStrategy
    {
        /// <summary> 재시도 안 함 </summary>
        None,
        /// <summary> 즉시 재시도 </summary>
        Immediate,
        /// <summary> 고정 간격 재시도 </summary>
        FixedInterval,
        /// <summary> 지수 백오프 (점진적으로 간격 증가) </summary>
        ExponentialBackoff,
        /// <summary> 서킷 브레이커 작동 </summary>
        CircuitBreaker
    }

    /// <summary>
    /// 재시도 정책을 설정하는 클래스입니다.
    /// </summary>
    public class RetryPolicy
    {
        /// <summary> 재시도 전략 </summary>
        public RetryStrategy Strategy { get; set; } = RetryStrategy.None;
        /// <summary> 최대 재시도 횟수 </summary>
        public int MaxRetries { get; set; } = 3;
        /// <summary> 초기 지연 시간 (ms) </summary>
        public int InitialDelayMs { get; set; } = 1000;
        /// <summary> 재시도 조건 (정규식 또는 태그) </summary>
        public string? Condition { get; set; } 
    }

    /// <summary>
    /// 자가 치유(Self-Healing)를 위한 진단 결과를 담는 클래스입니다.
    /// </summary>
    public class SelfHealingDiagnosis
    {
        /// <summary> 대상 도구 이름 </summary>
        public string ToolName { get; set; } = string.Empty;
        /// <summary> 진단된 오류 범주 </summary>
        public ErrorCategory Category { get; set; }
        /// <summary> 실패 횟수 </summary>
        public int FailureCount { get; set; }
        /// <summary> 공통적으로 나타나는 오류 메시지 요약 </summary>
        public string CommonErrorMessage { get; set; } = string.Empty;
        /// <summary> 해결을 위한 제안 내용 </summary>
        public string Suggestion { get; set; } = string.Empty;
        /// <summary> 권장되는 재시도 정책 </summary>
        public RetryPolicy? RecommendedRetry { get; set; }
    }

    /// <summary>
    /// 오류 메시지를 분석하여 범주를 분류하고 대응 정책을 결정하는 정적 클래스입니다.
    /// </summary>
    public static class ErrorClassifier
    {
        /// <summary>
        /// 오류 문자열을 분석하여 ErrorCategory를 반환합니다.
        /// </summary>
        /// <param name="toolName">발생한 도구 이름</param>
        /// <param name="error">오류 메시지 본문</param>
        /// <returns>분류된 오류 범주</returns>
        public static ErrorCategory Classify(string toolName, string error)
        {
            if (string.IsNullOrEmpty(error)) return ErrorCategory.Unknown;

            var lower = error.ToLowerInvariant();
            
            // 1. 할당량 및 제한 (Quota & Limits)
            if (RegexMatch(lower, "quota|limit|exhausted|429"))
                return ErrorCategory.QuotaError;

            // 2. 네트워크 및 연결성 (Network & Connectivity)
            if (RegexMatch(lower, "network|connection.*reset|dns|endpoint|socket|refused|unreachable"))
                return ErrorCategory.NetworkError;

            // 3. 시간 초과 (Timeouts)
            if (RegexMatch(lower, "timeout|timed out|deadline|exceeded.*time"))
                return ErrorCategory.TimeoutError;

            // 4. 권한 관련 (Permissions)
            if (RegexMatch(lower, "access denied|permission|unauthorized|forbidden|403|401"))
                return ErrorCategory.PermissionError;
            
            // 5. 경로 및 파일 시스템 (Paths)
            if (RegexMatch(lower, "not found|find part of the path|directorynotfound|no such file"))
                return ErrorCategory.PathError;
            
            // 6. 빌드/컴파일 (Build)
            if (RegexMatch(lower, "build failed|compile error|cs[0-9]{4}|msbuild|csc.exe|dotnet build"))
                return ErrorCategory.BuildError;
            
            // 7. 테스트 (Tests)
            if (RegexMatch(lower, "test failed|assertionfailed|xunit|nunit|failed.*tests"))
                return ErrorCategory.TestError;
            
            // 8. 제공자 특정 오류 (Provider Specific)
            if (RegexMatch(lower, "api key|provider error|anthropic-version|model.*not.*found"))
                return ErrorCategory.ProviderError;
            
            // 9. MCP 통신 오류
            if (RegexMatch(lower, "mcp|jsonrpc|stdio-transport|server.*disconnected"))
                return ErrorCategory.McpError;

            // 10. 로직/인자 오류 (Logic)
            if (RegexMatch(lower, "invalid argument|schema mismatch|not supported|unexpected format"))
                return ErrorCategory.LogicError;

            return ErrorCategory.ToolFailure;
        }

        /// <summary>
        /// 정규식 매칭을 수행하며, 실패 시 대체 검색을 수행합니다.
        /// </summary>
        private static bool RegexMatch(string input, string pattern)
        {
            try { return System.Text.RegularExpressions.Regex.IsMatch(input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
            catch { return input.Contains(pattern.Split('|')[0]); } // 정규식 오류 시 첫 번째 키워드로 대체 검색
        }

        /// <summary>
        /// 오류 범주에 따른 권장 재시도 정책을 가져옵니다.
        /// </summary>
        /// <param name="category">오류 범주</param>
        /// <returns>권장 재시도 정책</returns>
        public static RetryPolicy GetRecommendedPolicy(ErrorCategory category)
        {
            return category switch
            {
                // 할당량 초과는 긴 지연 시간을 동반한 지수 백오프 적용
                ErrorCategory.QuotaError => new RetryPolicy { Strategy = RetryStrategy.ExponentialBackoff, MaxRetries = 5, InitialDelayMs = 5000 },
                // 네트워크 오류는 일반적인 지수 백오프 적용
                ErrorCategory.NetworkError => new RetryPolicy { Strategy = RetryStrategy.ExponentialBackoff, MaxRetries = 3, InitialDelayMs = 2000 },
                // 타임아웃은 고정 간격으로 약간 대기 후 재시도
                ErrorCategory.TimeoutError => new RetryPolicy { Strategy = RetryStrategy.FixedInterval, MaxRetries = 2, InitialDelayMs = 3000 },
                // 제공자 오류는 짧은 대기 후 재시도
                ErrorCategory.ProviderError => new RetryPolicy { Strategy = RetryStrategy.FixedInterval, MaxRetries = 3, InitialDelayMs = 1000 },
                // 일반 도구 실패는 1회 즉시 재시도 시도
                ErrorCategory.ToolFailure => new RetryPolicy { Strategy = RetryStrategy.Immediate, MaxRetries = 1 },
                // 나머지는 재시도하지 않음
                _ => new RetryPolicy { Strategy = RetryStrategy.None }
            };
        }
    }
}
