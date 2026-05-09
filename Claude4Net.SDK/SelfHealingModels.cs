using System;

namespace Claude4Net.SDK
{
    /// <summary>
    /// ë°œìƒ???¤ë¥˜??ë²”ì£¼ë¥??•ì˜?˜ëŠ” ?´ê±°?•ì…?ˆë‹¤.
    /// </summary>
    public enum ErrorCategory
    {
        /// <summary> ?????†ëŠ” ?¤ë¥˜ </summary>
        Unknown,
        /// <summary> ê²½ë¡œ ?ëŠ” ?Œì¼ ì°¾ê¸° ?¤ë¥˜ </summary>
        PathError,
        /// <summary> ì»´íŒŒ???ëŠ” ë¹Œë“œ ?¤ë¥˜ </summary>
        BuildError,
        /// <summary> ?¨ìœ„ ?ŒìŠ¤???¤íŒ¨ </summary>
        TestError,
        /// <summary> ê¶Œí•œ ë¶€ì¡??¤ë¥˜ </summary>
        PermissionError,
        /// <summary> LLM ?œê³µ??ê´€???¤ë¥˜ (API ???? </summary>
        ProviderError,
        /// <summary> ?¼ë°˜?ì¸ ?„êµ¬ ?¤í–‰ ?¤íŒ¨ </summary>
        ToolFailure,
        /// <summary> MCP ?„ë¡œ? ì½œ ?µì‹  ?¤ë¥˜ </summary>
        McpError,
        /// <summary> ?¤íŠ¸?Œí¬ ?°ê²° ?¤ë¥˜ </summary>
        NetworkError,
        /// <summary> ?œê°„ ì´ˆê³¼ </summary>
        TimeoutError,
        /// <summary> ?¸ìê°??¤ë¥˜ ??ë¡œì§ ê²°í•¨ </summary>
        LogicError,
        /// <summary> ? ë‹¹??Quota) ì´ˆê³¼ ?¤ë¥˜ </summary>
        QuotaError,
        /// <summary> ë¬´í•œ ë£¨í”„ ê°ì? </summary>
        InfiniteLoop,
        /// <summary> ?˜ê°(Hallucination) ê°ì? </summary>
        Hallucination
    }

    /// <summary>
    /// ?ì´?„íŠ¸ ?¤íŒ¨ ?¨í„´???•ì˜?©ë‹ˆ??
    /// </summary>
    public enum FailurePattern
    {
        None,
        /// <summary> ?™ì¼???„êµ¬/?¸ìë¡?ë°˜ë³µ ?¸ì¶œ?˜ëŠ” ë¬´í•œ ë£¨í”„ </summary>
        InfiniteLoop,
        /// <summary> ì¡´ì¬?˜ì? ?ŠëŠ” ?Œì¼/?¨ìˆ˜ë¥??¬ìš©?˜ëŠ” ?˜ê° </summary>
        Hallucination,
        /// <summary> ?„êµ¬ ?¤í‚¤ë§ˆë? ì¤€?˜í•˜ì§€ ?ŠëŠ” ë¬¸ë²• ?¤ë¥˜ </summary>
        ToolUsageError,
        /// <summary> ë³´ì•ˆ ?•ì±… ?„ë°˜?¼ë¡œ ?¸í•œ ë°˜ë³µ ê±°ë? </summary>
        SecurityRejection,
        /// <summary> ?´ê²°?˜ì? ?ŠëŠ” ì¢…ì†???¤ë¥˜ </summary>
        DependencyHell
    }

    /// <summary>
    /// ?ì´?„íŠ¸ êµì •???„í•œ ì¹˜ìœ  ì§€ì¹??´ë˜?¤ì…?ˆë‹¤.
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
    /// ?¬ì‹œ???„ëµ???•ì˜?˜ëŠ” ?´ê±°?•ì…?ˆë‹¤.
    /// </summary>
    public enum RetryStrategy
    {
        /// <summary> ?¬ì‹œ??????</summary>
        None,
        /// <summary> ì¦‰ì‹œ ?¬ì‹œ??</summary>
        Immediate,
        /// <summary> ê³ ì • ê°„ê²© ?¬ì‹œ??</summary>
        FixedInterval,
        /// <summary> ì§€??ë°±ì˜¤??(?ì§„?ìœ¼ë¡?ê°„ê²© ì¦ê?) </summary>
        ExponentialBackoff,
        /// <summary> ?œí‚· ë¸Œë ˆ?´ì»¤ ?‘ë™ </summary>
        CircuitBreaker
    }

