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
                return "****" + value.Substring(Math.Max(0, value.Length - 2));
            }

            return value.Substring(0, 4) + "..." + value.Substring(value.Length - 4);
        }

        public static string MaskConnectionString(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return "(not set)";
            
            // Mask password in connection string
            // Match password=... or pwd=...
            var masked = Regex.Replace(connectionString, @"(password|pwd)\s*=\s*[^;]+", "$1=****", RegexOptions.IgnoreCase);
            return masked;
        }

        public static string MaskSensitiveInfo(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Simple heuristic masking for common patterns if found in logs/output
            // This is a safety net
            var masked = text;
            
            // Mask potential Bearer tokens
            masked = Regex.Replace(masked, @"Bearer\s+[a-zA-Z0-9\-\._~+/]+=*", "Bearer ****");
            
            return masked;
        }
    }
}
