using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class QueryRouter
    {
        public static string? Route(string input)
        {
            string text = input.Trim().ToLower();

            // Simple Keyword Mapping to System Commands
            if (Regex.IsMatch(text, @"\b(build|빌드)\b")) return "!build";
            if (Regex.IsMatch(text, @"\b(test|테스트)\b")) return "!test";
            if (Regex.IsMatch(text, @"\b(clean|클린|청소)\b")) return "!clean";
            if (Regex.IsMatch(text, @"\b(status|상태)\b")) return "!status";
            
            return null; // No match, proceed to LLM
        }
    }
}