    /// <summary>
    /// ?¬ì‹œ???•ì±…???¤ì •?˜ëŠ” ?´ë˜?¤ì…?ˆë‹¤.
    /// </summary>
    public class RetryPolicy
    {
        /// <summary> ?¬ì‹œ???„ëµ </summary>
        public RetryStrategy Strategy { get; set; } = RetryStrategy.None;
        /// <summary> ìµœë? ?¬ì‹œ???Ÿìˆ˜ </summary>
        public int MaxRetries { get; set; } = 3;
        /// <summary> ì´ˆê¸° ì§€???œê°„ (ms) </summary>
        public int InitialDelayMs { get; set; } = 1000;
        /// <summary> ?¬ì‹œ??ì¡°ê±´ (?•ê·œ???ëŠ” ?œê·¸) </summary>
        public string? Condition { get; set; }
    }

    /// <summary>
    /// ?ê? ì¹˜ìœ (Self-Healing)ë¥??„í•œ ì§„ë‹¨ ê²°ê³¼ë¥??´ëŠ” ?´ë˜?¤ì…?ˆë‹¤.
    /// </summary>
    public class SelfHealingDiagnosis
    {
        /// <summary> ?€???„êµ¬ ?´ë¦„ </summary>
        public string ToolName { get; set; } = string.Empty;
        /// <summary> ì§„ë‹¨???¤ë¥˜ ë²”ì£¼ </summary>
        public ErrorCategory Category { get; set; }
        /// <summary> ?¤íŒ¨ ?Ÿìˆ˜ </summary>
        public int FailureCount { get; set; }
        /// <summary> ê³µí†µ?ìœ¼ë¡??˜í??˜ëŠ” ?¤ë¥˜ ë©”ì‹œì§€ ?”ì•½ </summary>
        public string CommonErrorMessage { get; set; } = string.Empty;
        /// <summary> ?´ê²°???„í•œ ?œì•ˆ ?´ìš© </summary>
        public string Suggestion { get; set; } = string.Empty;
        /// <summary> ê¶Œì¥?˜ëŠ” ?¬ì‹œ???•ì±… </summary>
        public RetryPolicy? RecommendedRetry { get; set; }
    }

    /// <summary>
    /// ?¤ë¥˜ ë©”ì‹œì§€ë¥?ë¶„ì„?˜ì—¬ ë²”ì£¼ë¥?ë¶„ë¥˜?˜ê³  ?€???•ì±…??ê²°ì •?˜ëŠ” ?•ì  ?´ë˜?¤ì…?ˆë‹¤.
    /// </summary>
    public static class ErrorClassifier
    {
        /// <summary>
        /// ?¤ë¥˜ ë¬¸ì?´ì„ ë¶„ì„?˜ì—¬ ErrorCategoryë¥?ë°˜í™˜?©ë‹ˆ??
        /// </summary>
        /// <param name="toolName">ë°œìƒ???„êµ¬ ?´ë¦„</param>
        /// <param name="error">?¤ë¥˜ ë©”ì‹œì§€ ë³¸ë¬¸</param>
        /// <returns>ë¶„ë¥˜???¤ë¥˜ ë²”ì£¼</returns>
        public static ErrorCategory Classify(string toolName, string error)
        {
            if (string.IsNullOrEmpty(error)) return ErrorCategory.Unknown;

            var lower = error.ToLowerInvariant();

            // 1. ? ë‹¹??ë°??œí•œ (Quota & Limits)
            if (RegexMatch(lower, "quota|limit|exhausted|429"))
                return ErrorCategory.QuotaError;

            // 2. ?¤íŠ¸?Œí¬ ë°??°ê²°??(Network & Connectivity)
            if (RegexMatch(lower, "network|connection.*reset|dns|endpoint|socket|refused|unreachable"))
                return ErrorCategory.NetworkError;

            // 3. ?œê°„ ì´ˆê³¼ (Timeouts)
            if (RegexMatch(lower, "timeout|timed out|deadline|exceeded.*time"))
                return ErrorCategory.TimeoutError;

            // 4. ê¶Œí•œ ê´€??(Permissions)
            if (RegexMatch(lower, "access denied|permission|unauthorized|forbidden|403|401"))
                return ErrorCategory.PermissionError;

            // 5. ê²½ë¡œ ë°??Œì¼ ?œìŠ¤??(Paths)
            if (RegexMatch(lower, "not found|find part of the path|directorynotfound|no such file"))
                return ErrorCategory.PathError;

            // 6. ë¹Œë“œ/ì»´íŒŒ??(Build)
            if (RegexMatch(lower, "build failed|compile error|cs[0-9]{4}|msbuild|csc.exe|dotnet build"))
                return ErrorCategory.BuildError;

            // 7. ?ŒìŠ¤??(Tests)
            if (RegexMatch(lower, "test failed|assertionfailed|xunit|nunit|failed.*tests"))
                return ErrorCategory.TestError;

            // 8. ?œê³µ???¹ì • ?¤ë¥˜ (Provider Specific)
            if (RegexMatch(lower, "api key|provider error|anthropic-version|model.*not.*found"))
                return ErrorCategory.ProviderError;

            // 9. MCP ?µì‹  ?¤ë¥˜
            if (RegexMatch(lower, "mcp|jsonrpc|stdio-transport|server.*disconnected"))
                return ErrorCategory.McpError;

            // 10. ë¡œì§/?¸ì ?¤ë¥˜ (Logic)
            if (RegexMatch(lower, "invalid argument|schema mismatch|not supported|unexpected format"))
                return ErrorCategory.LogicError;

            return ErrorCategory.ToolFailure;
        }

