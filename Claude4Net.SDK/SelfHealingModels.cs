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
        McpError
    }

    public class SelfHealingDiagnosis
    {
        public string ToolName { get; set; } = string.Empty;
        public ErrorCategory Category { get; set; }
        public int FailureCount { get; set; }
        public string CommonErrorMessage { get; set; } = string.Empty;
        public string Suggestion { get; set; } = string.Empty;
    }

    public static class ErrorClassifier
    {
        public static ErrorCategory Classify(string toolName, string error)
        {
            if (string.IsNullOrEmpty(error)) return ErrorCategory.Unknown;

            var lower = error.ToLowerInvariant();
            
            if (lower.Contains("access denied") || lower.Contains("permission") || lower.Contains("unauthorized") || lower.Contains("denied"))
                return ErrorCategory.PermissionError;
            
            if (lower.Contains("not found") || lower.Contains("could not find part of the path") || lower.Contains("directorynotfound"))
                return ErrorCategory.PathError;
            
            if (lower.Contains("build failed") || lower.Contains("compile error") || lower.Contains("cs0") || lower.Contains("msbuild"))
                return ErrorCategory.BuildError;
            
            if (lower.Contains("test failed") || lower.Contains("assertionfailed") || lower.Contains("xunit"))
                return ErrorCategory.TestError;
            
            if (lower.Contains("api key") || lower.Contains("rate limit") || lower.Contains("quota exceeded") || lower.Contains("provider error"))
                return ErrorCategory.ProviderError;
            
            if (lower.Contains("mcp") || lower.Contains("jsonrpc"))
                return ErrorCategory.McpError;

            return ErrorCategory.ToolFailure;
        }
    }
}
