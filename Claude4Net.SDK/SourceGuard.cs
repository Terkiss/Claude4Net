using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

namespace Claude4Net.SDK
{
    public class RedactionResult
    {
        public string OriginalText { get; set; } = string.Empty;
        public string FilteredText { get; set; } = string.Empty;
        public List<string> FoundTypes { get; set; } = new();
        public int TotalMatches { get; set; }
        public bool IsClean => TotalMatches == 0;
    }

    public static class SourceGuard
    {
        private static readonly List<(string Name, Regex Pattern)> _filters = new()
        {
            ("API Key", new Regex(@"\b(sk-ant-[a-zA-Z0-9_\-]{16,}|sk-[a-zA-Z0-9]{20,}|AIza[0-9A-Za-z_\-]{20,}|gh[pousr]_[A-Za-z0-9_]{20,})\b", RegexOptions.Compiled)),
            ("Discord Token", new Regex(@"([a-zA-Z0-9_\-]{24}\.[a-zA-Z0-9_\-]{6}\.[a-zA-Z0-9_\-]{27})", RegexOptions.Compiled)),
            ("Authorization Bearer", new Regex(@"(Bearer\s+[a-zA-Z0-9\-\._~+/]+=*)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Connection String Password", new Regex(@"(password|pwd)\s*=\s*([^;]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Generic Password", new Regex(@"(password|pass|secret)\s*[:=]\s*([^\s,;]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Email", new Regex(@"([a-zA-Z0-9_\-\.]+)@([a-zA-Z0-9_\-\.]+)\.([a-zA-Z]{2,5})", RegexOptions.Compiled))
        };

        private static readonly string[] _sensitiveKeyParts =
        {
            "KEY",
            "TOKEN",
            "SECRET",
            "PASSWORD",
            "PASS",
            "PWD",
            "AUTH",
            "CONNECTION",
            "CREDENTIAL"
        };

        public static RedactionResult Filter(string? input)
        {
            var result = new RedactionResult { OriginalText = input ?? "" };
            if (string.IsNullOrEmpty(input))
            {
                result.FilteredText = "";
                return result;
            }

            string filtered = input;
            foreach (var filter in _filters)
            {
                var matches = filter.Pattern.Matches(filtered);
                if (matches.Count > 0)
                {
                    result.TotalMatches += matches.Count;
                    if (!result.FoundTypes.Contains(filter.Name))
                        result.FoundTypes.Add(filter.Name);

                    filtered = filter.Pattern.Replace(filtered, m => 
                    {
                        // Specific handling for groups if needed
                        if (filter.Name == "Connection String Password" || filter.Name == "Generic Password")
                        {
                            return m.Groups[1].Value + "=****";
                        }
                        return "****";
                    });
                }
            }

            result.FilteredText = filtered;
            return result;
        }

        public static string MaskValue(string? value, string? keyName = null)
        {
            if (string.IsNullOrEmpty(value)) return "(not set)";
            
            var result = Filter(value);
            if (result.IsClean)
            {
                if (LooksSensitiveKey(keyName))
                    return SecurityUtils.Mask(value);
                return value;
            }
            return result.FilteredText;
        }

        public static bool LooksSensitiveKey(string? keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return false;

            return _sensitiveKeyParts.Any(part =>
                keyName.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
