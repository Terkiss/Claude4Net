using System;
using System.Text.RegularExpressions;

namespace Claude4Net.SDK
{
    public static class SecurityUtils
    {
        public static string Mask(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "(not set)";
            
            if (value.Length <= 8)
            {
                return "****" + (value.Length > 2 ? value.Substring(value.Length - 2) : "");
            }

            // Reveal only first 3 and last 3 characters for longer tokens
            return value.Substring(0, 3) + "..." + value.Substring(value.Length - 3);
        }

        public static string MaskConnectionString(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return "(not set)";
            
            // Mask password in connection string using SourceGuard's refined logic
            return SourceGuard.Filter(connectionString).FilteredText;
        }

        public static string MaskSensitiveInfo(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Use SourceGuard for comprehensive masking
            return SourceGuard.Filter(text).FilteredText;
        }
    }
}