        /// <summary>
        /// ?•ê·œ??ë§¤ì¹­???˜í–‰?˜ë©°, ?¤íŒ¨ ???€ì²?ê²€?‰ì„ ?˜í–‰?©ë‹ˆ??
        /// </summary>
        private static bool RegexMatch(string input, string pattern)
        {
            try { return System.Text.RegularExpressions.Regex.IsMatch(input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
            catch { return input.Contains(pattern.Split('|')[0]); } // ?•ê·œ???¤ë¥˜ ??ì²?ë²ˆì§¸ ?¤ì›Œ?œë¡œ ?€ì²?ê²€??
        }

        /// <summary>
        /// ?¤ë¥˜ ë²”ì£¼???°ë¥¸ ê¶Œì¥ ?¬ì‹œ???•ì±…??ê°€?¸ì˜µ?ˆë‹¤.
        /// </summary>
        /// <param name="category">?¤ë¥˜ ë²”ì£¼</param>
        /// <returns>ê¶Œì¥ ?¬ì‹œ???•ì±…</returns>
        public static RetryPolicy GetRecommendedPolicy(ErrorCategory category)
        {
            return category switch
            {
                // ? ë‹¹??ì´ˆê³¼??ê¸?ì§€???œê°„???™ë°˜??ì§€??ë°±ì˜¤???ìš©
                ErrorCategory.QuotaError => new RetryPolicy { Strategy = RetryStrategy.ExponentialBackoff, MaxRetries = 5, InitialDelayMs = 5000 },
                // ?¤íŠ¸?Œí¬ ?¤ë¥˜???¼ë°˜?ì¸ ì§€??ë°±ì˜¤???ìš©
                ErrorCategory.NetworkError => new RetryPolicy { Strategy = RetryStrategy.ExponentialBackoff, MaxRetries = 3, InitialDelayMs = 2000 },
                // ?€?„ì•„?ƒì? ê³ ì • ê°„ê²©?¼ë¡œ ?½ê°„ ?€ê¸????¬ì‹œ??
                ErrorCategory.TimeoutError => new RetryPolicy { Strategy = RetryStrategy.FixedInterval, MaxRetries = 2, InitialDelayMs = 3000 },
                // ?œê³µ???¤ë¥˜??ì§§ì? ?€ê¸????¬ì‹œ??
                ErrorCategory.ProviderError => new RetryPolicy { Strategy = RetryStrategy.FixedInterval, MaxRetries = 3, InitialDelayMs = 1000 },
                // ?¼ë°˜ ?„êµ¬ ?¤íŒ¨??1??ì¦‰ì‹œ ?¬ì‹œ???œë„
                ErrorCategory.ToolFailure => new RetryPolicy { Strategy = RetryStrategy.Immediate, MaxRetries = 1 },
                // ?˜ë¨¸ì§€???¬ì‹œ?„í•˜ì§€ ?ŠìŒ
                _ => new RetryPolicy { Strategy = RetryStrategy.None }
            };
        }
    }
}
